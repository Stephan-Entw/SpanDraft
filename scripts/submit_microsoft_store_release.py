"""Upload a reviewed release folder to the Microsoft Store and submit it for certification.

Usage (from main, any OS):
    python scripts/submit_microsoft_store_release.py artifacts/store-release/<version>_<timestamp>
        [--replace-draft <submission ID>]
    python scripts/submit_microsoft_store_release.py <folder> --check
    python scripts/submit_microsoft_store_release.py --check-credentials
    python scripts/submit_microsoft_store_release.py --clear-state <token>

Run it only after reviewing the folder from scripts/prepare-microsoft-store-release.ps1.
The script uses the Microsoft Store submission API (https://manage.devcenter.microsoft.com):

  1. validates the folder, the Store state and the upload archive before writing anything,
  2. creates a submission from the last published one, replaces the SpanDraft x64 package,
     "What's new" and the screenshots and forces publish mode Manual (PUT), then uploads the files,
  3. reads the submission back and, only if nothing else changed, COMMITS it: it goes to certification,
  4. waits until Microsoft has processed the commit.

It never publishes. After certification, review in Partner Center and choose "Publish now".
Until the commit, Partner Center does not show uploaded files.

Every run keeps one journal per app on this computer (see journal_directory). If the outcome of a
create or commit request is unclear, every later run only reads, from any release folder, until the
user has checked Partner Center and archived the journal with --clear-state. Submit from one computer
only: the journal does not exist on others.

An unpublished draft (PendingCommit or CommitFailed) is only deleted with --replace-draft and its ID.

Credentials (Partner Center > Account settings > User management > Microsoft Entra applications,
role Manager) are MS_STORE_TENANT_ID, MS_STORE_CLIENT_ID and MS_STORE_CLIENT_SECRET. On Windows they
are read from Bitwarden Secrets Manager (see bitwarden_secrets.py); if all three are set as
environment variables, those are used instead.
--check only validates the folder and contacts nothing.
--check-credentials signs in and reads the app from the Store; it changes nothing.
--clear-state archives the local journal only. It confirms nothing in the Store: check the actual
state in Partner Center first.
"""

import argparse
import contextlib
import hashlib
import http.client
import json
import os
import re
import struct
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import Path

if __package__:
    from . import bitwarden_secrets
    from .step_progress import Steps
else:
    import bitwarden_secrets
    from step_progress import Steps


BITWARDEN_PROJECT = "SpanDraft"
CREDENTIAL_NAMES = ("MS_STORE_TENANT_ID", "MS_STORE_CLIENT_ID", "MS_STORE_CLIENT_SECRET")
APPLICATION_ID = "9NVWB1M86PDZ"
API = "https://manage.devcenter.microsoft.com/v1.0/my"
CULTURES = ("de-DE", "en-US")
WHATS_NEW_LIMIT = 1500
MAX_SCREENSHOTS = 10
MIN_SIZE, MAX_SIZE = (1366, 768), (3840, 2160)
MAX_IMAGE_BYTES = 50 * 1024 * 1024
APP_PACKAGE = re.compile(r"SpanDraft-(\d+)\.(\d+)\.(\d+)\.0-x64\.msix")
POLL_SECONDS, POLL_TIMEOUT = 20, 20 * 60

# Submission statuses as documented for the submission API; anything else is treated as unknown.
DELETABLE = {"PendingCommit", "CommitFailed"}  # Never submitted successfully: --replace-draft may delete.
PROCESSING = {"CommitStarted", "PreProcessing", "Certification", "Release", "PendingPublication", "Publishing"}
FAILED = {"CommitFailed", "PreProcessingFailed", "CertificationFailed", "ReleaseFailed", "PublishFailed", "Canceled"}
KNOWN = DELETABLE | PROCESSING | FAILED | {"Published"}
SUBMITTED = (PROCESSING - {"CommitStarted"}) | {"Published"}
# Final message per SUBMITTED status (publish mode Manual is confirmed before): submitted, certified, published.
_CERTIFICATION_PENDING = ("NOT published. Certification is still pending: Microsoft is preparing the submission. "
                          "Publishing stays your decision in Partner Center.")
_WAIT_FOR_CERTIFICATION = "NOT published. Certification is running; publishing stays your decision in Partner Center."
_READY_TO_PUBLISH = 'NOT published. Certification passed: select "Publish now" in Partner Center when you are ready.'
OUTCOMES = {
    "PreProcessing": ("Submitted for certification", _CERTIFICATION_PENDING),
    "Certification": ("In certification", _WAIT_FOR_CERTIFICATION),
    "Release": ("Certified, not published", "NOT published. Check the release status in Partner Center."),
    "PendingPublication": ("Certified, not published", _READY_TO_PUBLISH),
    "Publishing": ("Publishing (started in Partner Center)", None),
    "Published": ("Published", None),
}
MISSING = "<missing>"  # The submission does not exist (HTTP 404).

# Journal stages. After an unclear request, only reading is allowed until --clear-state.
UNCLEAR = {"create-requested", "commit-requested"}
STAGES = UNCLEAR | {"delete-requested", "created", "uploaded", "committed"}

# Fields that Partner Center assigns or ignores (submission API resource documentation).
SERVER_FIELDS = ("id", "status", "statusDetails", "fileUploadUrl", "friendlyName")
IGNORED_LISTING_FIELDS = ("privacyPolicy", "supportContact", "websiteUrl")
PACKAGE_FIELDS = ("fileName", "fileStatus", "minimumDirectXVersion", "minimumSystemRam")
# Observed in the first real run (1.2.0): the Store returns these differently without any real change.
IGNORED_PRICING_FIELDS = ("sales", "isAdvancedPricingModel")  # sales: unsupported; the other: read-only.
IGNORED_REPLACED_PACKAGE_FIELDS = ("targetPlatform",)  # Dropped from packages marked PendingDelete.
TRIMMED_LISTING_TEXTS = ("releaseNotes", "licenseTerms")  # Line breaks and outer whitespace only.


class Release:
    """A validated release folder and the identity of everything that goes to the Store."""

    def __init__(self, root):
        self.root = Path(root).resolve()
        manifest_path = self.root / "release.json"
        if not manifest_path.is_file():
            raise ValueError(f"{manifest_path} not found; run prepare-microsoft-store-release.ps1 first.")
        self.manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        self.version = self.manifest["version"]
        self.package = self._file(self.manifest["package"]["file"])
        if not self.package.name.endswith(f"{self.version}.0-x64.msix"):
            raise ValueError(f"Package {self.package.name} does not match version {self.version}.")
        package_hash = _sha256(self.package)
        if package_hash != self.manifest["package"]["sha256"]:
            raise ValueError(f"{self.package.name} does not match its recorded SHA-256.")
        self.whats_new, self.screenshots = {}, {}
        hashes = {"whatsNew": {}, "screenshots": {}}
        for culture in CULTURES:
            text = self._file(self.manifest["whatsNew"][culture]).read_text(encoding="utf-8").strip()
            if not text or len(text) > WHATS_NEW_LIMIT:
                raise ValueError(f"What's new for {culture} must have 1 to {WHATS_NEW_LIMIT} characters, has {len(text)}.")
            self.whats_new[culture] = text
            hashes["whatsNew"][culture] = hashlib.sha256(text.encode("utf-8")).hexdigest()
            files = [self._file(name) for name in self.manifest["screenshots"][culture]]
            if not 1 <= len(files) <= MAX_SCREENSHOTS or "hero" not in files[0].name:
                raise ValueError(f"{culture} needs 1 to {MAX_SCREENSHOTS} screenshots with the hero image first.")
            for path in files:
                width, height = _png_size(path)
                if not (MIN_SIZE[0] <= width <= MAX_SIZE[0] and MIN_SIZE[1] <= height <= MAX_SIZE[1]):
                    raise ValueError(f"{path.name} is {width}x{height}; the Store accepts 1366x768 to 3840x2160.")
                if path.stat().st_size > MAX_IMAGE_BYTES:
                    raise ValueError(f"{path.name} exceeds 50 MB.")
            self.screenshots[culture] = files
            hashes["screenshots"][culture] = [[self.relative(path), _sha256(path)] for path in files]
        # Everything that reaches the Store, in order. Computed from the files, so existing folders work.
        self.content = {
            "applicationId": APPLICATION_ID, "version": self.version,
            "tag": self.manifest.get("tag"), "commit": self.manifest.get("commit"),
            "package": [self.package.name, package_hash], **hashes,
        }
        self.fingerprint = hashlib.sha256(json.dumps(self.content, sort_keys=True).encode("utf-8")).hexdigest()

    def _file(self, relative):
        path = (self.root / relative).resolve()
        if self.root not in path.parents or not path.is_file():
            raise ValueError(f"Missing or invalid release file: {relative}")
        return path

    def relative(self, path):
        return path.relative_to(self.root).as_posix()

    def upload_hashes(self):
        """{archive entry name: SHA-256} of the upload archive."""
        entries = {self.package.name: self.content["package"][1]}
        for files in self.content["screenshots"].values():
            entries.update({name: digest for name, digest in files})
        return entries


def content_changes(old, new):
    """Name what differs between two Release.content values."""
    changes = [key for key in ("version", "tag", "commit", "package") if old.get(key) != new.get(key)]
    for kind in ("whatsNew", "screenshots"):
        for culture in sorted(set(old.get(kind) or {}) | set(new.get(kind) or {})):
            if (old.get(kind) or {}).get(culture) != (new.get(kind) or {}).get(culture):
                changes.append(f"{kind} {culture}")
    return changes or ["release identity"]


def _sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _png_size(path):
    with path.open("rb") as stream:
        header = stream.read(24)
    if header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise ValueError(f"{path.name} is not a PNG file.")
    return struct.unpack(">II", header[16:24])


def culture_for(listing_language):
    """Map a Store listing key such as 'de-de' or 'en' to the release culture."""
    language = listing_language.split("-")[0].lower()
    matches = [c for c in CULTURES if c.split("-")[0].lower() == language]
    if len(matches) != 1:
        raise ValueError(f"The Store listing '{listing_language}' has no matching release language {CULTURES}.")
    return matches[0]


def _package_version(name):
    match = APP_PACKAGE.fullmatch(name or "")
    return tuple(int(part) for part in match.groups()) if match else None


def prepare_submission(submission, release):
    """Return the updated body: Manual publishing, the package replaced, new texts and screenshots.

    Existing entries are marked PendingDelete and new ones added as PendingUpload, as in Microsoft's
    AppSubmissionUpdateSample. Applying it to its own result changes nothing further.
    """
    body = json.loads(json.dumps(submission))
    body["targetPublishMode"] = "Manual"
    if isinstance(body.get("pricing"), dict):
        # The Sale resource is no longer supported; sending it causes SalesUnsupportedWarning.
        body["pricing"].pop("sales", None)
    new = release.package.name
    version = _package_version(new)
    if version is None:
        raise ValueError(f"{new} is not a SpanDraft x64 package name.")
    packages, replaced = [], None
    for package in body.get("applicationPackages") or []:
        old = _package_version(package.get("fileName"))
        if old is None:
            packages.append(package)  # Not the SpanDraft x64 package: keep as it is.
            continue
        if package.get("fileStatus") == "PendingUpload":
            continue  # Added again below.
        if old >= version:
            raise ValueError(f"The Store already has {package['fileName']}; {new} is not newer.")
        replaced = package
        packages.append(dict(package, fileStatus="PendingDelete"))
    packages.append({
        "fileName": new, "fileStatus": "PendingUpload",
        "minimumDirectXVersion": (replaced or {}).get("minimumDirectXVersion") or "None",
        "minimumSystemRam": (replaced or {}).get("minimumSystemRam") or "None",
    })
    body["applicationPackages"] = packages
    listings = body.get("listings") or {}
    if not listings:
        raise ValueError("The submission has no Store listings.")
    covered = set()
    for language, listing in listings.items():
        culture = culture_for(language)
        covered.add(culture)
        base = listing.setdefault("baseListing", {})
        base["releaseNotes"] = release.whats_new[culture]
        images = []
        for image in base.get("images") or []:
            if image.get("imageType") != "Screenshot":
                images.append(image)
            elif image.get("fileStatus") != "PendingUpload":
                images.append(dict(image, fileStatus="PendingDelete"))
        images += [{"fileName": release.relative(path), "fileStatus": "PendingUpload", "imageType": "Screenshot"}
                   for path in release.screenshots[culture]]
        base["images"] = images
    missing = set(CULTURES) - covered
    if missing:
        raise ValueError(f"The Store has no listing for {sorted(missing)}; add it in Partner Center first.")
    return body


def _comparable(submission):
    """A copy without what Partner Center assigns or ignores; nothing else is normalized.

    Missing and null stay different except for 'description' of new screenshots, which we never send.
    The IGNORED_* and TRIMMED_* lists name every other exception.
    """
    value = json.loads(json.dumps(submission))
    for key in SERVER_FIELDS:
        value.pop(key, None)
    if isinstance(value.get("pricing"), dict):
        for key in IGNORED_PRICING_FIELDS:
            value["pricing"].pop(key, None)
    rollout = (value.get("packageDeliveryOptions") or {}).get("packageRollout")
    if isinstance(rollout, dict):
        rollout.pop("packageRolloutStatus", None)  # Assigned by Partner Center.
        rollout.pop("fallbackSubmissionId", None)
    packages = {}
    for package in value.get("applicationPackages") or []:
        name = package.get("fileName")
        if package.get("fileStatus") == "PendingUpload":
            # Partner Center populates every other field of a new package.
            package = {key: package[key] for key in PACKAGE_FIELDS if key in package}
        elif package.get("fileStatus") == "PendingDelete":
            package = {key: item for key, item in package.items() if key not in IGNORED_REPLACED_PACKAGE_FIELDS}
        packages[name if name not in packages else f"{name} (duplicate)"] = package
    if "applicationPackages" in value:
        value["applicationPackages"] = packages
    for listing in (value.get("listings") or {}).values():
        base = listing.get("baseListing") if isinstance(listing, dict) else None
        if not isinstance(base, dict):
            continue
        for key in IGNORED_LISTING_FIELDS:
            base.pop(key, None)
        for key in TRIMMED_LISTING_TEXTS:
            if isinstance(base.get(key), str):
                base[key] = base[key].replace("\r\n", "\n").strip()
        if "images" in base:
            new, others = [], []
            for image in base["images"] or []:
                if image.get("imageType") == "Screenshot" and image.get("fileStatus") == "PendingUpload":
                    image = dict(image, fileName=str(image.get("fileName")).replace("\\", "/"))
                    image.pop("id", None)  # Supplied by Partner Center.
                    if image.get("description", 0) is None:
                        image.pop("description")
                    new.append(image)
                else:
                    others.append(image)
            base["images"] = {"new screenshots": new,
                              "others": sorted(others, key=lambda i: json.dumps(i, sort_keys=True))}
    return value


def differences(actual, expected, path="submission"):
    """Paths where two JSON values differ."""
    if isinstance(actual, dict) and isinstance(expected, dict):
        found = []
        for key in sorted(set(actual) | set(expected), key=str):
            if key not in expected:
                found.append(f"{path}.{key} is unexpected")
            elif key not in actual:
                found.append(f"{path}.{key} is missing")
            else:
                found += differences(actual[key], expected[key], f"{path}.{key}")
        return found
    if isinstance(actual, list) and isinstance(expected, list) and len(actual) == len(expected):
        found = []
        for index, (left, right) in enumerate(zip(actual, expected)):
            found += differences(left, right, f"{path}[{index}]")
        return found
    return [] if actual == expected and type(actual) is type(expected) else [f"{path} differs"]


def verify_clone(draft, published):
    """The new submission must equal the published one except for what Partner Center assigns."""
    return differences(_comparable(draft), _comparable(published))


def verify_submission(stored, baseline, release, submission_id):
    """Compare what the Store has saved with the intended change of the baseline; return the problems.

    This checks metadata and file references. The Store reads the uploaded files only after the commit.
    """
    problems = []
    if stored.get("id") != submission_id:
        problems.append(f"the Store returned submission {stored.get('id')!r}, not {submission_id}")
    if stored.get("targetPublishMode") != "Manual":
        problems.append(f"publish mode is {stored.get('targetPublishMode')!r}, not 'Manual'")
    if stored.get("status") != "PendingCommit":
        problems.append(f"status is {stored.get('status')!r}, not 'PendingCommit'")
    expected = prepare_submission(baseline, release)
    return problems + differences(_comparable(stored), _comparable(expected))


def build_upload(release, target):
    """ZIP with the package and screenshots under the fileName paths used in the submission.

    The archive is read back: it must contain exactly the reviewed files.
    """
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.write(release.package, release.package.name)
        for files in release.screenshots.values():
            for path in files:
                archive.write(path, release.relative(path), compress_type=zipfile.ZIP_STORED)
    expected = release.upload_hashes()
    with zipfile.ZipFile(target) as archive:
        found = {}
        for name in archive.namelist():
            digest = hashlib.sha256()
            with archive.open(name) as stream:
                for chunk in iter(lambda: stream.read(1 << 20), b""):
                    digest.update(chunk)
            found[name] = digest.hexdigest()
    if found != expected:
        changed = sorted(name for name in set(found) | set(expected) if found.get(name) != expected.get(name))
        raise ValueError(f"The upload archive does not match the reviewed files ({', '.join(changed)}). A file "
                         "changed while packing; nothing was sent.")


def redact(text):
    """Remove URLs: upload URLs carry a secret signature."""
    return re.sub(r"https?://[^\s\"'<>]+", "<url>", text)


class StoreError(RuntimeError):
    def __init__(self, message, status=None):
        super().__init__(message)
        self.status = status  # HTTP status, or None if no answer arrived.


def _conflict_hint(status):
    if status != 409:
        return ""
    return (" Microsoft rejects this in the current state (HTTP 409). Possible causes named by Microsoft: the "
            "state of the submission or app, changes made in Partner Center, or a Partner Center feature the "
            "submission API does not support.")


class StoreClient:
    """One request per call; writing requests are never repeated automatically."""

    def __init__(self, token, send=None):
        self.token = token
        self.send = send or _send

    def call(self, method, path, body=None):
        headers = {"Authorization": f"Bearer {self.token}", "Content-Type": "application/json"}
        data = None if body is None else json.dumps(body).encode("utf-8")
        if method == "POST":
            headers["Content-Length"] = str(len(data or b""))
        try:
            status, payload = self.send(method, f"{API}{path}", headers, data)
        except (OSError, http.client.HTTPException) as error:
            raise StoreError(f"{method} {path} got no answer ({type(error).__name__}).") from None
        if status >= 300:
            detail = redact(payload[:2000].decode("utf-8", "replace"))
            raise StoreError(f"{method} {path} failed with HTTP {status}: {detail}.{_conflict_hint(status)}", status)
        try:
            return json.loads(payload) if payload.strip() else None
        except ValueError:
            raise StoreError(f"{method} {path} returned an unreadable answer.") from None


def _send(method, url, headers, data):
    request = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=600) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def load_credentials(environ=os.environ, read_secrets=bitwarden_secrets.read_secrets):
    """Return (tenant, client, secret) from the environment or else from Bitwarden."""
    values = {name: environ.get(name) for name in CREDENTIAL_NAMES}
    if not all(values.values()):
        values = read_secrets(BITWARDEN_PROJECT, CREDENTIAL_NAMES)
    tenant, client, secret = (values[name] for name in CREDENTIAL_NAMES)
    # Both end up in the sign-in request; reject mix-ups before they can appear in an error message.
    for name, value in zip(CREDENTIAL_NAMES, (tenant, client)):
        if not re.fullmatch(r"[A-Za-z0-9.\-]+", value):
            raise ValueError(f"{name} is not a valid ID.")
    return tenant, client, secret


def _token(send, credentials):
    tenant, client, secret = credentials
    form = urllib.parse.urlencode({
        "grant_type": "client_credentials", "client_id": client, "client_secret": secret,
        "resource": "https://manage.devcenter.microsoft.com",
    }).encode("ascii")
    status, payload = send("POST", f"https://login.microsoftonline.com/{tenant}/oauth2/token",
                           {"Content-Type": "application/x-www-form-urlencoded"}, form)
    if status >= 300:
        raise RuntimeError(f"Sign-in failed with HTTP {status}.")
    return json.loads(payload)["access_token"]


def _sign_in(send, steps):
    with steps.step("Reading credentials"):
        credentials = load_credentials()
    with steps.step("Signing in to Microsoft"):
        return _token(send, credentials)


def _link(submission_id):
    return f"https://partner.microsoft.com/dashboard/products/{APPLICATION_ID}/submissions/{submission_id}"


def journal_directory(environ=os.environ):
    if os.name == "nt":
        if not environ.get("LOCALAPPDATA"):
            raise RuntimeError("LOCALAPPDATA is not set; cannot find the submission journal.")
        return Path(environ["LOCALAPPDATA"]) / "SpanDraft"
    return Path(environ.get("XDG_STATE_HOME") or Path.home() / ".local" / "state") / "SpanDraft"


class Journal:
    """The submission journal of the app on this computer: one open record at most."""

    SCHEMA = 1

    def __init__(self, directory):
        self.directory = Path(directory)
        self.path = self.directory / f"store-{APPLICATION_ID}.json"
        self.history = self.directory / "history"

    @contextlib.contextmanager
    def lock(self):
        """Exclusive per app; the operating system releases it when the process ends."""
        self.directory.mkdir(parents=True, exist_ok=True)
        descriptor = os.open(self.directory / f"store-{APPLICATION_ID}.lock", os.O_RDWR | os.O_CREAT, 0o600)
        try:
            try:
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(descriptor, msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except OSError:
                raise RuntimeError("Another Store submission run is active on this computer. Nothing was "
                                   "changed.") from None
            try:
                yield
            finally:
                if os.name == "nt":
                    import msvcrt
                    os.lseek(descriptor, 0, os.SEEK_SET)
                    msvcrt.locking(descriptor, msvcrt.LK_UNLCK, 1)
        finally:
            os.close(descriptor)

    def load(self):
        if not self.path.is_file():
            return None
        try:
            record = json.loads(self.path.read_text(encoding="utf-8"))
        except ValueError:
            record = None
        if (not isinstance(record, dict) or record.get("schema") != self.SCHEMA
                or record.get("applicationId") != APPLICATION_ID or record.get("stage") not in STAGES):
            raise RuntimeError(f"The submission journal {self.path} is unreadable or of another format. Nothing "
                               "was changed. Check Partner Center, then move the file away.")
        return record

    def save(self, record):
        text = json.dumps(dict(record, schema=self.SCHEMA, applicationId=APPLICATION_ID), indent=2)
        if "fileUploadUrl" in text or re.search(r"[?&]sig=", text):
            raise RuntimeError("Refusing to store an upload URL in the journal.")
        self.directory.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_suffix(".tmp")
        with open(temporary, "w", encoding="utf-8") as stream:
            stream.write(text)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, self.path)

    def close(self, reason):
        self.history.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H-%M-%S.%fZ")
        target = self.history / f"store-{APPLICATION_ID}-{stamp}-{reason}.json"
        os.replace(self.path, target)
        return target


def token_of(record):
    """What --clear-state expects for this record."""
    return record.get("submissionId") or record.get("deleteRequested") or "create"


def _sanitized(submission):
    """The draft without the upload URL (a secret) and status details (may carry report URLs)."""
    return {key: value for key, value in submission.items() if key not in ("fileUploadUrl", "statusDetails")}


class Stop(RuntimeError):
    """Stopped; the message says what was changed, if anything."""


class Submission:
    """The upload and certification submission of one release folder."""

    def __init__(self, release, send=_send, steps=None, clock=time.monotonic, sleep=time.sleep, journal=None):
        self.release, self.send, self.clock, self.sleep = release, send, clock, sleep
        self.steps = steps or Steps(10)
        self.journal = journal or Journal(journal_directory())
        self.client = None
        self.app = f"/applications/{APPLICATION_ID}"
        self.record = None

    def path(self, submission_id, suffix=""):
        return f"{self.app}/submissions/{submission_id}{suffix}"

    def say(self, text, tone=None):
        self.steps.say(text, tone)  # Output errors never reach the workflow.

    def save(self, **changes):
        self.record = dict(self.record or {}, **changes)
        self.journal.save(self.record)

    def new_record(self, stage, **fields):
        self.record = {"version": self.release.version, "folder": str(self.release.root),
                       "fingerprint": self.release.fingerprint, "content": self.release.content,
                       "stage": stage, "submissionId": None, "baseline": None, "deleteRequested": None}
        self.save(**fields)

    # Reading -----------------------------------------------------------------------------------
    def get_submission(self, submission_id):
        submission = self.client.call("GET", self.path(submission_id))
        if not isinstance(submission, dict) or submission.get("id") != submission_id:
            raise Stop(f"The Store returned another submission than {submission_id}. Stopped.")
        return submission

    def status_of(self, submission_id):
        """The status, MISSING for HTTP 404; unknown values are returned as they are."""
        try:
            answer = self.client.call("GET", self.path(submission_id, "/status"))
        except StoreError as error:
            if error.status == 404:
                return MISSING
            raise
        return answer.get("status") if isinstance(answer, dict) else None

    def app_state(self):
        application = self.client.call("GET", self.app)
        pending = (application.get("pendingApplicationSubmission") or {}).get("id")
        published = (application.get("lastPublishedApplicationSubmission") or {}).get("id")
        if not published:
            raise Stop("The app has no published submission to start from. Nothing was changed.")
        if pending == published:
            raise Stop("The pending submission is the published one; refusing to touch it. Nothing was changed.")
        return pending, published

    def report(self, submission):
        submission_id = submission["id"]
        for label, value in (("Version", self.release.version), ("Submission", submission_id),
                             ("Status", submission.get("status")),
                             ("Publish mode", submission.get("targetPublishMode")),
                             ("Partner Center", _link(submission_id))):
            self.say(f"{label + ':':<16}{value}")
        details = submission.get("statusDetails") or {}
        for kind, tone in (("errors", "error"), ("warnings", "warning")):
            for item in details.get(kind) or []:
                self.say(f"  {kind[:-1]}: {item.get('code')} {redact(str(item.get('details')))}", tone)

    # Deciding ----------------------------------------------------------------------------------
    def decide(self, replace_draft, pending, published):
        """Return ('report', id), ('wait', id), ('continue', id), ('create', None) or ('replace', id).

        Only reads; every refusal raises Stop before anything is written.
        """
        record, release = self.record, self.release
        unchanged = "Nothing was changed."
        check = "Check the actual state in Partner Center first"
        if record is None:
            if replace_draft:
                return "replace", self.replaceable(replace_draft, pending, published)
            if pending:
                status = self.status_of(pending)
                if status in DELETABLE:
                    raise Stop(f"There is an unpublished draft {pending} (status {status}). {unchanged} To delete "
                               f"and replace it, add: --replace-draft {pending}")
                raise Stop(f"Submission {pending} has status {status}. It is not touched. {unchanged} Handle it in "
                           f"Partner Center: {_link(pending)}")
            return "create", None

        stage, own = record["stage"], record.get("submissionId")
        same = record.get("fingerprint") == release.fingerprint
        where = f"release {record.get('version')} from {record.get('folder')}"
        clear = f"{check}, then archive the local journal with: --clear-state {token_of(record)}"
        if stage == "create-requested":
            seen = f"Pending submission now: {pending}." if pending else "No pending submission is visible yet."
            raise Stop(f"Creating a submission for {where} was requested, but the answer never arrived. {seen} "
                       f"Only reading until this is resolved. {unchanged} {clear}")
        if stage == "delete-requested":
            target = record["deleteRequested"]
            if not same:
                raise Stop(f"Replacing draft {target} for {where} is unfinished. {unchanged} Run that folder "
                           f"again, or {clear[0].lower()}{clear[1:]}")
            if replace_draft != target:
                raise Stop(f"Replacing draft {target} is unfinished. {unchanged} Run again with "
                           f"--replace-draft {target}, or {clear[0].lower()}{clear[1:]}")
            if pending == target:
                if self.status_of(target) == MISSING:
                    raise Stop(f"Draft {target} is deleted, but Microsoft still lists it as pending. {unchanged} Run "
                               "the same command again later.")
                return "replace", self.replaceable(target, pending, published)
            if pending:
                raise Stop(f"Draft {target} is gone, but submission {pending} is pending now. {unchanged} {clear}")
            return "create", None

        status = self.status_of(own)
        if status == MISSING or (status in KNOWN and own != pending and status != "Published"):
            raise Stop(f"Submission {own} of {where} ({stage}) is no longer pending in the Store (status {status}). "
                       f"Only reading. {unchanged} {clear}")
        if status not in KNOWN:
            raise Stop(f"Submission {own} of {where} has the unknown status {status!r}. Only reading. {unchanged} "
                       f"{check}: {_link(own)}")
        if stage in ("created", "uploaded"):
            if replace_draft == own:
                return "replace", self.replaceable(own, pending, published)
            if replace_draft:
                raise Stop(f"--replace-draft {replace_draft} does not name the pending draft {own}. {unchanged}")
            if status != "PendingCommit":
                raise Stop(f"Draft {own} was not committed by this tool but has status {status}. {unchanged} "
                           f"{check}: {_link(own)}")
            if not same:
                changed = ", ".join(content_changes(record.get("content") or {}, release.content))
                raise Stop(f"Draft {own} belongs to {where} with other content (changed: {changed}). {unchanged} "
                           f"To replace it with this folder, add: --replace-draft {own}")
            return "continue", own
        # commit-requested or committed: the commit may have been sent; only reading from here on.
        if replace_draft:
            if stage == "committed" and status == "CommitFailed" and replace_draft == own:
                return "replace", self.replaceable(own, pending, published)
            raise Stop(f"Submission {own} of {where} was committed or its commit is unconfirmed (status {status}); "
                       f"--replace-draft is refused. {unchanged}")
        if not same:
            raise Stop(f"Submission {own} of {where} is still open (status {status}). {unchanged} Finish it first: "
                       f"{_link(own)}")
        return ("wait" if status in ("PendingCommit", "CommitStarted") else "report"), own

    def replaceable(self, draft, pending, published):
        """Refuse --replace-draft unless it names the pending, deletable draft."""
        if draft == published:
            raise Stop(f"{draft} is the published submission; it is never deleted. Nothing was changed.")
        if draft != pending:
            raise Stop(f"There is no pending draft {draft} to replace"
                       f"{f' (pending: {pending})' if pending else ''}. Nothing was changed.")
        status = self.status_of(draft)
        if status not in DELETABLE:
            raise Stop(f"Submission {draft} has status {status}; only drafts in {sorted(DELETABLE)} are replaced. "
                       f"It is not touched. Nothing was changed. {_link(draft)}")
        return draft

    # Running -----------------------------------------------------------------------------------
    def run(self, replace_draft=None):
        """Returns the submission as last read."""
        with self.journal.lock():
            self.record = self.journal.load()
            if replace_draft and self.record and self.record["stage"] in UNCLEAR:
                raise Stop(f"The journal has an unclear request for release {self.record.get('version')} "
                           f"({self.record['stage']}); --replace-draft is refused. Nothing was changed. Check "
                           f"Partner Center, then: --clear-state {token_of(self.record)}")
            self.client = StoreClient(_sign_in(self.send, self.steps), self.send)
            with self.steps.step("Checking the Store (read only)"):
                pending, published = self.app_state()
                own = (self.record or {}).get("submissionId")
                if own and own == published:
                    # Proven published: the journal is finished.
                    mine = self.record.get("fingerprint") == self.release.fingerprint
                    archived = self.journal.close("published")
                    self.record = None
                    if mine:
                        final = self.get_submission(own)
                        self.steps.detail = "this folder is already published"
                        action = "published"
                if not (own and own == published and mine):
                    action, target = self.decide(replace_draft, pending, published)
                    if action in ("create", "replace", "continue"):
                        # Everything that can be rejected is rejected before the first write, including a
                        # version that is not newer than the published one.
                        self.published = self.get_submission(published)
                        prepare_submission(self.published, self.release)
                        if action == "continue":
                            prepare_submission(self.record["baseline"], self.release)
                    self.steps.detail = {"create": "will create a submission", "replace": f"will replace draft {target}",
                                         "continue": f"continuing {target}"}.get(action, f"{target}: read only")
            if action == "published":
                self.say(f"Nothing to do: this folder is published. Nothing was changed. Journal archived: {archived}")
                self.report(final)
                return final
            if action == "report":
                self.say("Nothing to do: this folder was already committed. Nothing was changed.")
            if action in ("report", "wait"):
                return self.wait(target)
            return self.write(action, target)

    def write(self, action, target):
        with tempfile.TemporaryDirectory() as temp:
            upload = Path(temp) / "upload.zip"
            with self.steps.step("Packing and checking MSIX and screenshots"):
                build_upload(self.release, upload)
                self.steps.detail = f"{upload.stat().st_size / 1e6:.0f} MB"
            with self.steps.step("Preparing the submission"):
                if action == "replace":
                    self.delete(target)
                    action = "create"
                submission_id = self.create() if action == "create" else target
                draft = self.get_submission(submission_id)
                if draft.get("status") != "PendingCommit":
                    raise Stop(f"Draft {submission_id} has status {draft.get('status')}. Not committed.")
                baseline = self.record["baseline"]
                # Checked on every run, also when continuing: only the intended change is ever written.
                problems = verify_clone(baseline, self.published)
                if problems:
                    raise Stop(f"Draft {submission_id} differs from the published submission: {'; '.join(problems)}. "
                               f"It was not changed. Check {_link(submission_id)}; to start over, add: "
                               f"--replace-draft {submission_id}")
                answer = self.client.call("PUT", self.path(submission_id), prepare_submission(baseline, self.release))
                if not isinstance(answer, dict) or answer.get("id") != submission_id:
                    raise Stop(f"The update answer names another submission than {submission_id}. Not committed.")
                self.steps.detail = submission_id
            with self.steps.step("Uploading files"):
                self.upload(draft.get("fileUploadUrl"), upload)
                self.save(stage="uploaded")

        with self.steps.step("Verifying the saved submission"):
            stored = self.client.call("GET", self.path(submission_id))
            problems = verify_submission(stored or {}, baseline, self.release, submission_id)
            if problems:
                raise Stop("Not committed. The Store's saved submission differs: " + "; ".join(problems)
                           + f". Check {_link(submission_id)}")
        self.commit(submission_id)
        return self.wait(submission_id)

    def delete(self, draft):
        # Read again right before deleting: ID, status and the user's --replace-draft must agree.
        pending, published = self.app_state()
        self.replaceable(draft, pending, published)
        if self.record and self.record.get("stage") == "delete-requested":
            self.save(stage="delete-requested", deleteRequested=draft)
        else:
            self.new_record("delete-requested", deleteRequested=draft)
        try:
            self.client.call("DELETE", self.path(draft))
        except StoreError as error:
            raise Stop(f"Deleting draft {draft} is unconfirmed: {error} Run the same command again; it checks "
                       "the Store first.") from None

    def create(self):
        pending, _ = self.app_state()
        if pending:
            raise Stop(f"Submission {pending} is pending; nothing is created. If a draft was just deleted, Microsoft "
                       "may not show it yet: run the same command again later.")
        self.new_record("create-requested")
        try:
            created = self.client.call("POST", f"{self.app}/submissions")
        except (StoreError, KeyboardInterrupt) as error:
            raise Stop(f"Creating the submission is unconfirmed ({type(error).__name__}). Only reading from now "
                       f"on. Check Partner Center, then: --clear-state create") from None
        created_id = created.get("id") if isinstance(created, dict) else None
        pending, _ = self.app_state()
        if not created_id or pending != created_id:
            raise Stop(f"The Store answered with submission {created_id}, but {pending} is pending. Only reading "
                       "from now on. Check Partner Center, then: --clear-state create")
        draft = self.get_submission(created_id)
        self.save(stage="created", submissionId=created_id, baseline=_sanitized(draft))
        return created_id

    def upload(self, url, upload):
        if not url:
            raise Stop("The draft has no upload URL. Not committed.")
        size = upload.stat().st_size
        with upload.open("rb") as stream:
            try:
                status, _ = self.send("PUT", url, {
                    "x-ms-blob-type": "BlockBlob", "x-ms-version": "2020-04-08", "Content-Length": str(size),
                }, self.steps.reader(stream, size))
            except Exception as error:  # Messages could contain the signed URL.
                raise Stop(f"File upload failed ({type(error).__name__}). Not committed; run the same command "
                           "again to continue.") from None
        if status >= 300:
            raise Stop(f"File upload failed with HTTP {status}. Not committed; run the same command again to "
                       "continue.")
        self.steps.detail = f"{size / 1e6:.0f} MB received by Microsoft"  # Only after HTTP < 300.

    def commit(self, submission_id):
        with self.steps.step("Committing: submit for certification"):
            self.save(stage="commit-requested")
            try:
                answer = self.client.call("POST", self.path(submission_id, "/commit"))
            except (StoreError, KeyboardInterrupt) as error:
                # It may have arrived although the answer did not: from now on only reading.
                self.steps.detail = f"answer missing ({type(error).__name__}), reading the status"
                if isinstance(error, KeyboardInterrupt):
                    raise Stop(f"Interrupted while committing {submission_id}. Run the same command again; it "
                               "only reads the status.") from None
                return
            if isinstance(answer, dict) and answer.get("status") in ("CommitStarted", *SUBMITTED):
                self.save(stage="committed")
            self.steps.detail = str((answer or {}).get("status") if isinstance(answer, dict) else answer)

    def wait(self, submission_id):
        """Only reads. Marks the journal committed once the Store shows the commit."""
        with self.steps.step("Waiting for Microsoft to process"):
            deadline = self.clock() + POLL_TIMEOUT
            status = None
            while True:
                self.steps.checking()
                try:
                    status = self.status_of(submission_id)
                except StoreError:
                    status = None  # Reading may be repeated.
                if status is not None and status != "PendingCommit" and self.record["stage"] == "commit-requested":
                    if status in KNOWN:
                        self.save(stage="committed")
                if status is not None and status not in ("PendingCommit", "CommitStarted"):
                    break
                if self.clock() >= deadline:
                    break
                self.steps.waiting(status or "no answer yet")
                self.steps.countdown(POLL_SECONDS, self.sleep)  # Display only: still POLL_SECONDS in total.
            if status == MISSING or (status is not None and status not in KNOWN):
                raise Stop(f"Submission {submission_id} has the status {status!r}. Only reading. Check Partner "
                           f"Center, then: --clear-state {submission_id}")
            final = self.get_submission(submission_id)
            status = final.get("status")
            self.steps.detail = str(status)
        problem = None
        if self.record["stage"] != "committed" or status == "PendingCommit":
            problem = (f"The commit of {submission_id} is not confirmed (status {status}). Nothing more is sent. "
                       "Run the same command again later; it only reads. If it stays a draft, check Partner Center, "
                       f"then: --clear-state {submission_id} and, to start over, --replace-draft {submission_id}")
        elif final.get("targetPublishMode") != "Manual":
            problem = "WARNING: the publish mode is no longer 'Manual'. Check Partner Center now."
        elif status == "CommitStarted":
            problem = ("Committed, but Microsoft has not finished processing the commit. Run the same command again "
                       "later to see the status.")
        elif status not in SUBMITTED:
            again = f" To start over with a new submission, add: --replace-draft {submission_id}" \
                if status == "CommitFailed" else ""
            problem = f"The submission ended with status {status}. See the details above and Partner Center.{again}"
        if problem:
            self.report(final)
            raise Stop(problem)
        # Reached only when committed, in a SUBMITTED status and with publish mode Manual confirmed.
        headline, note = OUTCOMES[status]
        self.steps.say()
        self.steps.success(headline)
        self.steps.say()
        self.report(final)
        if note:
            self.steps.say()
            self.steps.say(note, "warning")
        return final


def clear_state(token, journal=None, stream=sys.stdout):
    """Archive the open journal record. Local only; it confirms nothing in the Store."""
    journal = journal or Journal(journal_directory())
    with journal.lock():
        record = journal.load()
        if record is None:
            raise Stop(f"There is no open submission journal in {journal.directory}. Nothing was changed.")
        if token != token_of(record):
            raise Stop(f"The open journal ({record['stage']}, release {record.get('version')}) expects "
                       f"--clear-state {token_of(record)}. Nothing was changed.")
        target = journal.close("cleared")
    print(f"Archived the local journal to {target}. This changes nothing in the Store and confirms nothing: make "
          "sure Partner Center shows what you expect before the next run.", file=stream)


def check_credentials(send=_send, steps=None):
    steps = steps or Steps(3)
    client = StoreClient(_sign_in(send, steps), send)
    with steps.step("Reading the app from the Store"):
        client.call("GET", f"/applications/{APPLICATION_ID}")
    steps.success("Store credentials work.")


def parse_arguments(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("release", type=Path, nargs="?", help="artifacts/store-release/<version>_<timestamp>")
    parser.add_argument("--replace-draft", metavar="ID", help="delete this unpublished draft and replace it")
    parser.add_argument("--check", action="store_true", help="validate the folder only")
    parser.add_argument("--check-credentials", action="store_true", help="sign in to the Store, change nothing")
    parser.add_argument("--clear-state", metavar="TOKEN",
                        help="archive the local journal only; check Partner Center first, it confirms nothing")
    args = parser.parse_args(argv)
    if args.check_credentials and (args.release or args.check or args.replace_draft or args.clear_state):
        parser.error("--check-credentials takes no other arguments")
    if args.clear_state and (args.release or args.check or args.replace_draft):
        parser.error("--clear-state takes no other arguments")
    if args.check and args.replace_draft:
        parser.error("--check cannot be combined with --replace-draft")
    if args.release is None and not (args.check_credentials or args.clear_state):
        parser.error("the release folder is required")
    return args


def main(argv=None):
    args = parse_arguments(argv)
    steps = Steps(3 if args.check_credentials else 10)
    try:
        if args.check_credentials:
            check_credentials(steps=steps)
            return
        if args.clear_state:
            clear_state(args.clear_state)
            return
        if args.check:
            release = Release(args.release)
            print(f"Release folder for {release.version} is valid.")
            return
        with steps.step("Checking the release folder"):
            release = Release(args.release)
        Submission(release, steps=steps).run(args.replace_draft)
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        where = f" in step {steps.failed}" if steps.failed else ""
        raise SystemExit(f"error{where}: {error}") from None
    except KeyboardInterrupt:
        raise SystemExit(f"Interrupted in step {steps.number}. Run the same command again; it reads the Store "
                         "and the journal first.") from None


if __name__ == "__main__":
    sys.exit(main())
