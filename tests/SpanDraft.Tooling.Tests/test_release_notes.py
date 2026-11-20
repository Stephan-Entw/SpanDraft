import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts import release_notes


VERSION = "1.2.0"
PATHS = ["Directory.Build.props", "release-notes/en.md", "release-notes/de.md"]
ENGLISH = (
    "# Release notes\r\n\r\n## Unreleased\r\nFuture\r\n\r\n"
    "## 1.2.0\r\n\r\n### New\r\n\r\n- English release only\r\n\r\n"
    "## 1.1.0\r\nOlder\r\n"
)
GERMAN = "# Versionshinweise\n\n## 1.2.0\n\n- Deutsche Version\n"
SCRIPT = Path(release_notes.__file__).resolve()


class ReleaseNotesTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.english = self.root / PATHS[1]
        self.german = self.root / PATHS[2]
        self.english.parent.mkdir()
        self.english.write_bytes(ENGLISH.encode("utf-8"))
        self.german.write_text(GERMAN, encoding="utf-8")
        for name, path in [("ENGLISH_NOTES", self.english), ("GERMAN_NOTES", self.german)]:
            override = patch.object(release_notes, name, path)
            override.start()
            self.addCleanup(override.stop)

    def test_preparation_requires_exactly_both_notes_and_the_version_file(self):
        release_notes.validate_preparation(VERSION, reversed(PATHS))
        for wrong in [PATHS[:2], PATHS[1:], PATHS[::2], PATHS + ["README.md"]]:
            with self.subTest(paths=wrong), self.assertRaisesRegex(ValueError, "change exactly"):
                release_notes.validate_preparation(VERSION, wrong)

    def test_preparation_rejects_invalid_sections_in_either_language(self):
        for path, original in [(self.english, ENGLISH), (self.german, GERMAN)]:
            for invalid, message in [
                ("## 1.2.0-rc.1\nDraft\n", "exactly one"),
                ("## 1.2.0\nFirst\n\n## 1.2.0\nSecond\n", "exactly one"),
                ("## 1.2.0\n \t\n## 1.1.0\nOlder\n", "non-empty"),
            ]:
                with self.subTest(path=path, invalid=invalid):
                    path.write_text(invalid, encoding="utf-8")
                    with self.assertRaisesRegex(ValueError, f"{path.name}.*{message}"):
                        release_notes.validate_preparation(VERSION, PATHS)
            path.write_bytes(original.encode("utf-8"))

    def test_publish_exports_only_the_matching_english_body(self):
        self.german.unlink()
        output = self.root / "publish.md"
        subprocess.run([sys.executable, str(SCRIPT), "export-en", VERSION, str(output)], cwd=self.root, check=True)
        self.assertEqual(output.read_bytes(), b"\r\n### New\r\n\r\n- English release only\r\n\r\n")


if __name__ == "__main__":
    unittest.main()
