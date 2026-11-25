"""Create a Microsoft Store draft submission from a prepared release folder.

Usage (from main, any OS):
    python scripts/microsoft_store_draft.py artifacts/store-release/<version>_<timestamp> [--check]
    python scripts/microsoft_store_draft.py --check-credentials

The folder comes from scripts/prepare-microsoft-store-release.ps1. The script uses the official
Microsoft Store submission API (https://manage.devcenter.microsoft.com):

  1. replaces an existing unpublished draft, if any (published releases cannot be deleted by the API),
  2. creates a new draft from the last published submission (description and other fields are kept),
  3. adds the MSIX, sets "What's new" per language and replaces the screenshots in the folder's order,
  4. uploads the files.

It never commits the draft. Review and submit it in Partner Center.

Credentials (Partner Center > Account settings > User management > Microsoft Entra applications,
role Manager) are MS_STORE_TENANT_ID, MS_STORE_CLIENT_ID and MS_STORE_CLIENT_SECRET. On Windows they
are read from Bitwarden Secrets Manager (see bitwarden_secrets.py); if all three are set as
environment variables, those are used instead.
--check only validates the folder and contacts nothing.
--check-credentials signs in and reads the app from the Store; it changes nothing.
"""

import argparse
import hashlib
import json
import os
import re
import struct
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

if __package__:
    from . import bitwarden_secrets
else:
    import bitwarden_secrets


BITWARDEN_PROJECT = "SpanDraft"
CREDENTIAL_NAMES = ("MS_STORE_TENANT_ID", "MS_STORE_CLIENT_ID", "MS_STORE_CLIENT_SECRET")
APPLICATION_ID = "9NVWB1M86PDZ"
API = "https://manage.devcenter.microsoft.com/v1.0/my"
CULTURES = ("de-DE", "en-US")
WHATS_NEW_LIMIT = 1500
MAX_SCREENSHOTS = 10
MIN_SIZE, MAX_SIZE = (1366, 768), (3840, 2160)
MAX_IMAGE_BYTES = 50 * 1024 * 1024


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


def prepare_submission(submission, release):
    """Return the updated submission body: new package, What's new and screenshots per listing."""
    body = json.loads(json.dumps(submission))
    package = release.package.name
    if not any(p.get("fileName") == package for p in body.get("applicationPackages", [])):
        body.setdefault("applicationPackages", []).append({
            "fileName": package, "fileStatus": "PendingUpload",
            "minimumDirectXVersion": "None", "minimumSystemRam": "None",
        })
    listings = body.get("listings") or {}
    if not listings:
        raise ValueError("The draft has no Store listings.")
    covered = set()
    for language, listing in listings.items():
        culture = culture_for(language)
        covered.add(culture)
        base = listing.setdefault("baseListing", {})
        base["releaseNotes"] = release.whats_new[culture]
        images = [dict(image, fileStatus="PendingDelete") if image.get("imageType") == "Screenshot" else image
                  for image in base.get("images", [])]
        images += [{"fileName": release.relative(path), "fileStatus": "PendingUpload", "imageType": "Screenshot"}
                   for path in release.screenshots[culture]]
        base["images"] = images
    missing = set(CULTURES) - covered
    if missing:
        raise ValueError(f"The Store has no listing for {sorted(missing)}; add it in Partner Center first.")
    return body


def build_upload(release, target):
    """ZIP with the package and screenshots under the fileName paths used in the submission."""
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.write(release.package, release.package.name)
        for files in release.screenshots.values():
            for path in files:
                archive.write(path, release.relative(path), compress_type=zipfile.ZIP_STORED)


class StoreClient:
    def __init__(self, token, send=None):
        self.token = token
        self.send = send or _send

    def call(self, method, path, body=None):
        headers = {"Authorization": f"Bearer {self.token}", "Content-Type": "application/json"}
        data = None if body is None else json.dumps(body).encode("utf-8")
        status, payload = self.send(method, f"{API}{path}", headers, data)
        if status >= 300:
            raise RuntimeError(f"{method} {path} failed with HTTP {status}: {payload[:2000].decode('utf-8', 'replace')}")
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


def _token(send):
    tenant, client, secret = load_credentials()
    form = urllib.parse.urlencode({
        "grant_type": "client_credentials", "client_id": client, "client_secret": secret,
        "resource": "https://manage.devcenter.microsoft.com",
    }).encode("ascii")
    status, payload = send("POST", f"https://login.microsoftonline.com/{tenant}/oauth2/token",
                           {"Content-Type": "application/x-www-form-urlencoded"}, form)
    if status >= 300:
        raise RuntimeError(f"Sign-in failed with HTTP {status}.")
    return json.loads(payload)["access_token"]


def create_draft(release, send=_send, log=print):
    client = StoreClient(_token(send), send)
    application = client.call("GET", f"/applications/{APPLICATION_ID}")
    pending = (application.get("pendingApplicationSubmission") or {}).get("id")
    published = (application.get("lastPublishedApplicationSubmission") or {}).get("id")
    if pending:
        if pending == published:
            raise RuntimeError("The pending submission is the published one; refusing to delete it.")
        log(f"Replacing the existing unpublished draft {pending}.")
        client.call("DELETE", f"/applications/{APPLICATION_ID}/submissions/{pending}")
    draft = client.call("POST", f"/applications/{APPLICATION_ID}/submissions")
    submission_id = draft["id"]
    client.call("PUT", f"/applications/{APPLICATION_ID}/submissions/{submission_id}", prepare_submission(draft, release))
    with tempfile.TemporaryDirectory() as temp:
        upload = Path(temp) / "upload.zip"
        build_upload(release, upload)
        with upload.open("rb") as stream:
            status, payload = send("PUT", draft["fileUploadUrl"], {
                "x-ms-blob-type": "BlockBlob", "x-ms-version": "2020-04-08",
                "Content-Length": str(upload.stat().st_size),
            }, stream)
        if status >= 300:
            raise RuntimeError(f"File upload failed with HTTP {status}: {payload[:500].decode('utf-8', 'replace')}")
    state = client.call("GET", f"/applications/{APPLICATION_ID}/submissions/{submission_id}/status")
    log(f"Draft {submission_id} for SpanDraft {release.version} is ready (status {state.get('status')}). Not submitted.")
    log(f"Review and submit: https://partner.microsoft.com/dashboard/products/{APPLICATION_ID}/submissions/{submission_id}")
    return submission_id


def check_credentials(send=_send, log=print):
    StoreClient(_token(send), send).call("GET", f"/applications/{APPLICATION_ID}")
    log("Store credentials work.")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("release", type=Path, nargs="?", help="artifacts/store-release/<version>_<timestamp>")
    parser.add_argument("--check", action="store_true", help="validate the folder only")
    parser.add_argument("--check-credentials", action="store_true", help="sign in to the Store, change nothing")
    args = parser.parse_args()
    if args.release is None and not args.check_credentials:
        parser.error("the release folder is required")
    if args.check_credentials and (args.release or args.check):
        parser.error("--check-credentials takes no other arguments")
    try:
        if args.check_credentials:
            check_credentials()
            return
        release = Release(args.release)
        if args.check:
            print(f"Release folder for {release.version} is valid.")
            return
        create_draft(release)
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        raise SystemExit(f"error: {error}") from None


if __name__ == "__main__":
    sys.exit(main())
