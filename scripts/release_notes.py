"""Validate release-preparation paths and read the notes for a single release."""

import argparse
import subprocess
from pathlib import Path


ENGLISH_NOTES = Path("release-notes/en.md")
GERMAN_NOTES = Path("release-notes/de.md")
PREPARATION_FILES = {"Directory.Build.props", ENGLISH_NOTES.as_posix(), GERMAN_NOTES.as_posix()}


def release_section(path, version):
    with path.open(encoding="utf-8", newline="") as source:
        lines = source.readlines()
    headings = [index for index, line in enumerate(lines) if line.rstrip("\r\n") == f"## {version}"]
    if len(headings) != 1:
        raise ValueError(f"{path} must contain exactly one heading ## {version}.")
    start = headings[0] + 1
    end = next((index for index in range(start, len(lines)) if lines[index].startswith("## ")), len(lines))
    notes = "".join(lines[start:end])
    if not notes.strip():
        raise ValueError(f"{path} must contain non-empty notes for {version}.")
    return notes


def validate_preparation(version, changed_files):
    if set(changed_files) != PREPARATION_FILES:
        raise ValueError(f"Release preparation must change exactly: {', '.join(sorted(PREPARATION_FILES))}.")
    for path in (ENGLISH_NOTES, GERMAN_NOTES):
        release_section(path, version)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    validate = commands.add_parser("validate")
    validate.add_argument("version")
    export = commands.add_parser("export-en")
    export.add_argument("version")
    export.add_argument("output", type=Path)
    args = parser.parse_args()

    try:
        if args.command == "validate":
            changed = subprocess.check_output(
                ["git", "diff", "--no-renames", "--name-only", "HEAD^", "HEAD"], text=True
            )
            validate_preparation(args.version, changed.splitlines())
        else:
            args.output.write_bytes(release_section(ENGLISH_NOTES, args.version).encode("utf-8"))
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error)) from None


if __name__ == "__main__":
    main()
