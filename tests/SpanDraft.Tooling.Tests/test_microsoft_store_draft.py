import hashlib
import json
import struct
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

from scripts import microsoft_store_draft as store


VERSION = "1.3.0"
PACKAGE = f"SpanDraft-{VERSION}.0-x64.msix"
MOTIFS = ["hero", "project", "section", "materials", "report", "start"]


def png(path, width, height):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", width, height) + b"\0" * 16)


class ReleaseFolder:
    def __init__(self, root):
        self.root = root
        (root / PACKAGE).write_bytes(b"msix")
        screenshots, notes = {}, {}
        for culture in store.CULTURES:
            screenshots[culture] = []
            for index, motif in enumerate(MOTIFS, start=1):
                relative = f"screenshots/{culture}/{index:02d}-{motif}.png"
                png(root / relative, 3840 if motif == "hero" else 3312, 2160 if motif == "hero" else 1863)
                screenshots[culture].append(relative)
            notes[culture] = f"whats-new/{culture}.txt"
            (root / notes[culture]).parent.mkdir(exist_ok=True)
            (root / notes[culture]).write_text(f"Neu in {culture}\n", encoding="utf-8")
        self.manifest = {
            "version": VERSION, "tag": f"v{VERSION}", "commit": "abc",
            "package": {"file": PACKAGE, "sha256": hashlib.sha256(b"msix").hexdigest()},
            "whatsNew": notes, "screenshots": screenshots,
        }
        self.save()

    def save(self):
        (self.root / "release.json").write_text(json.dumps(self.manifest), encoding="utf-8")


def published_submission():
    def listing(language):
        return {"baseListing": {"description": f"Beschreibung {language}", "releaseNotes": "alt", "images": [
            {"fileName": "old/hero.png", "fileStatus": "Uploaded", "imageType": "Screenshot", "id": "1"},
            {"fileName": "old/tile.png", "fileStatus": "Uploaded", "imageType": "StoreLogo300x300", "id": "2"},
        ]}}
    return {
        "id": "draft-2", "fileUploadUrl": "https://blob.test/upload?sas",
        "applicationPackages": [{"fileName": "SpanDraft-1.2.0.0-x64.msix", "fileStatus": "Uploaded", "id": "p1"}],
        "listings": {"de-de": listing("de"), "en-us": listing("en")},
    }


class FakeStore:
    """Records every HTTP call; answers like the Store submission API."""

    def __init__(self, pending="draft-1", published="published-1"):
        self.calls, self.upload, self.updated = [], None, None
        self.application = {
            "id": store.APPLICATION_ID,
            "lastPublishedApplicationSubmission": {"id": published},
            "pendingApplicationSubmission": {"id": pending} if pending else None,
        }

    def __call__(self, method, url, headers, data):
        self.calls.append((method, url))
        if url.startswith("https://login.microsoftonline.com/"):
            return 200, b'{"access_token": "token"}'
        if url.startswith("https://blob.test/"):
            self.upload = data.read()
            return 201, b""
        path = url.removeprefix(store.API)
        app = f"/applications/{store.APPLICATION_ID}"
        if method == "GET" and path == app:
            return 200, json.dumps(self.application).encode()
        if method == "DELETE" and path == f"{app}/submissions/draft-1":
            return 204, b""
        if method == "POST" and path == f"{app}/submissions":
            return 201, json.dumps(published_submission()).encode()
        if method == "PUT" and path == f"{app}/submissions/draft-2":
            self.updated = json.loads(data)
            return 200, data
        if method == "GET" and path == f"{app}/submissions/draft-2/status":
            return 200, b'{"status": "PendingCommit"}'
        return 404, b"unexpected"


class MicrosoftStoreDraftTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.folder = ReleaseFolder(self.root)
        env = patch.dict("os.environ", {"MSSTORE_TENANT_ID": "t", "MSSTORE_CLIENT_ID": "c", "MSSTORE_CLIENT_SECRET": "s"})
        env.start()
        self.addCleanup(env.stop)

    def test_release_folder_is_validated_before_any_store_access(self):
        release = store.Release(self.root)
        self.assertEqual([p.name for p in release.screenshots["de-DE"]], [f"{i:02d}-{m}.png" for i, m in enumerate(MOTIFS, 1)])
        faults = [
            ("package", lambda m: m["package"].update(sha256="0" * 64), "SHA-256"),
            ("hero", lambda m: m["screenshots"]["en-US"].reverse(), "hero image first"),
            ("count", lambda m: m["screenshots"]["en-US"].extend(m["screenshots"]["en-US"]), "1 to 10"),
            ("escape", lambda m: m["screenshots"]["de-DE"].append("../outside.png"), "invalid release file"),
        ]
        for name, change, message in faults:
            with self.subTest(name):
                original = json.loads(json.dumps(self.folder.manifest))
                change(self.folder.manifest)
                self.folder.save()
                with self.assertRaisesRegex(ValueError, message):
                    store.Release(self.root)
                self.folder.manifest = original
                self.folder.save()
        (self.root / "whats-new/de-DE.txt").write_text("x" * 1501, encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "1 to 1500"):
            store.Release(self.root)
        (self.root / "whats-new/de-DE.txt").write_text("ok", encoding="utf-8")
        png(self.root / "screenshots/de-DE/02-project.png", 1280, 720)
        with self.assertRaisesRegex(ValueError, "1366x768"):
            store.Release(self.root)

    def test_submission_keeps_other_fields_and_replaces_only_screenshots_in_order(self):
        body = store.prepare_submission(published_submission(), store.Release(self.root))
        self.assertEqual([p["fileName"] for p in body["applicationPackages"]], ["SpanDraft-1.2.0.0-x64.msix", PACKAGE])
        self.assertEqual(body["applicationPackages"][1]["fileStatus"], "PendingUpload")
        for language, culture in [("de-de", "de-DE"), ("en-us", "en-US")]:
            base = body["listings"][language]["baseListing"]
            self.assertEqual(base["description"], f"Beschreibung {language[:2]}")
            self.assertEqual(base["releaseNotes"], f"Neu in {culture}")
            self.assertEqual([(i["fileName"], i["fileStatus"]) for i in base["images"][:2]],
                             [("old/hero.png", "PendingDelete"), ("old/tile.png", "Uploaded")])
            self.assertEqual([i["fileName"] for i in base["images"][2:]], self.folder.manifest["screenshots"][culture])
        unknown = published_submission()
        unknown["listings"]["fr-fr"] = unknown["listings"]["de-de"]
        with self.assertRaisesRegex(ValueError, "fr-fr"):
            store.prepare_submission(unknown, store.Release(self.root))
        only_german = published_submission()
        del only_german["listings"]["en-us"]
        with self.assertRaisesRegex(ValueError, "en-US"):
            store.prepare_submission(only_german, store.Release(self.root))

    def test_draft_replaces_only_the_unpublished_draft_and_is_never_committed(self):
        fake = FakeStore()
        self.assertEqual(store.create_draft(store.Release(self.root), send=fake, log=lambda _: None), "draft-2")
        app = f"{store.API}/applications/{store.APPLICATION_ID}"
        self.assertEqual(fake.calls[1:], [
            ("GET", app), ("DELETE", f"{app}/submissions/draft-1"), ("POST", f"{app}/submissions"),
            ("PUT", f"{app}/submissions/draft-2"), ("PUT", "https://blob.test/upload?sas"),
            ("GET", f"{app}/submissions/draft-2/status"),
        ])
        self.assertFalse(any("commit" in url for _, url in fake.calls))
        upload = self.root / "upload.zip"
        upload.write_bytes(fake.upload)
        with zipfile.ZipFile(upload) as archive:
            names = set(archive.namelist())
        expected = {PACKAGE} | {f for files in self.folder.manifest["screenshots"].values() for f in files}
        self.assertEqual(names, expected)
        referenced = {p["fileName"] for p in fake.updated["applicationPackages"] if p["fileStatus"] == "PendingUpload"}
        referenced |= {i["fileName"] for listing in fake.updated["listings"].values()
                       for i in listing["baseListing"]["images"] if i["fileStatus"] == "PendingUpload"}
        self.assertEqual(referenced, expected)

    def test_without_a_draft_nothing_is_deleted(self):
        fake = FakeStore(pending=None)
        store.create_draft(store.Release(self.root), send=fake, log=lambda _: None)
        self.assertNotIn("DELETE", [method for method, _ in fake.calls])

    def test_the_published_submission_is_never_deleted(self):
        fake = FakeStore(pending="published-1")
        with self.assertRaisesRegex(RuntimeError, "refusing"):
            store.create_draft(store.Release(self.root), send=fake, log=lambda _: None)
        self.assertNotIn("DELETE", [method for method, _ in fake.calls])

    def test_listing_languages_map_to_release_cultures(self):
        self.assertEqual([store.culture_for(x) for x in ("de-de", "DE", "en-us", "en-GB")], ["de-DE", "de-DE", "en-US", "en-US"])
        with self.assertRaises(ValueError):
            store.culture_for("fr-fr")


if __name__ == "__main__":
    unittest.main()
