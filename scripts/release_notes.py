"""Validate release-preparation paths and read the notes for a single release."""

import argparse
import re
import subprocess
from pathlib import Path


ENGLISH_NOTES = Path("release-notes/en.md")
GERMAN_NOTES = Path("release-notes/de.md")
PREPARATION_FILES = {"Directory.Build.props", ENGLISH_NOTES.as_posix(), GERMAN_NOTES.as_posix()}
# Microsoft Store "What's new in this version": plain text, at most 1500 characters per language.
STORE_LIMIT = 1500
STORE_CULTURES = {"en-US": lambda: ENGLISH_NOTES, "de-DE": lambda: GERMAN_NOTES}


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


def _plain(text):
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)
    return text.replace("**", "").replace("`", "").strip()


def _blocks(notes):
    """Split a release section into ordered blocks: ("heading", text), ("item", title, text), ("paragraph", text)."""
    blocks, previous_blank = [], True
    for line in notes.replace("\r\n", "\n").split("\n"):
        stripped = line.strip()
        if not stripped:
            previous_blank = True
            continue
        if stripped.startswith("### "):
            blocks.append(("heading", stripped[4:].strip()))
        elif stripped.startswith("- "):
            match = re.match(r"- \*\*(.+?)\*\*\s*(.*)", stripped)
            blocks.append(("item", match.group(1), match.group(2)) if match else ("item", "", stripped[2:]))
        elif not previous_blank and blocks and blocks[-1][0] in ("item", "paragraph"):
            *head, text = blocks[-1]
            blocks[-1] = (*head, f"{text} {stripped}".strip())
        else:
            blocks.append(("paragraph", stripped))
        previous_blank = False
    return blocks


def _render(blocks, full):
    parts = []
    for block in blocks:
        if block[0] == "item":
            _, title, text = block
            line = "• " + _plain(" ".join(x for x in (title, text if full or not title else "") if x))
            if parts and parts[-1][0] == "list":
                parts[-1][1].append(line)
            else:
                parts.append(("list", [line]))
        elif block[0] == "heading":
            parts.append(("heading", _plain(block[1])))
        else:
            parts.append(("paragraph", _plain(block[1])))
    text, previous = "", None
    for kind, value in parts:
        value = "\n".join(value) if kind == "list" else value
        separator = "\n" if previous == "heading" and kind == "list" else "\n\n"
        text = value if not text else text + separator + value
        previous = kind
    return text


def store_text(path, version):
    """Plain-text Store notes: the full section if it fits, otherwise the same text with bullets reduced to their bold titles."""
    blocks = _blocks(release_section(path, version))
    full = _render(blocks, full=True)
    if len(full) <= STORE_LIMIT:
        return full
    if any(block[0] == "item" and not block[1] for block in blocks):
        raise ValueError(f"{path} {version} exceeds {STORE_LIMIT} characters and has bullets without a bold title.")
    compact = _render(blocks, full=False)
    if len(compact) > STORE_LIMIT:
        raise ValueError(f"{path} {version} exceeds {STORE_LIMIT} characters even as titles only ({len(compact)}).")
    return compact


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
    store = commands.add_parser("export-store", help="write <culture>.txt plain-text Store notes per language")
    store.add_argument("version")
    store.add_argument("output", type=Path)
    args = parser.parse_args()

    try:
        if args.command == "validate":
            changed = subprocess.check_output(
                ["git", "diff", "--no-renames", "--name-only", "HEAD^", "HEAD"], text=True
            )
            validate_preparation(args.version, changed.splitlines())
        elif args.command == "export-store":
            texts = {culture: store_text(path(), args.version) for culture, path in STORE_CULTURES.items()}
            args.output.mkdir(parents=True, exist_ok=True)
            for culture, text in texts.items():
                (args.output / f"{culture}.txt").write_bytes((text + "\n").encode("utf-8"))
        else:
            args.output.write_bytes(release_section(ENGLISH_NOTES, args.version).encode("utf-8"))
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error)) from None


if __name__ == "__main__":
    main()
