"""Runs the macOS platform contract selection and checks its results.

The manifest is the authoritative test selection. A marker scan of the test
sources is a supplementary guard against platform contracts that were added
without a manifest decision. See docs/TESTING.md, "Platform CI".

Usage (after a current Release build of SpanDraft.sln):
    python scripts/platform_contracts.py run --manifest FILE --results DIR
"""

from __future__ import annotations

import argparse
import ctypes
import ctypes.util
import json
import os
import platform
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

FRAMEWORK = "net10.0"
MODULES = ("SpanDraft.Tests", "SpanDraft.Desktop.Tests", "SpanDraft.Rendering.Tests", "SpanDraft.Tooling.Tests")
SUPPORT = "Support"
MARKERS = re.compile(
    r"OperatingSystem\.Is|RuntimeInformation|UnixFileMode|NamedPipe|ProjectIdentityResolver|"
    r"FileRecoveryStorage|BrokerServer|BrokerClient|DataScope|MacActivationSource|InstanceCoordinator|"
    r"ApplicationCloseCoordinator|Process\.Start|ProcessStartInfo|Assert\.Skip|SkipUnless|SkipWhen"
)
EXIT_FAILED, EXIT_BLOCKED = 1, 3


class Manifest:
    def __init__(self):
        self.run = {}  # fqn -> module, in manifest order
        self.exclude = {}  # fqn -> (module, reason)
        self.support = {}  # path -> (covering run classes, reason)

    def module_classes(self, module):
        return [fqn for fqn, owner in self.run.items() if owner == module]

    def decided(self, fqn):
        if fqn in self.run:
            return self.run[fqn]
        if fqn in self.exclude:
            return self.exclude[fqn][0]
        return None


def parse_manifest(text):
    """Returns (manifest, errors)."""
    manifest, errors = Manifest(), []
    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        fields = line.split()
        kind = fields[0]
        where = f"manifest line {number}"
        if kind in ("run", "exclude"):
            minimum = 3 if kind == "run" else 4
            if len(fields) < minimum:
                errors.append(f"{where}: '{kind}' needs {'a module and a class' if kind == 'run' else 'a module, a class and a reason'}")
                continue
            if kind == "run" and len(fields) > 3:
                errors.append(f"{where}: 'run' takes only a module and a class")
                continue
            module, fqn = fields[1], fields[2]
            if module not in MODULES:
                errors.append(f"{where}: unknown module '{module}'")
                continue
            if manifest.decided(fqn) is not None:
                errors.append(f"{where}: duplicate entry for {fqn}")
                continue
            if kind == "run":
                manifest.run[fqn] = module
            else:
                manifest.exclude[fqn] = (module, " ".join(fields[3:]))
        elif kind == "support":
            if len(fields) < 4:
                errors.append(f"{where}: 'support' needs a path, covering run classes and a reason")
                continue
            path = fields[1]
            if path in manifest.support:
                errors.append(f"{where}: duplicate entry for {path}")
                continue
            manifest.support[path] = ([name for name in fields[2].split(",") if name], " ".join(fields[3:]))
        else:
            errors.append(f"{where}: unknown entry type '{kind}'")
    for path, (classes, _) in manifest.support.items():
        for fqn in classes:
            if fqn not in manifest.run:
                errors.append(f"support entry {path}: covering class {fqn} is not a 'run' entry")
    if not manifest.run:
        errors.append("manifest selects no 'run' classes")
    return manifest, errors


def parse_inventory(module, returncode, stdout):
    """Returns (classes, errors) from `-list classes/json` output."""
    if returncode != 0:
        return set(), [f"{module}: class discovery exited with {returncode}"]
    line = next((item for item in stdout.splitlines() if item.lstrip().startswith("[")), None)
    try:
        classes = json.loads(line) if line is not None else None
    except ValueError:
        classes = None
    if not isinstance(classes, list) or not all(isinstance(item, str) for item in classes):
        return set(), [f"{module}: class discovery returned no valid JSON class list"]
    if not classes:
        return set(), [f"{module}: class discovery found no test classes"]
    return set(classes), []


def collect_sources(tests_root):
    """Returns {repository-relative path: text} for the scanned C# sources."""
    sources = {}
    for name in MODULES + (SUPPORT,):
        directory = Path(tests_root) / name
        for path in sorted(directory.rglob("*.cs")):
            parts = path.relative_to(directory).parts
            if any(part.lower() in ("bin", "obj") for part in parts):
                continue
            relative = Path("tests", name, *parts).as_posix()
            sources[relative] = path.read_text(encoding="utf-8-sig")
    return sources


def _simple_name(fqn):
    return re.split(r"[.+]", fqn)[-1]


def validate_selection(manifest, inventory, sources):
    """Checks the manifest against discovered classes and marker-bearing sources."""
    errors = []
    for fqn, module in manifest.run.items():
        if fqn not in inventory.get(module, ()):
            errors.append(f"run class {fqn} was not discovered in {module}")
    for fqn, (module, _) in manifest.exclude.items():
        if fqn not in inventory.get(module, ()):
            errors.append(f"excluded class {fqn} was not discovered in {module}")
    for path in manifest.support:
        if path not in sources:
            errors.append(f"support entry {path} does not name a scanned test source")
    for path, text in sources.items():
        if not MARKERS.search(text):
            continue
        module = path.split("/")[1]
        declared = []
        if module != SUPPORT:
            declared = sorted(
                fqn for fqn in inventory.get(module, ())
                if re.search(r"\bclass\s+" + re.escape(_simple_name(fqn)) + r"\b", text)
            )
        if not declared:
            if path not in manifest.support:
                errors.append(f"{path} contains platform markers but has no 'support' entry")
            continue
        for fqn in declared:
            owner = manifest.decided(fqn)
            if owner is None:
                errors.append(f"{path} contains platform markers; {fqn} needs a 'run' or 'exclude' entry")
            elif owner != module:
                errors.append(f"{fqn} is listed for {owner} but is declared in {module}")
    return errors


def preflight_errors(system, euid, temp_path, fstype):
    """Checks the conditions under which the selected contracts are meaningful."""
    errors = []
    if system != "Darwin":
        errors.append(f"macOS is required, found {system}")
        return errors
    if euid == 0:
        errors.append("the contracts must not run as root; Unix permission tests would be bypassed")
    if fstype != "apfs":
        errors.append(f"the .NET temporary directory {temp_path} must be on APFS, found {fstype or 'unknown'}")
    return errors


def dotnet_temp_path(environ):
    """Mirrors Path.GetTempPath on Unix: TMPDIR if set, otherwise /tmp/."""
    return environ.get("TMPDIR") or "/tmp/"


class _StatFs(ctypes.Structure):
    # struct statfs with 64-bit inodes, as used by .NET DriveInfo.DriveFormat on macOS.
    _fields_ = [
        ("f_bsize", ctypes.c_uint32), ("f_iosize", ctypes.c_int32),
        ("f_blocks", ctypes.c_uint64), ("f_bfree", ctypes.c_uint64), ("f_bavail", ctypes.c_uint64),
        ("f_files", ctypes.c_uint64), ("f_ffree", ctypes.c_uint64), ("f_fsid", ctypes.c_int32 * 2),
        ("f_owner", ctypes.c_uint32), ("f_type", ctypes.c_uint32), ("f_flags", ctypes.c_uint32),
        ("f_fssubtype", ctypes.c_uint32), ("f_fstypename", ctypes.c_char * 16),
        ("f_mntonname", ctypes.c_char * 1024), ("f_mntfromname", ctypes.c_char * 1024),
        ("f_flags_ext", ctypes.c_uint32), ("f_reserved", ctypes.c_uint32 * 7),
    ]


def filesystem_type(path):
    """Returns statfs f_fstypename, the value .NET reports as DriveInfo.DriveFormat."""
    libc = ctypes.CDLL(ctypes.util.find_library("c"), use_errno=True)
    try:
        statfs = libc["statfs$INODE64"]  # x86_64; arm64 has only the 64-bit variant.
    except AttributeError:
        statfs = libc["statfs"]
    statfs.argtypes = [ctypes.c_char_p, ctypes.POINTER(_StatFs)]
    result = _StatFs()
    if statfs(os.fsencode(path), ctypes.byref(result)) != 0:
        return None
    return result.f_fstypename.decode()


def _assembly_module(name):
    return re.split(r"[\\/]", name or "")[-1].rsplit(".", 1)[0]


def check_results(module, xml_path, expected, returncode):
    """Checks one runner result file. Returns (passed tests per class, errors)."""
    counts, errors = {fqn: 0 for fqn in expected}, []
    if returncode != 0:
        errors.append(f"{module}: test runner exited with {returncode}")
    path = Path(xml_path)
    if not path.is_file():
        return counts, errors + [f"{module}: result file {path} is missing"]
    if path.stat().st_size == 0:
        return counts, errors + [f"{module}: result file {path} is empty"]
    try:
        root = ET.parse(str(path)).getroot()
    except ET.ParseError as error:
        return counts, errors + [f"{module}: result file {path} is not valid XML ({error})"]
    assemblies = root.findall("assembly") if root.tag == "assemblies" else []
    if len(assemblies) != 1:
        return counts, errors + [f"{module}: expected one <assembly> result, found {len(assemblies)}"]
    assembly = assemblies[0]
    if _assembly_module(assembly.get("name")) != module:
        errors.append(f"{module}: result belongs to {assembly.get('name')}")
    if root.findall(".//errors/error"):
        errors.append(f"{module}: the runner reported {len(root.findall('.//errors/error'))} execution error(s)")
    attributes = {}
    for name in ("total", "passed", "failed", "skipped", "errors", "not-run"):
        value = assembly.get(name)
        if value is None or not value.isdigit():
            errors.append(f"{module}: result attribute '{name}' is missing or invalid")
        else:
            attributes[name] = int(value)
    for name in ("failed", "skipped", "errors", "not-run"):
        if attributes.get(name):
            errors.append(f"{module}: {attributes[name]} test(s) reported as {name}")
    tests = assembly.findall(".//test")
    total, passed = attributes.get("total"), attributes.get("passed")
    if not tests or not total:
        errors.append(f"{module}: the result contains no tests")
    elif not (total == passed == len(tests)):
        errors.append(f"{module}: total {total}, passed {passed} and {len(tests)} test elements disagree")
    for test in tests:
        fqn, result = test.get("type"), test.get("result")
        if fqn not in counts:
            errors.append(f"{module}: unexpected test class {fqn}")
            continue
        if result != "Pass":
            errors.append(f"{module}: {test.get('name')} reported {result}")
            continue
        counts[fqn] += 1
    for fqn, count in counts.items():
        if count == 0:
            errors.append(f"{module}: {fqn} has no passed test")
    return counts, errors


def _test_environment():
    environment = dict(os.environ)
    environment.update(LANG="en_US.UTF-8", LC_ALL="en_US.UTF-8", DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="0",
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", AVALONIA_TELEMETRY_OPTOUT="1")
    return environment


def _dll(root, module):
    return Path(root) / "tests" / module / "bin" / "Release" / FRAMEWORK / f"{module}.dll"


def _report(status, errors):
    for error in errors:
        print(f"  {error}")
    print(f"PLATFORM CONTRACTS {status}")


def run(manifest_path, results, root):
    manifest, errors = parse_manifest(Path(manifest_path).read_text(encoding="utf-8"))
    if errors:
        _report("FAILED", errors)
        return EXIT_FAILED

    temp_path = dotnet_temp_path(os.environ)
    system = platform.system()
    errors = preflight_errors(
        system, os.geteuid() if hasattr(os, "geteuid") else None, temp_path,
        filesystem_type(temp_path) if system == "Darwin" else None)
    if errors:
        _report("BLOCKED", errors)
        return EXIT_BLOCKED

    environment = _test_environment()
    inventory = {}
    for module in MODULES:
        dll = _dll(root, module)
        if not dll.is_file():
            errors.append(f"{module}: Release assembly {dll} is missing; build SpanDraft.sln in Release first")
            continue
        discovery = subprocess.run(["dotnet", str(dll), "-list", "classes/json", "-noLogo", "-noColor"],
                                   cwd=root, env=environment, capture_output=True, text=True)
        inventory[module], discovery_errors = parse_inventory(module, discovery.returncode, discovery.stdout)
        errors += discovery_errors
    if not errors:
        errors = validate_selection(manifest, inventory, collect_sources(Path(root) / "tests"))
    if errors:
        _report("FAILED", errors)
        return EXIT_FAILED

    Path(results).mkdir(parents=True, exist_ok=True)
    run_directory = Path(tempfile.mkdtemp(prefix="run-", dir=str(results)))
    print(f"Results: {run_directory}")
    summary = []
    for module in MODULES:
        classes = manifest.module_classes(module)
        if not classes:
            continue
        xml_path, log_path = run_directory / f"{module}.xml", run_directory / f"{module}.log"
        command = ["dotnet", str(_dll(root, module)), "-noColor", "-noAutoReporters", "-culture", "en-US", "-failSkips"]
        for fqn in classes:
            command += ["-class", fqn]
        command += ["-result-xml", str(xml_path)]
        print(f"Running {module} ({len(classes)} classes)", flush=True)
        with open(log_path, "w", encoding="utf-8") as log:
            returncode = subprocess.run(command, cwd=root, env=environment, stdout=log,
                                        stderr=subprocess.STDOUT).returncode
        counts, module_errors = check_results(module, xml_path, classes, returncode)
        errors += module_errors
        summary += [(module, fqn, counts[fqn]) for fqn in classes]
    for module, fqn, count in summary:
        print(f"  {'PASS' if count else 'FAIL'}  {count:4d}  {module}  {fqn}")
    if errors:
        _report("FAILED", errors)
        return EXIT_FAILED
    _report("PASSED", [])
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description="Run and check the macOS platform contract selection.")
    commands = parser.add_subparsers(dest="command", required=True)
    run_parser = commands.add_parser("run")
    run_parser.add_argument("--manifest", required=True)
    run_parser.add_argument("--results", required=True)
    arguments = parser.parse_args(argv)
    root = Path(__file__).resolve().parent.parent
    return run(arguments.manifest, arguments.results, root)


if __name__ == "__main__":
    sys.exit(main())
