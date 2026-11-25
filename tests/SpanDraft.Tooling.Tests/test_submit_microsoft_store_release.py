import hashlib
import io
import json
import re
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
ACCESS_TOKEN = "ACCESS-TOKEN-VALUE"
SENSITIVE = ("SECRET", "sig=", ACCESS_TOKEN, "fileUploadUrl")

# Write calls in the order the API receives them.
C = ("POST", "/submissions")
B = ("PUT", "blob")


def D(draft):
    return ("DELETE", f"/submissions/{draft}")


def U(draft):
    return ("PUT", f"/submissions/{draft}")


def K(draft):
    return ("POST", f"/submissions/{draft}/commit")


def png(path, width, height, extra=b""):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", width, height)
                     + b"\0" * 16 + extra)


def quiet():
    return Steps(10, stream=io.StringIO())


class ReleaseFolder:
    def __init__(self, root, version=VERSION):
        self.root, self.package = root, f"SpanDraft-{version}.0-x64.msix"
        (root / self.package).write_bytes(b"msix" + version.encode())
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
            "version": version, "tag": f"v{version}", "commit": "abc",
            "package": {"file": self.package, "sha256": hashlib.sha256(b"msix" + version.encode()).hexdigest()},
            "whatsNew": notes, "screenshots": screenshots,
        }
        self.save()

    def save(self):
        (self.root / "release.json").write_text(json.dumps(self.manifest), encoding="utf-8")


def published_submission():
    def listing(language):
        return {"baseListing": {"description": f"Beschreibung {language}", "releaseNotes": "alt", "keywords": ["statik"],
                                "privacyPolicy": "", "licenseTerms": "https://spandraft.test/license\n", "images": [
            {"fileName": "old/hero.png", "fileStatus": "Uploaded", "imageType": "Screenshot", "id": "1",
             "description": None},
            {"fileName": "old/tile.png", "fileStatus": "Uploaded", "imageType": "PromotionalArt16x9", "id": "2"},
        ]}, "platformOverrides": {}}
    return {
        "id": "published-1", "status": "Published", "friendlyName": "Submission 1",
        "statusDetails": {"errors": [], "certificationReports": [{"reportUrl": "https://reports.test/r?sig=SECRET"}]},
        "targetPublishMode": "Immediate", "targetPublishDate": "1601-01-01T00:00:00Z", "visibility": "Public",
        "applicationCategory": "Tools", "pricing": {"priceId": "Free", "trialPeriod": "NoFreeTrial", "sales": [],
                                                    "marketSpecificPricings": {}, "isAdvancedPricingModel": True},
        "hardwarePreferences": [], "automaticBackupEnabled": False, "canInstallOnRemovableMedia": True,
        "isGameDvrEnabled": False, "gamingOptions": [], "hasExternalInAppProducts": False,
        "meetAccessibilityGuidelines": True, "notesForCertification": "",
        "packageDeliveryOptions": {"packageRollout": {"isPackageRollout": False, "packageRolloutPercentage": 0,
                                                      "packageRolloutStatus": "PackageRolloutComplete",
                                                      "fallbackSubmissionId": "0"},
                                   "isMandatoryUpdate": False, "mandatoryUpdateEffectiveDate": "1601-01-01T00:00:00Z"},
        "enterpriseLicensing": "Online", "allowMicrosoftDecideAppAvailabilityToFutureDeviceFamilies": True,
        "allowTargetFutureDeviceFamilies": {"Desktop": True, "Xbox": False},
        "trailers": [],
        "applicationPackages": [
            {"fileName": OLD, "fileStatus": "Uploaded", "id": "p1", "version": "1.2.0.0", "architecture": "X64",
             "minimumDirectXVersion": "None", "minimumSystemRam": "Memory2GB", "targetPlatform": "Windows10"},
            {"fileName": "Other.appx", "fileStatus": "Uploaded", "id": "p2", "targetPlatform": "Windows10"},
        ],
        "listings": {"de-de": listing("de"), "en-us": listing("en")},
    }


def microsoft_normalizes(stored):
    """What the Store returned after the PUT in the first real run (1.2.0), with no real change."""
    for package in stored["applicationPackages"]:
        if package["fileStatus"] == "PendingDelete":
            package.pop("targetPlatform", None)
    for listing in stored["listings"].values():
        listing["baseListing"]["licenseTerms"] = listing["baseListing"]["licenseTerms"].rstrip("\n")
    stored["pricing"]["isAdvancedPricingModel"] = False


def copy(value):
    return json.loads(json.dumps(value))


def reply(value, status=200):
    return status, json.dumps(value).encode()


class FakeStore:
    """A stateful stand-in for the Store submission API, as documented. No network is involved.

    Like Partner Center, it assigns server fields itself, answers 404 for unknown submissions and 409 when
    a request does not fit the state, and can lose answers, delay status changes or be interrupted.
    """

    def __init__(self, pending_status=None):
        self.calls, self.uploads, self.logins, self.put_bodies = [], [], 0, []
        self.published, self.pending = "published-1", None
        self.submissions = {"published-1": published_submission()}
        self.created = 0
        self.after_commit = ["CommitStarted", "PreProcessing"]
        self.commit_delay = 0
        self.queues = {}
        self.faults = {}
        self.tamper = self.clone_tamper = self.get_tamper = None
        self.hidden = None
        if pending_status:
            self.pending = "draft-0"
            self.submissions["draft-0"] = dict(published_submission(), id="draft-0", status=pending_status,
                                               fileUploadUrl=BLOB)

    def writes(self):
        return [(method, path) for method, path in self.calls if method != "GET"]

    def publish(self, submission_id):
        """What the user's 'Publish now' leads to."""
        submission = self.submissions[submission_id]
        submission["status"] = "Published"
        submission["applicationPackages"] = [dict(p, fileStatus="Uploaded") for p in submission["applicationPackages"]
                                             if p["fileStatus"] != "PendingDelete"]
        self.published, self.pending = submission_id, None
        self.queues.pop(submission_id, None)

    def reveal(self):
        """A delayed create becomes visible."""
        self.submissions[self.hidden["id"]] = self.hidden
        self.pending, self.hidden = self.hidden["id"], None

    def _put(self, submission_id, body):
        old = self.submissions[submission_id]
        stored = copy(body)
        for key in store.SERVER_FIELDS:
            stored.pop(key, None)
            if key in old:
                stored[key] = old[key]
        for index, package in enumerate(stored.get("applicationPackages") or []):
            if package.get("fileStatus") == "PendingUpload":
                package.update(id=f"pkg-{index}", version=None, architecture="X64", languages=[], capabilities=[],
                               targetDeviceFamilies=[])
        for listing in stored.get("listings", {}).values():
            for index, image in enumerate(listing["baseListing"].get("images") or []):
                if image.get("fileStatus") == "PendingUpload":
                    image.update(id=f"img-{index}", description=None)
        stored["pricing"]["sales"] = []
        stored["packageDeliveryOptions"]["packageRollout"]["packageRolloutStatus"] = "PackageRolloutNotStarted"
        if self.tamper:
            self.tamper(stored)
        self.submissions[submission_id] = stored
        return stored

    def _commit(self, submission):
        queue = ["PendingCommit"] * self.commit_delay + list(self.after_commit)
        submission["status"] = queue.pop(0)
        self.queues[submission["id"]] = queue

    def __call__(self, method, url, headers, data):
        if url.startswith("https://login.microsoftonline.com/"):
            self.logins += 1
            return reply({"access_token": ACCESS_TOKEN})
        if url == BLOB:
            self.calls.append((method, "blob"))
            fault = self.faults.get("upload")
            if isinstance(fault, Exception):
                raise fault
            if fault:
                return fault, f"<Error>{BLOB}</Error>".encode()
            self.uploads.append(data.read())
            return 201, b""
        app = f"/applications/{store.APPLICATION_ID}"
        path = url.removeprefix(store.API)
        self.calls.append((method, path.removeprefix(app)))
        if path == app:
            return reply({"lastPublishedApplicationSubmission": {"id": self.published},
                          "pendingApplicationSubmission": {"id": self.pending} if self.pending else None})
        if method == "POST" and path == f"{app}/submissions":
            fault = self.faults.pop("create", None)
            if fault == "not-received":
                raise TimeoutError("timed out")
            if self.pending or self.hidden:
                return reply({"code": "InvalidState"}, 409)
            self.created += 1
            created = dict(copy(self.submissions[self.published]), id=f"draft-{self.created}", status="PendingCommit",
                           friendlyName=f"Submission {self.created + 1}", fileUploadUrl=BLOB, statusDetails={})
            created["packageDeliveryOptions"]["packageRollout"].update(packageRolloutStatus="PackageRolloutNotStarted",
                                                                       fallbackSubmissionId="0")
            if self.clone_tamper:
                self.clone_tamper(created)
            if fault == "delayed":
                self.hidden = created  # Becomes visible only with reveal().
                raise TimeoutError("timed out")
            self.submissions[created["id"]] = created
            self.pending = created["id"]
            if fault == "lost":
                raise TimeoutError("timed out")
            if fault == "other-id":
                return reply(dict(created, id="draft-9"), 201)
            return reply(created, 201)
        submission_id, _, action = path.removeprefix(f"{app}/submissions/").partition("/")
        submission = self.submissions.get(submission_id)
        if submission is None:
            return reply({"code": "NotFound"}, 404)
        if method == "DELETE":
            fault = self.faults.pop("delete", None)
            if fault == "not-received":
                raise TimeoutError("timed out")
            if submission["status"] not in store.DELETABLE:
                return reply({"code": "InvalidState"}, 409)
            del self.submissions[submission_id]
            self.pending = None
            if fault == "lost":
                raise TimeoutError("timed out")
            return 204, b""
        if method == "PUT":
            if self.faults.get("put"):
                return reply({"code": "InvalidState"}, self.faults["put"])
            self.put_bodies.append(json.loads(data))
            stored = self._put(submission_id, json.loads(data))
            return reply(dict(stored, id="draft-9") if self.faults.get("put-other-id") else stored)
        if action == "commit":
            fault = self.faults.pop("commit", None)
            if fault == "abort-before":
                raise KeyboardInterrupt
            if fault == "not-received":
                raise TimeoutError("timed out")
            if fault == 409 or submission["status"] != "PendingCommit":
                return reply({"code": "InvalidState"}, 409)
            self._commit(submission)
            if self.faults.get("mode-after-commit"):
                submission["targetPublishMode"] = self.faults["mode-after-commit"]
            if fault == "lost":
                raise TimeoutError("timed out")
            if fault == "abort-after":
                raise KeyboardInterrupt
            return reply({"status": "CommitStarted"}, 202)
        if action == "status":
            current = submission["status"]
            queue = self.queues.get(submission_id)
            if queue:
                submission["status"] = queue.pop(0)
            return reply({"status": current, "statusDetails": submission.get("statusDetails")})
        if self.calls[-2:-1] == [B] and self.faults.pop("abort-on-verify", None):
            raise KeyboardInterrupt
        answer = copy(submission)
        if self.get_tamper:
            self.get_tamper(answer)
        return reply(answer)


class SubmitStoreReleaseTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.temp = Path(temp.name)
        self.root = self.temp / "release"
        self.root.mkdir()
        self.folder = ReleaseFolder(self.root)
        self.journal = store.Journal(self.temp / "journal")
        self.output = io.StringIO()
        env = patch.dict("os.environ", {"MS_STORE_TENANT_ID": "t", "MS_STORE_CLIENT_ID": "c", "MS_STORE_CLIENT_SECRET": "s"})
        env.start()
        self.addCleanup(env.stop)
        self.addCleanup(self.assert_nothing_sensitive_is_kept)

    def assert_nothing_sensitive_is_kept(self):
        texts = [self.output.getvalue()]
        if self.journal.directory.exists():
            texts += [p.read_text(encoding="utf-8") for p in self.journal.directory.rglob("*.json")]
        for text in texts:
            for value in SENSITIVE:
                self.assertNotIn(value, text)

    def submit(self, fake, replace_draft=None, root=None):
        steps, now = quiet(), [0]
        steps.stream = self.output
        submission = store.Submission(store.Release(root or self.root), send=fake, steps=steps, clock=lambda: now[0],
                                      sleep=lambda seconds: now.__setitem__(0, now[0] + seconds), journal=self.journal)
        return submission.run(replace_draft)

    def refused(self, fake, message, replace_draft=None, root=None, error=RuntimeError):
        """Runs and expects a stop without any write call."""
        before = len(fake.writes())
        with self.assertRaisesRegex(error, message) as caught:
            self.submit(fake, replace_draft, root)
        self.assertEqual(fake.writes()[before:], [])
        return str(caught.exception)

    def written(self, fake, expected, replace_draft=None, root=None, message=None):
        before = len(fake.writes())
        if message:
            with self.assertRaisesRegex(RuntimeError, message):
                self.submit(fake, replace_draft, root)
        else:
            self.submit(fake, replace_draft, root)
        self.assertEqual(fake.writes()[before:], expected)

    def stage(self):
        record = self.journal.load()
        return record and record["stage"]

    def clear(self, token):
        store.clear_state(token, self.journal, stream=self.output)

    def other_folder(self, version=VERSION):
        root = self.temp / f"other-{version}"
        root.mkdir()
        ReleaseFolder(root, version)
        return root

    # Release folder and request body -----------------------------------------------------------
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
                original = copy(self.folder.manifest)
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

    def test_the_fingerprint_covers_package_screenshots_order_texts_and_tag_but_not_tool_details(self):
        base = store.Release(self.root).fingerprint
        self.folder.manifest.update(toolingCommit="x", createdUtc="y")  # As in the prepared 1.2.0 folder.
        self.folder.save()
        self.assertEqual(store.Release(self.root).fingerprint, base)
        changes = {
            "screenshots de-DE": lambda: png(self.root / "screenshots/de-DE/02-project.png", 3312, 1863, b"new"),
            "whatsNew en-US": lambda: (self.root / "whats-new/en-US.txt").write_text("Anders", encoding="utf-8"),
            "tag": lambda: (self.folder.manifest.update(tag="v9"), self.folder.save()),
            "screenshots en-US": lambda: (self.folder.manifest["screenshots"]["en-US"].insert(
                1, self.folder.manifest["screenshots"]["en-US"].pop(2)), self.folder.save()),
        }
        for name, change in changes.items():
            with self.subTest(name):
                before = store.Release(self.root)
                change()
                after = store.Release(self.root)
                self.assertNotEqual(after.fingerprint, before.fingerprint)
                self.assertEqual(store.content_changes(before.content, after.content), [name])

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

    def test_listing_languages_map_to_release_cultures(self):
        self.assertEqual([store.culture_for(x) for x in ("de-de", "DE", "en-us", "en-GB")], ["de-DE", "de-DE", "en-US", "en-US"])
        with self.assertRaises(ValueError):
            store.culture_for("fr-fr")

    # 1, 29: regular run, publication, no second submission of a published version -------------
    def test_submits_once_in_the_documented_order_and_never_publishes_or_resubmits(self):
        fake = FakeStore()
        self.written(fake, [C, U("draft-1"), B, K("draft-1")])
        verified = fake.calls.index(K("draft-1")) - 1
        self.assertEqual(fake.calls[verified], ("GET", "/submissions/draft-1"))
        self.assertEqual(fake.submissions["draft-1"]["targetPublishMode"], "Manual")
        self.assertEqual(self.stage(), "committed")
        archive = self.temp / "check.zip"
        archive.write_bytes(fake.uploads[0])
        with zipfile.ZipFile(archive) as opened:
            self.assertEqual(set(opened.namelist()),
                             {PACKAGE} | {f for files in self.folder.manifest["screenshots"].values() for f in files})
        output = self.output.getvalue()
        for expected in (VERSION, "draft-1", "Status:         PreProcessing", "Publish mode:   Manual",
                         store._link("draft-1"), "Submitted for certification", "NOT published"):
            self.assertIn(expected, output)

        self.written(fake, [])  # Still processing: report only.
        fake.publish("draft-1")  # The user chose "Publish now".
        self.written(fake, [])
        self.assertIn("this folder is published", self.output.getvalue())
        self.assertIsNone(self.journal.load())
        self.assertEqual(len(list(self.journal.history.iterdir())), 1)
        self.refused(fake, "not newer", error=ValueError)  # The journal is archived; the Store still says no.

    # 2-7: commit with unclear outcome ------------------------------------------------------------
    def test_a_received_commit_with_a_lost_answer_is_recognised_without_sending_it_again(self):
        for delay in (0, 3):
            with self.subTest(delay=delay):
                self.journal.path.unlink(missing_ok=True)
                fake = FakeStore()
                fake.commit_delay, fake.faults["commit"] = delay, "lost"
                self.written(fake, [C, U("draft-1"), B, K("draft-1")])
                self.assertEqual(self.stage(), "committed")
                self.written(fake, [])

    def test_an_unconfirmed_commit_is_never_repeated_and_only_the_user_resolves_it(self):
        fake = FakeStore()
        fake.faults["commit"] = "not-received"
        self.written(fake, [C, U("draft-1"), B, K("draft-1")], message="not confirmed")
        self.assertEqual(self.stage(), "commit-requested")
        self.refused(fake, "not confirmed")  # Same folder: reads only, also after the timeout.
        self.refused(fake, "not confirmed", root=self.other_folder())  # Identical content counts as this release.
        self.refused(fake, "still open", root=self.other_folder("1.3.1"))
        self.refused(fake, "unclear request", "draft-1")
        with self.assertRaisesRegex(RuntimeError, "expects --clear-state draft-1"):
            self.clear("create")
        self.assertEqual(self.stage(), "commit-requested")
        self.clear("draft-1")
        self.assertIn("confirms nothing", self.output.getvalue())
        self.assertIsNone(self.journal.load())
        self.refused(fake, "--replace-draft draft-1")  # Back to normal rules: the draft is still there.
        self.written(fake, [D("draft-1"), C, U("draft-2"), B, K("draft-2")], "draft-1")

    def test_interruptions_around_the_commit(self):
        fake = FakeStore()
        fake.faults["commit"] = "abort-before"
        self.written(fake, [C, U("draft-1"), B, K("draft-1")], message="Interrupted while committing")
        self.assertEqual(self.stage(), "commit-requested")
        self.refused(fake, "not confirmed")

        self.journal.path.unlink()
        fake = FakeStore()
        fake.faults["abort-on-verify"] = True  # Before commit-requested was saved: nothing was sent.
        with self.assertRaises(KeyboardInterrupt):
            self.submit(fake)
        self.assertEqual(self.stage(), "uploaded")
        self.written(fake, [U("draft-1"), B, K("draft-1")])

        self.journal.path.unlink()
        fake = FakeStore()
        fake.faults["commit"], fake.commit_delay = "abort-after", 2
        self.written(fake, [C, U("draft-1"), B, K("draft-1")], message="Interrupted while committing")
        self.written(fake, [])
        self.assertEqual(self.stage(), "committed")

    def test_commit_failures_and_unfinished_processing_are_reported_not_repeated(self):
        fake = FakeStore()
        fake.after_commit = ["CommitStarted", "CommitFailed"]
        self.written(fake, [C, U("draft-1"), B, K("draft-1")], message="CommitFailed")
        self.written(fake, [], message="CommitFailed")
        fake.after_commit = ["CommitStarted", "PreProcessing"]
        self.written(fake, [D("draft-1"), C, U("draft-2"), B, K("draft-2")], "draft-1")  # An explicit decision.
        self.journal.path.unlink()
        fake = FakeStore()
        fake.after_commit = ["CommitStarted"]
        self.written(fake, [C, U("draft-1"), B, K("draft-1")], message="not finished")
        self.written(fake, [], message="not finished")

    # 8-10: creating with unclear outcome ---------------------------------------------------------
    def test_an_unconfirmed_create_is_never_repeated(self):
        fake = FakeStore()
        fake.faults["create"] = "lost"
        self.written(fake, [C], message="unconfirmed")
        self.assertEqual(self.stage(), "create-requested")
        self.refused(fake, "Pending submission now: draft-1")
        self.refused(fake, "unclear request", "draft-1")
        self.refused(fake, "never arrived", root=self.other_folder("1.3.1"))
        self.clear("create")
        self.written(fake, [D("draft-1"), C, U("draft-2"), B, K("draft-2")], "draft-1")

    def test_a_create_that_may_still_arrive_is_not_sent_again(self):
        for fault in ("not-received", "delayed"):
            with self.subTest(fault):
                self.journal.path.unlink(missing_ok=True)
                fake = FakeStore()
                fake.faults["create"] = fault
                self.written(fake, [C], message="unconfirmed")
                self.refused(fake, "No pending submission is visible yet")
                if fault == "delayed":
                    fake.reveal()
                    self.refused(fake, "Pending submission now: draft-1")
                self.clear("create")
                if fault == "delayed":  # It arrived after all: the normal rules ask for an explicit decision.
                    self.refused(fake, "--replace-draft draft-1")
                else:
                    self.written(fake, [C, U("draft-1"), B, K("draft-1")])

    def test_a_create_answer_naming_another_submission_stops(self):
        fake = FakeStore()
        fake.faults["create"] = "other-id"
        self.written(fake, [C], message="draft-9, but draft-1 is pending")
        self.assertEqual(self.stage(), "create-requested")

    # 11, 11b, 28: deleting ------------------------------------------------------------------------
    def test_an_open_draft_is_only_deleted_when_named_explicitly_and_still_deletable(self):
        for replace_draft, message in ((None, "add: --replace-draft draft-0"), ("other", "no pending draft other"),
                                       ("published-1", "never deleted")):
            self.refused(FakeStore(pending_status="PendingCommit"), message, replace_draft)
        for status in ("CommitStarted", "Certification", "Release", "PendingPublication", "CertificationFailed",
                       "Published", "Canceled"):
            with self.subTest(status):
                self.refused(FakeStore(pending_status=status), "not touched", "draft-0")
        self.refused(FakeStore(), "no pending draft draft-0", "draft-0")
        # A prepared folder without journal (as 1.2.0) replaces an old draft explicitly.
        self.written(FakeStore(pending_status="PendingCommit"), [D("draft-0"), C, U("draft-1"), B, K("draft-1")],
                     "draft-0")

    def test_a_lost_delete_answer_is_resumed_only_if_the_store_still_fits(self):
        for fault, expected in (("lost", [C, U("draft-1"), B, K("draft-1")]),
                                ("not-received", [D("draft-0"), C, U("draft-1"), B, K("draft-1")])):
            with self.subTest(fault):
                self.journal.path.unlink(missing_ok=True)
                fake = FakeStore(pending_status="PendingCommit")
                fake.faults["delete"] = fault
                self.written(fake, [D("draft-0")], "draft-0", message="unconfirmed")
                self.assertEqual(self.stage(), "delete-requested")
                self.refused(fake, "--replace-draft draft-0")  # Without the user's flag: nothing.
                self.written(fake, expected, "draft-0")

        self.journal.path.unlink()
        fake = FakeStore(pending_status="PendingCommit")
        fake.faults["delete"] = "lost"
        self.written(fake, [D("draft-0")], "draft-0", message="unconfirmed")
        fake.submissions["other"] = dict(published_submission(), id="other", status="PendingCommit")
        fake.pending = "other"
        self.refused(fake, "draft-0 is gone, but submission other is pending", "draft-0")

        self.journal.path.unlink()
        fake = FakeStore(pending_status="PendingCommit")
        fake.faults["delete"] = "not-received"
        self.written(fake, [D("draft-0")], "draft-0", message="unconfirmed")
        fake.submissions["draft-0"]["status"] = "Certification"
        self.refused(fake, "not touched", "draft-0")

        self.journal.path.unlink()
        fake = FakeStore(pending_status="PendingCommit")
        fake.faults["delete"] = "lost"
        self.written(fake, [D("draft-0")], "draft-0", message="unconfirmed")
        fake.pending = "draft-0"  # Deleted, but still listed for a while.
        self.refused(fake, "still lists it as pending", "draft-0")

    # 12-13: parallel runs and other release folders -----------------------------------------------
    def test_a_parallel_run_is_refused_before_signing_in(self):
        fake = FakeStore()
        with self.journal.lock():
            for root in (self.root, self.other_folder()):
                with self.assertRaisesRegex(RuntimeError, "Another Store submission run"):
                    self.submit(fake, root=root)
        self.assertEqual((fake.calls, fake.logins), ([], 0))
        self.written(fake, [C, U("draft-1"), B, K("draft-1")])  # The lock is released.

    def test_another_folder_respects_the_open_journal(self):
        fake = FakeStore()
        fake.faults["upload"] = 500
        self.written(fake, [C, U("draft-1"), B], message="File upload failed")
        other = self.other_folder("1.3.1")
        self.refused(fake, "other content .*version", root=other)
        self.written(fake, [D("draft-1"), C, U("draft-2"), B], "draft-1", root=other, message="File upload failed")
        fake.faults.pop("upload")
        self.refused(fake, "other content", root=self.root)  # The journal now belongs to the other folder.

        self.journal.path.unlink()
        fake = FakeStore()
        fake.after_commit = ["CommitStarted", "Certification"]
        self.written(fake, [C, U("draft-1"), B, K("draft-1")])
        self.refused(fake, "still open", root=other)
        fake.publish("draft-1")
        self.written(fake, [C, U("draft-2"), B, K("draft-2")], root=other)
        self.assertEqual(len(list(self.journal.history.iterdir())), 1)

    # 14-15: changed content -------------------------------------------------------------------------
    def test_changed_release_content_is_never_attached_to_a_started_submission(self):
        changes = {
            "screenshots de-DE": lambda: png(self.root / "screenshots/de-DE/03-section.png", 3312, 1863, b"x"),
            "whatsNew en-US": lambda: (self.root / "whats-new/en-US.txt").write_text("Anders", encoding="utf-8"),
            "commit": lambda: (self.folder.manifest.update(commit="def"), self.folder.save()),
        }
        for name, change in changes.items():
            for stop, message in (("upload", "other content"), ("commit", "still open")):
                with self.subTest(name, stopped_at=stop):
                    self.journal.path.unlink(missing_ok=True)
                    self.folder = ReleaseFolder(self.root)
                    fake = FakeStore()
                    fake.faults[stop] = 500 if stop == "upload" else "not-received"
                    with self.assertRaises(RuntimeError):
                        self.submit(fake)
                    change()
                    text = self.refused(fake, message)
                    if stop == "upload":
                        self.assertIn(f"changed: {name}", text)

    def test_files_changed_after_validation_are_neither_uploaded_nor_used_to_delete(self):
        for pending, replace_draft in ((None, None), ("PendingCommit", "draft-0")):
            with self.subTest(pending=pending):
                fake = FakeStore(pending_status=pending)
                release = store.Release(self.root)
                png(self.root / "screenshots/en-US/04-materials.png", 3312, 1863, b"edited")
                submission = store.Submission(release, send=fake, steps=quiet(), journal=self.journal)
                with self.assertRaisesRegex(ValueError, "does not match the reviewed files"):
                    submission.run(replace_draft)
                self.assertEqual(fake.writes(), [])
                self.folder = ReleaseFolder(self.root)

    # 16-21: verification of what the Store saved -----------------------------------------------------
    def test_a_clone_that_differs_from_the_published_submission_is_not_changed(self):
        fake = FakeStore()
        fake.clone_tamper = lambda s: s.update(enterpriseLicensing="None")
        self.written(fake, [C], message="submission.enterpriseLicensing differs")
        self.assertEqual(self.stage(), "created")
        self.refused(fake, "enterpriseLicensing differs")  # Continuing does not skip the check.
        self.refused(fake, "--replace-draft draft-1", root=self.other_folder("1.3.1"))

    def test_nothing_is_committed_unless_only_the_intended_content_changed(self):
        def base(stored, language="de-de"):
            return stored["listings"][language]["baseListing"]

        faults = {
            "packageDeliveryOptions.isMandatoryUpdate": lambda s: s["packageDeliveryOptions"].update(isMandatoryUpdate=True),
            "packageDeliveryOptions.mandatoryUpdateEffectiveDate":
                lambda s: s["packageDeliveryOptions"].update(mandatoryUpdateEffectiveDate="2030-01-01T00:00:00Z"),
            "packageRollout.packageRolloutPercentage":
                lambda s: s["packageDeliveryOptions"]["packageRollout"].update(packageRolloutPercentage=10),
            "allowTargetFutureDeviceFamilies.Xbox": lambda s: s["allowTargetFutureDeviceFamilies"].update(Xbox=True),
            "enterpriseLicensing": lambda s: s.update(enterpriseLicensing="None"),
            "trailers": lambda s: s.update(trailers=[{"id": "t"}]),
            "gamingOptions": lambda s: s.update(gamingOptions=[{"genres": []}]),
            "hardwarePreferences": lambda s: s.update(hardwarePreferences=["Touch"]),
            "notesForCertification": lambda s: s.update(notesForCertification="x"),
            "pricing.priceId": lambda s: s["pricing"].update(priceId="Tier2"),
            "visibility": lambda s: s.update(visibility="Hidden"),
            "targetPublishDate": lambda s: s.update(targetPublishDate="2030-01-01T00:00:00Z"),
            "baseListing.description": lambda s: base(s).update(description="x"),
            "baseListing.keywords": lambda s: base(s, "en-us").update(keywords=[]),
            "platformOverrides": lambda s: s["listings"]["de-de"].update(platformOverrides={"Windows81": {}}),
            "images.others": lambda s: base(s)["images"].pop(1),
            "Other.appx": lambda s: s["applicationPackages"].pop(1),
            f"{OLD}.fileStatus": lambda s: s["applicationPackages"][0].update(fileStatus="Uploaded"),
            f"{OLD}.minimumSystemRam": lambda s: s["applicationPackages"][0].update(minimumSystemRam="Memory4GB"),
            "new screenshots[0].description": lambda s: base(s)["images"][2].update(description="Bild"),
            "new screenshots[0].isNew is unexpected": lambda s: base(s)["images"][2].update(isNew=True),
            "new screenshots[0].fileName": lambda s: base(s)["images"].insert(2, base(s)["images"].pop(3)),
            "releaseNotes": lambda s: base(s, "en-us").update(releaseNotes="x"),
            "isGameDvrEnabled is missing": lambda s: s.pop("isGameDvrEnabled"),
            "hasAdvertising is unexpected": lambda s: s.update(hasAdvertising=None),
            "notesForCertification differs": lambda s: s.update(notesForCertification=None),
            "publish mode is 'Immediate'": lambda s: s.update(targetPublishMode="Immediate"),
            "publish mode is 'SpecificDate'": lambda s: s.update(targetPublishMode="SpecificDate"),
            "publish mode is None": lambda s: s.pop("targetPublishMode"),
        }
        for message, tamper in faults.items():
            with self.subTest(message):
                self.journal.path.unlink(missing_ok=True)
                fake = FakeStore()
                fake.tamper = tamper
                self.written(fake, [C, U("draft-1"), B], message=f"Not committed.*{re.escape(message)}")

    def test_documented_server_changes_do_not_stop_the_commit(self):
        fake = FakeStore()  # Fills package and image fields, resets the rollout status, empties sales.

        def tamper(stored):
            base = stored["listings"]["de-de"]["baseListing"]
            base["releaseNotes"] = "Neu in de-DE\r\n"
            base["privacyPolicy"] = None
            base["images"][2]["fileName"] = base["images"][2]["fileName"].replace("/", "\\")
            stored["packageDeliveryOptions"]["packageRollout"]["fallbackSubmissionId"] = "123"
            stored["friendlyName"] = "Submission 7"

        fake.tamper = tamper
        self.written(fake, [C, U("draft-1"), B, K("draft-1")])

    def test_the_normalizations_observed_in_the_first_real_run_do_not_stop_the_commit(self):
        fake = FakeStore()
        fake.tamper = microsoft_normalizes
        self.written(fake, [C, U("draft-1"), B, K("draft-1")])

    def test_an_uploaded_submission_stopped_by_these_normalizations_is_continued(self):
        # As with 1.2.0: uploaded, then stopped by the verification of the earlier version of this tool.
        fake = FakeStore()
        fake.tamper = microsoft_normalizes
        earlier = {"IGNORED_PRICING_FIELDS": ("sales",), "IGNORED_REPLACED_PACKAGE_FIELDS": (),
                   "TRIMMED_LISTING_TEXTS": ("releaseNotes",)}
        with patch.multiple(store, **earlier):
            text = None
            try:
                self.submit(fake)
            except RuntimeError as error:
                text = str(error)
        self.assertEqual(fake.writes(), [C, U("draft-1"), B])
        for path in (f"applicationPackages.{OLD}.targetPlatform is missing", "licenseTerms differs",
                     "pricing.isAdvancedPricingModel differs"):
            self.assertIn(path, text)
        self.assertEqual(self.stage(), "uploaded")
        self.written(fake, [U("draft-1"), B, K("draft-1")])
        self.assertEqual(self.stage(), "committed")

    def test_real_license_and_pricing_changes_still_stop_the_commit(self):
        def license_terms(value):
            return lambda s: s["listings"]["en-us"]["baseListing"].update(licenseTerms=value)

        def other_package(s):
            return s["applicationPackages"][1]

        faults = {
            "licenseTerms differs": license_terms("https://other.test/license"),
            "licenseTerms differs ": license_terms("https://spandraft.test/lic ense"),
            "Other.appx.targetPlatform differs": lambda s: other_package(s).update(targetPlatform="Windows8"),
            "Other.appx.targetPlatform is missing": lambda s: other_package(s).pop("targetPlatform"),
            "pricing.priceId differs": lambda s: s["pricing"].update(priceId="Tier2"),
            "pricing.trialPeriod differs": lambda s: s["pricing"].update(trialPeriod="SevenDays"),
            "pricing.marketSpecificPricings.DE is unexpected":
                lambda s: s["pricing"]["marketSpecificPricings"].update(DE="Tier2"),
            "pricing.isFree is unexpected": lambda s: s["pricing"].update(isFree=False),
        }
        for message, change in faults.items():
            with self.subTest(message):
                self.journal.path.unlink(missing_ok=True)
                fake = FakeStore()
                fake.tamper = lambda s, change=change: (microsoft_normalizes(s), change(s))
                self.written(fake, [C, U("draft-1"), B], message=f"Not committed.*{re.escape(message.strip())}")

    def test_the_published_submission_stays_the_reference_for_a_continued_draft(self):
        fake = FakeStore()
        fake.faults["upload"] = 500
        self.written(fake, [C, U("draft-1"), B], message="File upload failed")
        fake.faults.pop("upload")
        record = self.journal.load()
        record["baseline"]["listings"]["de-de"]["baseListing"]["licenseTerms"] = "https://other.test/license"
        self.journal.save(record)
        self.refused(fake, "differs from the published submission.*licenseTerms differs")

    def test_the_publish_mode_is_watched_after_the_commit(self):
        fake = FakeStore()
        fake.faults["mode-after-commit"] = "Immediate"
        self.written(fake, [C, U("draft-1"), B, K("draft-1")], message="no longer 'Manual'")

    def test_answers_about_another_submission_stop_before_further_writes(self):
        fake = FakeStore()
        fake.get_tamper = lambda s: s.update(id="draft-9") if s["id"].startswith("draft") else None
        self.written(fake, [C], message="another submission than draft-1")
        self.journal.path.unlink()
        fake = FakeStore()
        fake.faults["put-other-id"] = True
        self.written(fake, [C, U("draft-1")], message="names another submission")

    def test_a_conflict_is_explained_with_all_possible_causes(self):
        fake = FakeStore()
        fake.faults["put"] = 409
        self.written(fake, [C, U("draft-1")], message="Possible causes.*changes made in Partner Center.*not support")
        self.journal.path.unlink()
        fake = FakeStore()
        fake.faults["commit"] = 409
        self.written(fake, [C, U("draft-1"), B, K("draft-1")], message="not confirmed")
        self.refused(fake, "not confirmed")

    # 22-25: unknown states, finished submissions, invalid parameters --------------------------------
    def test_unknown_statuses_are_only_read(self):
        for status in ("Mystery", None):
            with self.subTest(status=status):
                fake = FakeStore(pending_status="PendingCommit")
                fake.submissions["draft-0"]["status"] = status
                self.refused(fake, f"status {status}")
                self.refused(fake, "not touched", "draft-0")
        fake = FakeStore()
        self.written(fake, [C, U("draft-1"), B, K("draft-1")])
        fake.submissions["draft-1"]["status"] = "Mystery"
        self.refused(fake, "unknown status 'Mystery'")

    def test_own_submissions_after_the_commit_are_only_reported(self):
        for status in ("Certification", "PendingPublication", "Publishing", "CertificationFailed", "Canceled"):
            with self.subTest(status):
                self.journal.path.unlink(missing_ok=True)
                fake = FakeStore()
                fake.after_commit = ["CommitStarted", status]
                with self.assertRaises(RuntimeError) if status in store.FAILED else contextlib_null():
                    self.submit(fake)
                self.assertEqual(self.stage(), "committed")
                before = len(fake.writes())
                try:
                    self.submit(fake)
                except RuntimeError:
                    pass
                self.assertEqual(fake.writes()[before:], [])
                self.refused(fake, "--replace-draft is refused", "draft-1")

    def test_a_vanished_submission_is_only_resolved_by_the_user(self):
        fake = FakeStore()
        self.written(fake, [C, U("draft-1"), B, K("draft-1")])
        del fake.submissions["draft-1"]
        fake.pending = None
        self.refused(fake, "no longer pending.*--clear-state draft-1")
        self.refused(fake, "no longer pending", root=self.other_folder("1.3.1"))
        self.clear("draft-1")
        self.assertEqual(len(list(self.journal.history.iterdir())), 1)
        self.written(fake, [C, U("draft-2"), B, K("draft-2")])

    def test_invalid_parameters_change_nothing(self):
        with self.assertRaisesRegex(RuntimeError, "no open submission journal"):
            self.clear("create")
        for argv in (["--clear-state", "x", "folder"], ["--clear-state", "x", "--replace-draft", "y"],
                     ["folder", "--check", "--replace-draft", "y"], ["--check-credentials", "folder"], []):
            with self.subTest(argv), patch("sys.stderr", io.StringIO()), self.assertRaises(SystemExit):
                store.parse_arguments(argv)
        self.assertEqual(store.parse_arguments(["--clear-state", "create"]).clear_state, "create")

    def test_unreadable_journals_stop_everything(self):
        self.journal.directory.mkdir(parents=True)
        for text in ("{", json.dumps({"schema": 0}), json.dumps({"schema": 1, "applicationId": "X", "stage": "created"})):
            with self.subTest(text):
                self.journal.path.write_text(text, encoding="utf-8")
                self.refused(FakeStore(), "unreadable or of another format")
        with self.assertRaisesRegex(RuntimeError, "upload URL"):
            self.journal.save({"baseline": {"fileUploadUrl": "x"}})

    # Upload, output and redaction -------------------------------------------------------------------
    def test_a_failed_upload_is_not_committed_and_the_same_draft_is_continued(self):
        for fault in (403, OSError(f"broken pipe {BLOB}"), ValueError(f"unknown url {BLOB}")):
            with self.subTest(fault=type(fault).__name__):
                self.journal.path.unlink(missing_ok=True)
                fake = FakeStore()
                fake.faults["upload"] = fault
                self.written(fake, [C, U("draft-1"), B], message="File upload failed")
                fake.faults.pop("upload")
                self.written(fake, [U("draft-1"), B, K("draft-1")])

    def test_urls_are_redacted_from_errors(self):
        client = store.StoreClient("token", lambda *_: (400, f'{{"message": "bad {BLOB}"}}'.encode()))
        with self.assertRaisesRegex(store.StoreError, "<url>") as caught:
            client.call("PUT", "/x", {})
        self.assertNotIn("SECRET", str(caught.exception))

    def test_every_step_is_announced_and_a_failure_names_its_step(self):
        redirected = io.StringIO()
        store.Submission(store.Release(self.root), send=FakeStore(), steps=Steps(10, stream=redirected, mode="log"),
                         sleep=lambda _: None, journal=self.journal).run()
        results = [line for line in redirected.getvalue().split("\n") if line.startswith("[") and ": ok" in line]
        self.assertEqual([line[:7] for line in results], [f"[{n:>2}/10]" for n in range(1, 10)])
        self.assertIn("100%", redirected.getvalue())
        self.journal.path.unlink()
        failing = Steps(10, stream=io.StringIO())
        with self.assertRaises(RuntimeError):
            store.Submission(store.Release(self.root), send=FakeStore(pending_status="Certification"),
                             steps=failing, journal=self.journal).run()
        self.assertEqual(failing.failed, 3)

    # Request body and output -----------------------------------------------------------------------
    def test_the_put_body_has_no_sales_and_keeps_every_other_price_setting(self):
        fake = FakeStore()
        self.written(fake, [C, U("draft-1"), B, K("draft-1")])
        baseline = self.journal.load()["baseline"]["pricing"]
        self.assertIn("sales", baseline)  # The journal keeps what Microsoft returned.
        (body,) = fake.put_bodies
        self.assertNotIn("sales", body["pricing"])
        self.assertEqual(body["pricing"], {key: value for key, value in baseline.items() if key != "sales"})
        self.assertEqual(body["pricing"], {"priceId": "Free", "trialPeriod": "NoFreeTrial", "marketSpecificPricings": {},
                                           "isAdvancedPricingModel": True})
        release = store.Release(self.root)
        self.assertEqual(store.prepare_submission(store.prepare_submission(published_submission(), release), release),
                         store.prepare_submission(published_submission(), release))

    def test_the_final_message_matches_the_confirmed_store_status(self):
        cases = {
            "PreProcessing": ("Submitted for certification", "Certification is still pending"),
            "Certification": ("In certification", "Certification is running"),
            "Release": ("Certified, not published", "Check the release status"),
            "PendingPublication": ("Certified, not published", 'select "Publish now"'),
            "Published": ("Published", None),
        }
        for status, (headline, note) in cases.items():
            with self.subTest(status):
                self.journal.path.unlink(missing_ok=True)
                self.output = io.StringIO()
                fake = FakeStore()
                fake.after_commit = ["CommitStarted", status]
                self.submit(fake)
                output = self.output.getvalue()
                self.assertIn(f"OK {headline}\n", output)
                if note:
                    self.assertIn(note, output)
                    self.assertIn("NOT published", output)
                else:
                    self.assertNotIn("NOT published", output)
                self.assertEqual("Publish now" in output, status == "PendingPublication")

    def test_stopped_runs_show_no_success_and_no_request_to_publish(self):
        scenarios = [
            ("commit", "not-received", None),  # Unconfirmed commit.
            ("mode-after-commit", "Immediate", None),
            (None, None, ["CommitStarted"]),  # Still processing.
            (None, None, ["CommitStarted", "CommitFailed"]),
        ]
        for fault, value, after in scenarios:
            with self.subTest(fault=fault, after=after):
                self.journal.path.unlink(missing_ok=True)
                self.output = io.StringIO()
                fake = FakeStore()
                if fault:
                    fake.faults[fault] = value
                if after:
                    fake.after_commit = after
                with self.assertRaises(RuntimeError):
                    self.submit(fake)
                for unwanted in ("OK Submitted", "Certified", "Publish now", "OK In certification"):
                    self.assertNotIn(unwanted, self.output.getvalue())

    def test_a_failing_terminal_changes_nothing_in_the_workflow(self):
        class Broken(io.StringIO):
            def __init__(self, after):
                super().__init__()
                self.after = after

            def write(self, text):
                self.after -= 1
                if self.after < 0:
                    raise OSError("the console is gone")
                return super().write(text)

            def isatty(self):
                return True

        for after in (0, 3, 20, 60):
            with self.subTest(after=after):
                self.journal.path.unlink(missing_ok=True)
                fake = FakeStore()
                for mode in ("live", "log"):
                    steps = Steps(10, stream=Broken(after), mode=mode, tick=None)
                    store.Submission(store.Release(self.root), send=fake, steps=steps, sleep=lambda _: None,
                                     journal=self.journal).run()
                    if after == 0:
                        self.assertTrue(steps.broken)
                self.assertEqual(fake.writes(), [C, U("draft-1"), B, K("draft-1")])
                self.assertEqual(self.stage(), "committed")

    def test_the_countdown_keeps_the_poll_interval_and_timeout(self):
        fake = FakeStore()
        fake.after_commit = ["CommitStarted"]
        slept, now = [], [0]

        def sleep(seconds):
            slept.append(seconds)
            now[0] += seconds

        submission = store.Submission(store.Release(self.root), send=fake, steps=quiet(), clock=lambda: now[0],
                                      sleep=sleep, journal=self.journal)
        with self.assertRaisesRegex(RuntimeError, "not finished"):
            submission.run()
        reads = fake.calls.count(("GET", "/submissions/draft-1/status"))
        self.assertEqual(reads, store.POLL_TIMEOUT // store.POLL_SECONDS + 1)
        self.assertEqual((sum(slept), now[0]), (store.POLL_TIMEOUT, store.POLL_TIMEOUT))
        self.assertEqual(set(slept), {1})


class contextlib_null:
    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False


if __name__ == "__main__":
    unittest.main()
