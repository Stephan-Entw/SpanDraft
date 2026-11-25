import hashlib
import io
import json
import struct
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

from scripts import submit_microsoft_store_release as store
from scripts.step_progress import Steps


VERSION = "1.3.0"
PACKAGE = f"SpanDraft-{VERSION}.0-x64.msix"
MOTIFS = ["hero", "project", "section", "materials", "report", "start"]
OLD = "SpanDraft-1.2.0.0-x64.msix"
BLOB = "https://blob.test/upload?sig=SECRET"


def png(path, width, height):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", width, height) + b"\0" * 16)


def quiet():
    return Steps(10, stream=io.StringIO())


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
            {"fileName": "old/tile.png", "fileStatus": "Uploaded", "imageType": "PromotionalArt16x9", "id": "2"},
        ]}}
    return {
        "id": "published-1", "status": "Published", "targetPublishMode": "Immediate", "visibility": "Public",
        "applicationCategory": "Tools", "pricing": {"priceId": "Free"},
        "applicationPackages": [
            {"fileName": OLD, "fileStatus": "Uploaded", "id": "p1", "minimumSystemRam": "Memory2GB"},
            {"fileName": "Other.appx", "fileStatus": "Uploaded", "id": "p2"},
        ],
        "listings": {"de-de": listing("de"), "en-us": listing("en")},
    }


def copy(value):
    return json.loads(json.dumps(value))


class FakeStore:
    """A small stateful stand-in for the Store submission API. No network is involved."""

    def __init__(self, pending_status=None):
        self.calls, self.uploads = [], []
        self.published, self.pending = "published-1", None
        self.submissions = {"published-1": published_submission()}
        self.created = 0
        self.after_commit = ["CommitStarted", "PreProcessing"]
        self.upload_fault = self.commit_fault = self.tamper = None
        if pending_status:
            self.pending = "draft-0"
            self.submissions["draft-0"] = dict(published_submission(), id="draft-0", status=pending_status)

    def writes(self):
        return [(method, path) for method, path in self.calls if method != "GET"]

    def commits(self):
        return sum(path.endswith("/commit") for _, path in self.calls)

    def __call__(self, method, url, headers, data):
        if url.startswith("https://login.microsoftonline.com/"):
            return 200, b'{"access_token": "token"}'
        if url == BLOB:
            self.calls.append((method, "blob"))
            if isinstance(self.upload_fault, Exception):
                raise self.upload_fault
            if self.upload_fault:
                return self.upload_fault, f"<Error>{BLOB}</Error>".encode()
            self.uploads.append(data.read())
            return 201, b""
        app = f"/applications/{store.APPLICATION_ID}"
        path = url.removeprefix(store.API)
        self.calls.append((method, path.removeprefix(app)))
        reply = lambda value, status=200: (status, json.dumps(value).encode())
        if path == app:
            return reply({"lastPublishedApplicationSubmission": {"id": self.published},
                          "pendingApplicationSubmission": {"id": self.pending} if self.pending else None})
        if method == "POST" and path == f"{app}/submissions":
            self.created += 1
            self.pending = f"draft-{self.created}"
            self.submissions[self.pending] = dict(copy(self.submissions[self.published]), id=self.pending,
                                                  status="PendingCommit", fileUploadUrl=BLOB)
            return reply(self.submissions[self.pending], 201)
        submission_id, _, action = path.removeprefix(f"{app}/submissions/").partition("/")
        submission = self.submissions[submission_id]
        if method == "DELETE":
            del self.submissions[submission_id]
            self.pending = None
            return 204, b""
        if method == "PUT":
            self.submissions[submission_id] = stored = copy(json.loads(data))
            if self.tamper:
                self.tamper(stored)
            return reply(stored)
        if action == "commit":
            if self.commit_fault != "not-received":
                submission["status"] = self.after_commit[0]
            if self.commit_fault:
                raise TimeoutError("timed out")
            return reply({"status": "CommitStarted"}, 202)
        if action == "status":
            current = submission["status"]
            if current in self.after_commit[:-1]:
                submission["status"] = self.after_commit[self.after_commit.index(current) + 1]
            return reply({"status": current})
        return reply(submission)


class SubmitStoreReleaseTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.folder = ReleaseFolder(self.root)
        env = patch.dict("os.environ", {"MS_STORE_TENANT_ID": "t", "MS_STORE_CLIENT_ID": "c", "MS_STORE_CLIENT_SECRET": "s"})
        env.start()
        self.addCleanup(env.stop)

    def submit(self, fake, replace_draft=None):
        steps, now = quiet(), [0]
        self.output = steps.stream
        submission = store.Submission(store.Release(self.root), send=fake, steps=steps, clock=lambda: now[0],
                                      sleep=lambda seconds: now.__setitem__(0, now[0] + seconds))
        return submission.run(replace_draft)

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

    def test_prepared_body_forces_manual_publishing_and_replaces_only_the_app_package_and_screenshots(self):
        release = store.Release(self.root)
        body = store.prepare_submission(published_submission(), release)
        self.assertEqual(body["targetPublishMode"], "Manual")
        self.assertEqual([(p["fileName"], p["fileStatus"]) for p in body["applicationPackages"]],
                         [(OLD, "PendingDelete"), ("Other.appx", "Uploaded"), (PACKAGE, "PendingUpload")])
        self.assertEqual(body["applicationPackages"][2]["minimumSystemRam"], "Memory2GB")
        for language, culture in [("de-de", "de-DE"), ("en-us", "en-US")]:
            base = body["listings"][language]["baseListing"]
            self.assertEqual(base["description"], f"Beschreibung {language[:2]}")
            self.assertEqual(base["releaseNotes"], f"Neu in {culture}")
            self.assertEqual([(i["fileName"], i["fileStatus"]) for i in base["images"][:2]],
                             [("old/hero.png", "PendingDelete"), ("old/tile.png", "Uploaded")])
            self.assertEqual([i["fileName"] for i in base["images"][2:]], self.folder.manifest["screenshots"][culture])
        self.assertEqual((body["pricing"], body["visibility"]), ({"priceId": "Free"}, "Public"))
        self.assertEqual(store.prepare_submission(body, release), body)
        faults = [
            ("fr-fr", lambda s: s["listings"].update({"fr-fr": s["listings"]["de-de"]})),
            ("en-US", lambda s: s["listings"].pop("en-us")),
            ("not newer", lambda s: s["applicationPackages"][0].update(fileName="SpanDraft-1.3.0.0-x64.msix")),
        ]
        for message, change in faults:
            submission = published_submission()
            change(submission)
            with self.subTest(message), self.assertRaisesRegex(ValueError, message):
                store.prepare_submission(submission, release)

    def test_submits_for_certification_once_after_upload_and_verification_and_never_publishes(self):
        fake = FakeStore()
        self.assertEqual(self.submit(fake)["status"], "PreProcessing")
        self.assertEqual(fake.writes(), [("POST", "/submissions"), ("PUT", "/submissions/draft-1"), ("PUT", "blob"),
                                         ("POST", "/submissions/draft-1/commit")])
        verified = fake.calls.index(("POST", "/submissions/draft-1/commit")) - 1
        self.assertEqual(fake.calls[verified], ("GET", "/submissions/draft-1"))
        self.assertEqual(fake.submissions["draft-1"]["targetPublishMode"], "Manual")
        upload = self.root / "check.zip"
        upload.write_bytes(fake.uploads[0])
        with zipfile.ZipFile(upload) as archive:
            self.assertEqual(set(archive.namelist()),
                             {PACKAGE} | {f for files in self.folder.manifest["screenshots"].values() for f in files})
        output = self.output.getvalue()
        for expected in (VERSION, "draft-1", "PreProcessing", "Publish mode: Manual", store._link("draft-1"),
                         "NOT published"):
            self.assertIn(expected, output)
        state = (self.root / store.STATE_FILE).read_text(encoding="utf-8")
        self.assertIn('"stage": "committed"', state)
        self.assertNotIn("SECRET", state + output)

        before = len(fake.writes())
        self.submit(fake)  # In certification: report only.
        fake.published, fake.pending = "draft-1", None
        self.submit(fake)  # Published by the user: report only.
        self.assertEqual(len(fake.writes()), before)
        self.assertIn("already committed", self.output.getvalue())

    def test_nothing_is_committed_unless_the_saved_submission_matches(self):
        def images(stored):
            return stored["listings"]["de-de"]["baseListing"]["images"]

        faults = [
            ("publish mode is 'Immediate'", lambda s: s.update(targetPublishMode="Immediate")),
            ("publish mode is None", lambda s: s.pop("targetPublishMode")),
            ("not 'PendingDelete'", lambda s: s["applicationPackages"][0].update(fileStatus="Uploaded")),
            ("other packages changed", lambda s: s["applicationPackages"].pop(1)),
            ("pricing changed", lambda s: s.update(pricing={"priceId": "Tier2"})),
            ("description changed", lambda s: s["listings"]["de-de"]["baseListing"].update(description="x")),
            ("What's new differs", lambda s: s["listings"]["en-us"]["baseListing"].update(releaseNotes="x")),
            ("out of order", lambda s: images(s).reverse()),
            ("other images changed", lambda s: images(s).pop(1)),
        ]
        for message, tamper in faults:
            with self.subTest(message):
                (self.root / store.STATE_FILE).unlink(missing_ok=True)
                fake = FakeStore()
                fake.tamper = tamper
                with self.assertRaisesRegex(RuntimeError, message):
                    self.submit(fake)
                self.assertEqual(fake.commits(), 0)

    def test_an_open_draft_is_only_deleted_when_named_explicitly(self):
        for replace_draft, message in [(None, "--replace-draft draft-0"), ("other", "--replace-draft draft-0")]:
            fake = FakeStore(pending_status="PendingCommit")
            with self.assertRaisesRegex(RuntimeError, message):
                self.submit(fake, replace_draft)
            self.assertEqual(fake.writes(), [])
        for status in ("CommitStarted", "Certification", "Release", "PendingPublication", "CertificationFailed"):
            fake = FakeStore(pending_status=status)
            with self.subTest(status), self.assertRaisesRegex(RuntimeError, "already in progress"):
                self.submit(fake, "draft-0")
            self.assertEqual(fake.writes(), [])
        fake = FakeStore()
        with self.assertRaisesRegex(RuntimeError, "no draft draft-0"):
            self.submit(fake, "draft-0")
        self.assertEqual(fake.writes(), [])
        fake = FakeStore(pending_status="PendingCommit")
        self.submit(fake, "draft-0")
        self.assertEqual(fake.writes()[:2], [("DELETE", "/submissions/draft-0"), ("POST", "/submissions")])
        self.assertEqual(fake.commits(), 1)

    def test_a_rejected_release_writes_nothing_even_when_a_draft_would_be_replaced(self):
        fake = FakeStore(pending_status="PendingCommit")
        fake.submissions["published-1"]["applicationPackages"][0]["fileName"] = "SpanDraft-1.4.0.0-x64.msix"
        with self.assertRaisesRegex(ValueError, "not newer"):
            self.submit(fake, "draft-0")
        self.assertEqual(fake.writes(), [])

    def test_a_failed_upload_is_not_committed_and_the_same_draft_is_continued(self):
        for fault in (403, OSError(f"broken pipe {BLOB}"), ValueError(f"unknown url {BLOB}")):
            with self.subTest(fault=type(fault).__name__):
                (self.root / store.STATE_FILE).unlink(missing_ok=True)
                fake = FakeStore()
                fake.upload_fault = fault
                with self.assertRaisesRegex(RuntimeError, "File upload failed") as caught:
                    self.submit(fake)
                self.assertNotIn("SECRET", str(caught.exception) + self.output.getvalue())
                self.assertEqual(fake.commits(), 0)
                fake.upload_fault = None
                self.submit(fake)
                self.assertEqual((fake.created, fake.commits()), (1, 1))
                self.assertNotIn("DELETE", [method for method, _ in fake.calls])

    def test_a_commit_with_a_lost_answer_is_never_sent_again_by_itself(self):
        fake = FakeStore()
        fake.commit_fault = "received"
        self.submit(fake)
        self.assertEqual(fake.commits(), 1)
        (self.root / store.STATE_FILE).unlink()
        fake = FakeStore()
        fake.commit_fault = "not-received"
        with self.assertRaisesRegex(RuntimeError, "not confirmed"):
            self.submit(fake)
        self.assertEqual(fake.commits(), 1)

    def test_commit_failures_and_unfinished_processing_are_reported_not_repeated(self):
        fake = FakeStore()
        fake.after_commit = ["CommitStarted", "CommitFailed"]
        with self.assertRaisesRegex(RuntimeError, "CommitFailed"):
            self.submit(fake)
        self.submit(fake)
        self.assertEqual(fake.commits(), 1)
        fake.after_commit = ["CommitStarted", "PreProcessing"]
        self.submit(fake, "draft-1")  # Replacing the failed one is an explicit decision.
        self.assertEqual((fake.created, fake.commits()), (2, 2))
        (self.root / store.STATE_FILE).unlink()
        fake = FakeStore()
        fake.after_commit = ["CommitStarted"]
        with self.assertRaisesRegex(RuntimeError, "not finished"):
            self.submit(fake)
        self.assertEqual(fake.commits(), 1)

    def test_state_of_other_content_is_rejected_and_urls_are_redacted_from_errors(self):
        self.submit(FakeStore())
        state = json.loads((self.root / store.STATE_FILE).read_text(encoding="utf-8"))
        (self.root / store.STATE_FILE).write_text(json.dumps(dict(state, packageSha256="0")), encoding="utf-8")
        fake = FakeStore()
        with self.assertRaisesRegex(ValueError, "different release content"):
            self.submit(fake)
        self.assertEqual(fake.writes(), [])
        client = store.StoreClient("token", lambda *_: (400, f'{{"message": "bad {BLOB}"}}'.encode()))
        with self.assertRaisesRegex(store.StoreError, "<url>") as caught:
            client.call("PUT", "/x", {})
        self.assertNotIn("SECRET", str(caught.exception))

    def test_every_step_is_announced_and_a_failure_names_its_step(self):
        terminal = io.StringIO()
        terminal.isatty = lambda: True
        store.Submission(store.Release(self.root), send=FakeStore(), steps=Steps(10, stream=terminal),
                         sleep=lambda _: None).run()
        output = terminal.getvalue()
        self.assertEqual([line[:6] for line in output.split("\n") if line.startswith("[")],
                         [f"[{n}/10]"[:6] for n in range(1, 10)])
        self.assertIn("100 %", output)
        (self.root / store.STATE_FILE).unlink()
        failing = Steps(10, stream=io.StringIO())
        with self.assertRaises(RuntimeError):
            store.Submission(store.Release(self.root), send=FakeStore(pending_status="Certification"),
                             steps=failing).run()
        self.assertEqual(failing.failed, 3)

    def test_listing_languages_map_to_release_cultures(self):
        self.assertEqual([store.culture_for(x) for x in ("de-de", "DE", "en-us", "en-GB")], ["de-DE", "de-DE", "en-US", "en-US"])
        with self.assertRaises(ValueError):
            store.culture_for("fr-fr")


if __name__ == "__main__":
    unittest.main()
