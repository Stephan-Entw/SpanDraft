import json
import tempfile
import unittest
from pathlib import Path

from scripts import platform_contracts as contracts

MODULE = "SpanDraft.Tests"
IDENTITY = "SpanDraft.Tests.ProjectIdentityTests"
RECOVERY = "SpanDraft.Tests.RecoveryCatalogTests"

VALID_MANIFEST = f"""
# comment
run {MODULE} {IDENTITY}
run {MODULE} {RECOVERY}  # trailing comment
exclude SpanDraft.Tooling.Tests SpanDraft.Tests.ScreenshotTests Windows-only release tooling
support tests/Support/ProjectTestSupport.cs {IDENTITY},{RECOVERY} Creates real project files
"""


def manifest(text=VALID_MANIFEST):
    parsed, errors = contracts.parse_manifest(text)
    if errors:
        raise AssertionError(errors)
    return parsed


def result_xml(tests, **attributes):
    """tests: list of (class, result). Attributes default to a consistent passing run."""
    passed = sum(result == "Pass" for _, result in tests)
    values = {"total": len(tests), "passed": passed, "failed": 0, "skipped": 0, "errors": 0, "not-run": 0}
    values.update(attributes)
    attribute_text = " ".join(f'{key}="{value}"' for key, value in values.items() if value is not None)
    elements = "".join(
        f'<test name="{fqn}.Case{index}" type="{fqn}" method="Case{index}" result="{result}" />'
        for index, (fqn, result) in enumerate(tests))
    return (f'<assemblies schema-version="3"><assembly name="/repo/tests/{MODULE}/bin/Release/net10.0/{MODULE}.dll" '
            f'{attribute_text}><collection>{elements}</collection></assembly></assemblies>')


class ManifestTests(unittest.TestCase):
    def test_valid_manifest_is_parsed(self):
        parsed = manifest()
        self.assertEqual([IDENTITY, RECOVERY], parsed.module_classes(MODULE))
        self.assertIn("SpanDraft.Tests.ScreenshotTests", parsed.exclude)
        self.assertIn("tests/Support/ProjectTestSupport.cs", parsed.support)

    def assert_rejected(self, text, fragment):
        _, errors = contracts.parse_manifest(text)
        self.assertTrue(any(fragment in error for error in errors), errors)

    def test_duplicate_class_is_rejected(self):
        self.assert_rejected(f"run {MODULE} {IDENTITY}\nrun {MODULE} {IDENTITY}", "duplicate entry")

    def test_class_both_run_and_excluded_is_rejected(self):
        self.assert_rejected(f"run {MODULE} {IDENTITY}\nexclude {MODULE} {IDENTITY} reason", "duplicate entry")

    def test_duplicate_support_path_is_rejected(self):
        self.assert_rejected(
            f"run {MODULE} {IDENTITY}\nsupport a.cs {IDENTITY} one\nsupport a.cs {IDENTITY} two", "duplicate entry")

    def test_exclusion_without_reason_is_rejected(self):
        self.assert_rejected(f"run {MODULE} {IDENTITY}\nexclude {MODULE} {RECOVERY}", "needs a module, a class and a reason")

    def test_support_without_reason_is_rejected(self):
        self.assert_rejected(f"run {MODULE} {IDENTITY}\nsupport a.cs {IDENTITY}", "needs a path")

    def test_support_must_be_covered_by_a_run_class(self):
        self.assert_rejected(f"run {MODULE} {IDENTITY}\nsupport a.cs {RECOVERY} reason", "is not a 'run' entry")

    def test_unknown_entry_and_module_are_rejected(self):
        self.assert_rejected(f"run {MODULE} {IDENTITY}\nskip {MODULE} {RECOVERY}", "unknown entry type")
        self.assert_rejected(f"run Other.Tests {IDENTITY}", "unknown module")

    def test_run_with_extra_fields_is_rejected(self):
        self.assert_rejected(f"run {MODULE} {IDENTITY} because", "only a module and a class")

    def test_empty_selection_is_rejected(self):
        self.assert_rejected("# nothing\nexclude SpanDraft.Tests X reason", "selects no 'run' classes")


class InventoryTests(unittest.TestCase):
    def test_discovered_classes_are_returned(self):
        classes, errors = contracts.parse_inventory(MODULE, 0, json.dumps([IDENTITY, RECOVERY]) + "\n")
        self.assertEqual([], errors)
        self.assertEqual({IDENTITY, RECOVERY}, classes)

    def test_failed_discovery_is_rejected_even_with_output(self):
        _, errors = contracts.parse_inventory(MODULE, 1, json.dumps([IDENTITY]))
        self.assertIn("class discovery exited with 1", errors[0])

    def test_invalid_or_empty_discovery_is_rejected(self):
        for output in ("", "not json", "[1, 2]", "[broken"):
            _, errors = contracts.parse_inventory(MODULE, 0, output)
            self.assertIn("no valid JSON class list", errors[0], output)
        _, errors = contracts.parse_inventory(MODULE, 0, "[]")
        self.assertIn("found no test classes", errors[0])


class SelectionTests(unittest.TestCase):
    inventory = {
        MODULE: {IDENTITY, RECOVERY, "SpanDraft.Tests.Unrelated"},
        "SpanDraft.Tooling.Tests": {"SpanDraft.Tests.ScreenshotTests", "SpanDraft.Tests.StartupTests"},
    }
    sources = {
        f"tests/{MODULE}/ProjectIdentityTests.cs": "public sealed class ProjectIdentityTests { OperatingSystem.IsMacOS(); }",
        f"tests/{MODULE}/RecoveryCatalogTests.cs": "public sealed class RecoveryCatalogTests { UnixFileMode m; }",
        f"tests/{MODULE}/Unrelated.cs": "public sealed class Unrelated { }",
        "tests/SpanDraft.Tooling.Tests/ScreenshotTests.cs": "public sealed class ScreenshotTests { SkipWhen = x; }",
        "tests/Support/ProjectTestSupport.cs": "static class ProjectTestSupport { ProjectIdentityResolver r; }",
    }

    def test_complete_selection_passes(self):
        self.assertEqual([], contracts.validate_selection(manifest(), self.inventory, self.sources))

    def test_run_class_missing_from_inventory_is_rejected(self):
        inventory = dict(self.inventory, **{MODULE: {IDENTITY}})
        errors = contracts.validate_selection(manifest(), inventory, self.sources)
        self.assertIn(f"run class {RECOVERY} was not discovered in {MODULE}", errors)

    def test_stale_exclusion_is_rejected(self):
        inventory = dict(self.inventory, **{"SpanDraft.Tooling.Tests": set()})
        errors = contracts.validate_selection(manifest(), inventory, self.sources)
        self.assertTrue(any("excluded class SpanDraft.Tests.ScreenshotTests" in error for error in errors), errors)

    def test_marker_class_without_decision_is_rejected(self):
        text = VALID_MANIFEST.replace(f"run {MODULE} {RECOVERY}  # trailing comment\n", "").replace(f",{RECOVERY}", "")
        errors = contracts.validate_selection(manifest(text), self.inventory, self.sources)
        self.assertTrue(any(f"{RECOVERY} needs a 'run' or 'exclude' entry" in error for error in errors), errors)

    def test_every_class_of_a_marker_file_needs_a_decision(self):
        sources = dict(self.sources)
        sources["tests/SpanDraft.Tooling.Tests/ScreenshotTests.cs"] += "\npublic sealed class StartupTests { }"
        errors = contracts.validate_selection(manifest(), self.inventory, sources)
        self.assertEqual(
            ["tests/SpanDraft.Tooling.Tests/ScreenshotTests.cs contains platform markers; "
             "SpanDraft.Tests.StartupTests needs a 'run' or 'exclude' entry"], errors)

    def test_class_names_are_matched_as_whole_words(self):
        sources = dict(self.sources)
        sources["tests/SpanDraft.Tooling.Tests/ScreenshotTests.cs"] += "\nclass StartupTestsHelper { }"
        self.assertEqual([], contracts.validate_selection(manifest(), self.inventory, sources))

    def test_marker_support_file_without_entry_is_rejected(self):
        sources = dict(self.sources)
        sources["tests/Support/DesktopControlEnvironment.cs"] = "class DesktopControlEnvironment { OperatingSystem.IsMacOS(); }"
        errors = contracts.validate_selection(manifest(), self.inventory, sources)
        self.assertEqual(["tests/Support/DesktopControlEnvironment.cs contains platform markers but has no 'support' entry"], errors)

    def test_marker_file_without_test_class_needs_support_entry(self):
        sources = dict(self.sources)
        sources[f"tests/{MODULE}/Helpers.cs"] = "static class Helpers { Process.Start(x); }"
        errors = contracts.validate_selection(manifest(), self.inventory, sources)
        self.assertEqual([f"tests/{MODULE}/Helpers.cs contains platform markers but has no 'support' entry"], errors)

    def test_support_entry_for_missing_source_is_rejected(self):
        sources = {path: text for path, text in self.sources.items() if "Support" not in path}
        errors = contracts.validate_selection(manifest(), self.inventory, sources)
        self.assertIn("support entry tests/Support/ProjectTestSupport.cs does not name a scanned test source", errors)

    def test_class_listed_for_the_wrong_module_is_rejected(self):
        text = VALID_MANIFEST.replace("exclude SpanDraft.Tooling.Tests", f"exclude {MODULE}")
        inventory = dict(self.inventory, **{MODULE: self.inventory[MODULE] | {"SpanDraft.Tests.ScreenshotTests"}})
        errors = contracts.validate_selection(manifest(text), inventory, self.sources)
        self.assertIn(f"SpanDraft.Tests.ScreenshotTests is listed for {MODULE} but is declared in SpanDraft.Tooling.Tests",
                      errors)

    def test_sources_are_collected_without_build_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in (f"{MODULE}/A.cs", f"{MODULE}/bin/Release/B.cs", f"{MODULE}/obj/C.cs", "Support/D.cs", "Other/E.cs"):
                (root / relative).parent.mkdir(parents=True, exist_ok=True)
                (root / relative).write_text("class X { }", encoding="utf-8")
            self.assertEqual([f"tests/{MODULE}/A.cs", "tests/Support/D.cs"], sorted(contracts.collect_sources(root)))


class PreflightTests(unittest.TestCase):
    def test_suitable_host_passes(self):
        self.assertEqual([], contracts.preflight_errors("Darwin", 501, "/tmp/", "apfs"))

    def test_other_operating_system_is_rejected(self):
        self.assertIn("macOS is required", contracts.preflight_errors("Windows", None, "C:/Temp", None)[0])

    def test_root_is_rejected(self):
        self.assertIn("must not run as root", contracts.preflight_errors("Darwin", 0, "/tmp/", "apfs")[0])

    def test_non_apfs_temporary_directory_is_rejected(self):
        for fstype in ("hfs", None):
            self.assertIn("must be on APFS", contracts.preflight_errors("Darwin", 501, "/tmp/", fstype)[0])

    def test_temporary_path_matches_dotnet(self):
        self.assertEqual("/custom/", contracts.dotnet_temp_path({"TMPDIR": "/custom/"}))
        self.assertEqual("/tmp/", contracts.dotnet_temp_path({"TMPDIR": ""}))
        self.assertEqual("/tmp/", contracts.dotnet_temp_path({}))


class ResultTests(unittest.TestCase):
    expected = [IDENTITY, RECOVERY]

    def check(self, content, returncode=0, write=True):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / f"{MODULE}.xml"
            if write:
                path.write_text(content, encoding="utf-8")
            return contracts.check_results(MODULE, path, self.expected, returncode)

    def assert_rejected(self, fragment, *args, **kwargs):
        _, errors = self.check(*args, **kwargs)
        self.assertTrue(any(fragment in error for error in errors), errors)

    def passing(self, **attributes):
        return result_xml([(IDENTITY, "Pass"), (IDENTITY, "Pass"), (RECOVERY, "Pass")], **attributes)

    def test_passing_result_is_accepted(self):
        counts, errors = self.check(self.passing())
        self.assertEqual([], errors)
        self.assertEqual({IDENTITY: 2, RECOVERY: 1}, counts)

    def test_missing_empty_and_invalid_files_are_rejected(self):
        self.assert_rejected("is missing", "", write=False)
        self.assert_rejected("is empty", "")
        self.assert_rejected("is not valid XML", "<assemblies><assembly>")

    def test_wrong_structure_is_rejected(self):
        self.assert_rejected("expected one <assembly>", "<assemblies />")
        self.assert_rejected("expected one <assembly>", "<other />")
        self.assert_rejected("result belongs to", self.passing().replace(f"/{MODULE}.dll", "/Other.Tests.dll"))

    def test_empty_run_is_rejected(self):
        self.assert_rejected("contains no tests", result_xml([]))

    def test_failed_skipped_and_not_run_tests_are_rejected(self):
        for result, attribute in (("Fail", "failed"), ("Skip", "skipped"), ("NotRun", "not-run")):
            content = result_xml([(IDENTITY, "Pass"), (RECOVERY, "Pass"), (RECOVERY, result)], **{attribute: 1})
            _, errors = self.check(content)
            self.assertTrue(any(f"reported as {attribute}" in error for error in errors), errors)
            self.assertTrue(any(f"reported {result}" in error for error in errors), errors)

    def test_non_pass_element_is_rejected_even_when_counters_are_clean(self):
        content = result_xml([(IDENTITY, "Pass"), (RECOVERY, "Pass"), (RECOVERY, "Skip")], passed=3)
        self.assert_rejected("reported Skip", content)

    def test_missing_or_invalid_counters_are_rejected(self):
        self.assert_rejected("'not-run' is missing or invalid", self.passing(**{"not-run": None}))
        self.assert_rejected("'total' is missing or invalid", self.passing(total="three"))

    def test_inconsistent_counts_are_rejected(self):
        self.assert_rejected("disagree", self.passing(total=4, passed=4))
        self.assert_rejected("disagree", self.passing(passed=2))

    def test_execution_errors_are_rejected(self):
        content = self.passing().replace(
            "</assembly>", '<errors><error type="assembly-cleanup" name="x"><failure /></error></errors></assembly>')
        self.assert_rejected("execution error", content)
        self.assert_rejected("reported as errors", self.passing(errors=1))

    def test_unexpected_class_is_rejected(self):
        content = result_xml([(IDENTITY, "Pass"), (RECOVERY, "Pass"), ("SpanDraft.Tests.Other", "Pass")])
        self.assert_rejected("unexpected test class SpanDraft.Tests.Other", content)

    def test_expected_class_without_passed_test_is_rejected(self):
        self.assert_rejected(f"{RECOVERY} has no passed test", result_xml([(IDENTITY, "Pass")]))

    def test_runner_failure_is_rejected_with_clean_result(self):
        self.assert_rejected("test runner exited with 1", self.passing(), returncode=1)


if __name__ == "__main__":
    unittest.main()
