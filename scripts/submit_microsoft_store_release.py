"""Upload a reviewed release folder to the Microsoft Store and submit it for certification.

Usage (from main, any OS):
    python scripts/submit_microsoft_store_release.py artifacts/store-release/<version>_<timestamp>
        [--replace-draft <submission ID>]
    python scripts/submit_microsoft_store_release.py <folder> --check
    python scripts/submit_microsoft_store_release.py --check-credentials

Run it only after reviewing the folder from scripts/prepare-microsoft-store-release.ps1.
The script uses the Microsoft Store submission API (https://manage.devcenter.microsoft.com):

  1. validates the folder and the Store state before writing anything,
  2. creates a draft from the last published submission, replaces the SpanDraft x64 package,
     "What's new" and the screenshots, forces publish mode Manual and uploads the files,
  3. reads the submission back and, only if everything matches, COMMITS it: it goes to certification,
  4. waits until Microsoft has processed the commit.

It never publishes. After certification, review in Partner Center and choose "Publish now".
Until the commit, Partner Center does not show uploaded files.

An existing unpublished draft is only deleted with --replace-draft and its ID. Running the command
again continues an interrupted upload of the same folder (state in <folder>/submission.json) or just
reports the status of what was already committed.

Credentials (Partner Center > Account settings > User management > Microsoft Entra applications,
role Manager) are MS_STORE_TENANT_ID, MS_STORE_CLIENT_ID and MS_STORE_CLIENT_SECRET. On Windows they
are read from Bitwarden Secrets Manager (see bitwarden_secrets.py); if all three are set as
environment variables, those are used instead.
--check only validates the folder and contacts nothing.
--check-credentials signs in and reads the app from the Store; it changes nothing.
"""

import argparse
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
STATE_FILE = "submission.json"
# Never submitted successfully, so --replace-draft may delete them.
DRAFT_STATUSES = {"PendingCommit", "CommitFailed"}
PROCESSING = {"PendingCommit", "CommitStarted"}
SUBMITTED = {"PreProcessing", "Certification", "Release", "PendingPublication"}
POLL_SECONDS, POLL_TIMEOUT = 20, 20 * 60


class Release:
    """A validated release folder."""

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
        if _sha256(self.package) != self.manifest["package"]["sha256"]:
            raise ValueError(f"{self.package.name} does not match its recorded SHA-256.")
        self.whats_new, self.screenshots = {}, {}
        for culture in CULTURES:
            text = self._file(self.manifest["whatsNew"][culture]).read_text(encoding="utf-8").strip()
            if not text or len(text) > WHATS_NEW_LIMIT:
                raise ValueError(f"What's new for {culture} must have 1 to {WHATS_NEW_LIMIT} characters, has {len(text)}.")
            self.whats_new[culture] = text
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

    def _file(self, relative):
        path = (self.root / relative).resolve()
        if self.root not in path.parents or not path.is_file():
            raise ValueError(f"Missing or invalid release file: {relative}")
        return path

    def relative(self, path):
        return path.relative_to(self.root).as_posix()


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

    Applying it to its own result changes nothing further, so an interrupted run can repeat it.
    """
    body = json.loads(json.dumps(submission))
    body["targetPublishMode"] = "Manual"
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
            continue  # Left by an interrupted run; added again below.
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


def _text(value):
    return (value or "").replace("\r\n", "\n").strip()


def verify_submission(stored, original, release):
    """Compare what the Store has saved with the release folder; return the differences.

    This checks metadata and file references. The Store reads the uploaded files only after the commit.
    """
    problems = []
    if stored.get("targetPublishMode") != "Manual":
        problems.append(f"publish mode is {stored.get('targetPublishMode')!r}, not 'Manual'")
    if stored.get("status") != "PendingCommit":
        problems.append(f"status is {stored.get('status')!r}, not 'PendingCommit'")

    def split(submission):
        ours, others = {}, []
        for package in submission.get("applicationPackages") or []:
            if _package_version(package.get("fileName")):
                ours[package["fileName"]] = package.get("fileStatus")
            else:
                others.append((package.get("fileName"), package.get("fileStatus")))
        return ours, sorted(others, key=str)

    ours, others = split(stored)
    new = release.package.name
    if ours.get(new) != "PendingUpload":
        problems.append(f"package {new} is {ours.get(new)!r}, not 'PendingUpload'")
    problems += [f"package {name} is {status!r}, not 'PendingDelete'"
                 for name, status in ours.items() if name != new and status != "PendingDelete"]
    if others != split(original)[1]:
        problems.append("other packages changed")
    for key in ("applicationCategory", "pricing", "visibility"):
        if stored.get(key) != original.get(key):
            problems.append(f"{key} changed")
    listings, before = stored.get("listings") or {}, original.get("listings") or {}
    if set(listings) != set(before):
        return problems + ["the listing languages changed"]
    for language, listing in listings.items():
        culture = culture_for(language)
        base, old = listing.get("baseListing") or {}, before[language].get("baseListing") or {}
        if _text(base.get("releaseNotes")) != _text(release.whats_new[culture]):
            problems.append(f"{language}: What's new differs")
        problems += [f"{language}: {key} changed" for key in sorted(set(base) | set(old))
                     if key not in ("releaseNotes", "images") and base.get(key) != old.get(key)]
        images = base.get("images") or []
        screenshots = [i for i in images if i.get("imageType") == "Screenshot"]
        pending = [i.get("fileName", "").replace("\\", "/") for i in screenshots if i.get("fileStatus") == "PendingUpload"]
        if pending != [release.relative(path) for path in release.screenshots[culture]]:
            problems.append(f"{language}: new screenshots are missing or out of order")
        if any(i.get("fileStatus") not in ("PendingUpload", "PendingDelete") for i in screenshots):
            problems.append(f"{language}: old screenshots are not marked for removal")

        def other_images(values):
            return sorted((i.get("imageType"), i.get("fileName")) for i in values
                          if i.get("imageType") != "Screenshot" and i.get("fileStatus") != "PendingDelete")

        if other_images(images) != other_images(old.get("images") or []):
            problems.append(f"{language}: logos or other images changed")
    return problems


def build_upload(release, target):
    """ZIP with the package and screenshots under the fileName paths used in the submission."""
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.write(release.package, release.package.name)
        for files in release.screenshots.values():
            for path in files:
                archive.write(path, release.relative(path), compress_type=zipfile.ZIP_STORED)


def redact(text):
    """Remove URLs: upload URLs carry a secret signature."""
    return re.sub(r"https?://[^\s\"'<>]+", "<url>", text)


class StoreError(RuntimeError):
    def __init__(self, message, status=None):
        super().__init__(message)
        self.status = status  # HTTP status, or None if no answer arrived.


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
            raise StoreError(f"{method} {path} failed with HTTP {status}: {detail}", status)
        return json.loads(payload) if payload.strip() else None


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


class Submission:
    """The upload and certification submission of one release folder."""

    def __init__(self, release, send=_send, steps=None, clock=time.monotonic, sleep=time.sleep):
        self.release, self.send, self.clock, self.sleep = release, send, clock, sleep
        self.steps = steps or Steps(10)
        self.client = None
        self.app = f"/applications/{APPLICATION_ID}"
        self.state_path = release.root / STATE_FILE

    def path(self, submission_id, suffix=""):
        return f"{self.app}/submissions/{submission_id}{suffix}"

    def say(self, text):
        print(text, file=self.steps.stream, flush=True)

    # Local state: progress, IDs and the untouched draft content; never upload URLs or credentials.
    def load_state(self):
        if not self.state_path.is_file():
            return None
        state = json.loads(self.state_path.read_text(encoding="utf-8"))
        expected = (APPLICATION_ID, self.release.version, self.release.manifest["package"]["sha256"])
        if (state.get("applicationId"), state.get("version"), state.get("packageSha256")) != expected:
            raise ValueError(f"{self.state_path} belongs to different release content. Check Partner Center, "
                             "then remove the file.")
        return state

    def save_state(self, submission_id, stage, baseline=None):
        if baseline is None and self.state_path.is_file():
            previous = json.loads(self.state_path.read_text(encoding="utf-8"))
            if previous.get("submissionId") == submission_id:
                baseline = previous.get("baseline")
        temporary = self.state_path.with_suffix(".tmp")
        temporary.write_text(json.dumps({
            "applicationId": APPLICATION_ID, "version": self.release.version,
            "packageSha256": self.release.manifest["package"]["sha256"],
            "submissionId": submission_id, "stage": stage, "baseline": baseline,
        }, indent=2), encoding="utf-8")
        temporary.replace(self.state_path)

    def report(self, submission):
        submission_id = submission["id"]
        self.say(f"Version:      {self.release.version}")
        self.say(f"Submission:   {submission_id}")
        self.say(f"Status:       {submission.get('status')}")
        self.say(f"Publish mode: {submission.get('targetPublishMode')}")
        self.say(f"Partner Center: {_link(submission_id)}")
        details = submission.get("statusDetails") or {}
        for kind in ("errors", "warnings"):
            for item in details.get(kind) or []:
                self.say(f"  {kind[:-1]}: {item.get('code')} {redact(str(item.get('details')))}")

    def run(self, replace_draft=None):
        """Returns the submission as last read."""
        self.client = StoreClient(_sign_in(self.send, self.steps), self.send)
        state = self.load_state()
        with self.steps.step("Checking the Store (read only)"):
            application = self.client.call("GET", self.app)
            pending = (application.get("pendingApplicationSubmission") or {}).get("id")
            published = (application.get("lastPublishedApplicationSubmission") or {}).get("id")
            if not published:
                raise RuntimeError("The app has no published submission to start from.")
            if pending == published:
                raise RuntimeError("The pending submission is the published one; refusing to touch it.")
            mine = state["submissionId"] if state else None
            existing, delete = None, None
            if mine and mine == published:
                existing = self.client.call("GET", self.path(mine))
                self.steps.detail = "this folder is already published"
            elif mine and mine == pending:
                existing = self.client.call("GET", self.path(mine))
                self.steps.detail = f"found this folder's submission, {existing.get('status')}"
            else:
                if pending:
                    status = self.client.call("GET", self.path(pending, "/status")).get("status")
                    if status not in DRAFT_STATUSES:
                        raise RuntimeError(f"Submission {pending} is already in progress (status {status}). It is "
                                           f"not touched; handle it in Partner Center: {_link(pending)}")
                    if replace_draft != pending:
                        raise RuntimeError(f"There is an unpublished draft {pending} (status {status}). Nothing was "
                                           f"changed. To delete and replace it, add: --replace-draft {pending}")
                    delete = pending
                elif replace_draft:
                    raise RuntimeError(f"There is no draft {replace_draft} to replace. Nothing was changed.")
                self.steps.detail = f"will replace draft {delete}" if delete else "no draft open"
            if existing and existing["id"] == pending == replace_draft and existing.get("status") == "CommitFailed":
                delete, existing = pending, None
                self.steps.detail = f"will replace draft {delete}"
            if existing is None:
                # Everything that can be rejected is rejected before the first write.
                prepare_submission(self.client.call("GET", self.path(published)), self.release)

        if existing and (existing["id"] == published or existing.get("status") != "PendingCommit"):
            self.say("Nothing to do: this folder was already committed. Nothing was changed.")
            self.report(existing)
            return existing

        with tempfile.TemporaryDirectory() as temp:
            upload = Path(temp) / "upload.zip"
            with self.steps.step("Packing MSIX and screenshots"):
                build_upload(self.release, upload)
                self.steps.detail = f"{upload.stat().st_size / 1e6:.0f} MB"
            with self.steps.step("Preparing the draft"):
                if delete:
                    self.client.call("DELETE", self.path(delete))
                if existing:
                    draft = existing
                    baseline = state.get("baseline")
                    self.steps.detail = f"continuing {draft['id']}"
                else:
                    created = self.client.call("POST", f"{self.app}/submissions")["id"]
                    self.save_state(created, "created")
                    draft = self.client.call("GET", self.path(created))
                    baseline = None
                    self.steps.detail = f"created {created}"
                submission_id = draft["id"]
                if baseline is None:
                    baseline = {key: draft.get(key) for key in
                                ("applicationCategory", "pricing", "visibility", "applicationPackages", "listings")}
                    self.save_state(submission_id, "created", baseline)
                self.client.call("PUT", self.path(submission_id), prepare_submission(draft, self.release))
            with self.steps.step("Uploading files"):
                self.upload(draft["fileUploadUrl"], upload)
                self.save_state(submission_id, "uploaded")

        with self.steps.step("Verifying the saved submission"):
            stored = self.client.call("GET", self.path(submission_id))
            problems = verify_submission(stored, baseline, self.release)
            if problems:
                raise RuntimeError("Not committed. The Store's saved submission differs: " + "; ".join(problems)
                                   + f". Check {_link(submission_id)}")
        self.commit(submission_id)
        return self.wait(submission_id)

    def upload(self, url, upload):
        size = upload.stat().st_size
        with upload.open("rb") as stream:
            try:
                status, _ = self.send("PUT", url, {
                    "x-ms-blob-type": "BlockBlob", "x-ms-version": "2020-04-08", "Content-Length": str(size),
                }, self.steps.reader(stream, size))
            except Exception as error:  # Messages could contain the signed URL.
                raise RuntimeError(f"File upload failed ({type(error).__name__}). Not committed; run the same "
                                   "command again to continue.") from None
        if status >= 300:
            raise RuntimeError(f"File upload failed with HTTP {status}. Not committed; run the same command again "
                               "to continue.")
        self.steps.detail = f"{size / 1e6:.0f} MB received"

    def commit(self, submission_id):
        with self.steps.step("Committing: submit for certification"):
            self.save_state(submission_id, "commit-requested")
            try:
                self.client.call("POST", self.path(submission_id, "/commit"))
            except StoreError as error:
                # The commit may have arrived although the answer did not. Ask; never send it twice.
                try:
                    status = self.client.call("GET", self.path(submission_id, "/status")).get("status")
                except StoreError:
                    status = None
                if status is None or status == "PendingCommit":
                    raise RuntimeError(f"The commit is not confirmed (status {status or 'unknown'}): {error} "
                                       "Check Partner Center; running the same command again reads the status "
                                       "first.") from None
                self.steps.detail = f"answer lost, status {status}"
            self.save_state(submission_id, "committed")

    def wait(self, submission_id):
        with self.steps.step("Waiting for Microsoft to process"):
            deadline = self.clock() + POLL_TIMEOUT
            status = None
            while True:
                try:
                    status = self.client.call("GET", self.path(submission_id, "/status")).get("status")
                except StoreError:
                    pass  # Reading may be repeated.
                if status and status not in PROCESSING:
                    break
                if self.clock() >= deadline:
                    break
                self.steps.status(status or "no answer yet")
                self.sleep(POLL_SECONDS)
            final = self.client.call("GET", self.path(submission_id))
            status = final.get("status")
            self.steps.detail = str(status)
        self.report(final)
        if final.get("targetPublishMode") != "Manual":
            raise RuntimeError("WARNING: the publish mode is no longer 'Manual'. Check Partner Center now.")
        if status in PROCESSING:
            raise RuntimeError("Committed, but processing has not finished. Run the same command again later to "
                               "see the status.")
        if status not in SUBMITTED:
            raise RuntimeError(f"The submission ended with status {status}. See the details above and Partner Center.")
        self.say("Submitted for certification. NOT published: after certification, review it in Partner Center "
                 "and choose 'Publish now'.")
        return final


def check_credentials(send=_send, steps=None):
    steps = steps or Steps(3)
    client = StoreClient(_sign_in(send, steps), send)
    with steps.step("Reading the app from the Store"):
        client.call("GET", f"/applications/{APPLICATION_ID}")
    print("Store credentials work.", file=steps.stream)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("release", type=Path, nargs="?", help="artifacts/store-release/<version>_<timestamp>")
    parser.add_argument("--replace-draft", metavar="ID", help="delete this unpublished draft and replace it")
    parser.add_argument("--check", action="store_true", help="validate the folder only")
    parser.add_argument("--check-credentials", action="store_true", help="sign in to the Store, change nothing")
    args = parser.parse_args()
    if args.release is None and not args.check_credentials:
        parser.error("the release folder is required")
    if args.check_credentials and (args.release or args.check or args.replace_draft):
        parser.error("--check-credentials takes no other arguments")
    steps = Steps(3 if args.check_credentials else 10)
    try:
        if args.check_credentials:
            check_credentials(steps=steps)
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
                         "status first.") from None


if __name__ == "__main__":
    sys.exit(main())
