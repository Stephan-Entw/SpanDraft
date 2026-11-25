import json
import os
import subprocess
import tempfile
import unittest
import uuid
from pathlib import Path

from scripts import bitwarden_secrets as bitwarden
from scripts import submit_microsoft_store_release as store


TOKEN = "0.token-value:key"
KEYS = store.CREDENTIAL_NAMES


def secrets(**overrides):
    values = {key: f"value-of-{key}" for key in KEYS} | {"OTHER": "foreign"} | overrides
    return [{"key": key, "value": value} for key, value in values.items() if value is not None]


class FakeBws:
    def __init__(self, entries=None, projects=None, returncode=0):
        self.calls = []
        self.projects = [{"id": "p1", "name": "SpanDraft"}, {"id": "p2", "name": "Other"}] if projects is None else projects
        self.entries = secrets() if entries is None else entries
        self.returncode = returncode

    def __call__(self, command, **options):
        self.calls.append((command, options))
        data = self.projects if command[1] == "project" else self.entries
        # Failing output carries sensitive text to prove that it is never repeated.
        return subprocess.CompletedProcess(command, self.returncode, json.dumps(data), f"denied {TOKEN}")


def read(fake):
    return bitwarden.read_secrets("SpanDraft", KEYS, credential=lambda: TOKEN, find=lambda: "bws", run=fake)


class BitwardenSecretsTests(unittest.TestCase):
    def test_reads_only_the_project_and_passes_the_token_through_the_child_environment(self):
        fake = FakeBws()
        self.assertEqual(read(fake), {key: f"value-of-{key}" for key in KEYS})
        self.assertEqual([c[1:4] for c, _ in fake.calls], [["project", "list", "--output"], ["secret", "list", "p1"]])
        for command, options in fake.calls:
            self.assertNotIn(TOKEN, " ".join(command))
            self.assertIn(bitwarden.SERVER_URL, command)
            self.assertEqual(options["env"]["BWS_ACCESS_TOKEN"], TOKEN)
        self.assertNotEqual(os.environ.get("BWS_ACCESS_TOKEN"), TOKEN)

    def test_failures_are_explained_without_sensitive_data(self):
        faults = [
            (FakeBws(returncode=1), "exit code 1"),
            (FakeBws(projects=[]), "exactly one Bitwarden project"),
            (FakeBws(entries=secrets(MS_STORE_CLIENT_SECRET=None)), "MS_STORE_CLIENT_SECRET"),
            (FakeBws(entries=secrets(MS_STORE_CLIENT_ID=" ")), "MS_STORE_CLIENT_ID"),
            (FakeBws(entries=secrets() + secrets()), "more than once"),
            (FakeBws(entries={"value": TOKEN}), "unexpected output"),
        ]
        for fake, message in faults:
            with self.subTest(message), self.assertRaisesRegex(bitwarden.SecretsError, message) as caught:
                read(fake)
            self.assertNotIn(TOKEN, str(caught.exception))
            self.assertNotIn("value-of-", str(caught.exception))

        def hang(command, **options):
            raise subprocess.TimeoutExpired(command, 60, output=TOKEN)

        with self.assertRaisesRegex(bitwarden.SecretsError, "did not answer"):
            read(hang)

    def test_bws_is_found_on_path_or_in_the_local_program_folder(self):
        with tempfile.TemporaryDirectory() as temp:
            fallback = Path(temp) / "Programs" / "Bitwarden" / "bws.exe"
            environ = {"LOCALAPPDATA": temp}
            self.assertEqual(bitwarden.find_bws(environ, lambda _: "on-path"), "on-path")
            with self.assertRaisesRegex(bitwarden.SecretsError, "was not found"):
                bitwarden.find_bws(environ, lambda _: None)
            fallback.parent.mkdir(parents=True)
            fallback.write_bytes(b"")
            self.assertEqual(bitwarden.find_bws(environ, lambda _: None), str(fallback))

    def test_complete_environment_variables_take_precedence_over_bitwarden(self):
        environ = {key: f"id-{index}" for index, key in enumerate(KEYS)}
        self.assertEqual(store.load_credentials(environ, read_secrets=self.fail), ("id-0", "id-1", "id-2"))
        requested = []

        def from_bitwarden(project, keys):
            requested.append((project, keys))
            return {key: f"bw-{index}" for index, key in enumerate(keys)}

        del environ["MS_STORE_CLIENT_SECRET"]
        self.assertEqual(store.load_credentials(environ, from_bitwarden), ("bw-0", "bw-1", "bw-2"))
        self.assertEqual(requested, [("SpanDraft", KEYS)])
        with self.assertRaisesRegex(ValueError, "MS_STORE_TENANT_ID is not a valid ID"):
            store.load_credentials({}, lambda *_: {key: "a/b?c" for key in KEYS})

    @unittest.skipUnless(os.name == "nt", "Windows Credential Manager")
    def test_reads_a_generic_windows_credential(self):
        target = f"SpanDraft-test-{uuid.uuid4()}"
        with self.assertRaisesRegex(bitwarden.SecretsError, "no generic credential"):
            bitwarden.read_windows_credential(target)
        subprocess.run(["cmdkey", f"/generic:{target}", "/user:test", "/pass:not-a-secret"], check=True, capture_output=True)
        self.addCleanup(subprocess.run, ["cmdkey", f"/delete:{target}"], capture_output=True)
        self.assertEqual(bitwarden.read_windows_credential(target), "not-a-secret")


if __name__ == "__main__":
    unittest.main()
