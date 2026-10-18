# Releasing SpanDraft

Windows x64 packaging is available locally and through a manual GitHub Actions
workflow. Tag-triggered release automation is the next release step and is not
implemented yet.

## Version and release notes

The central product version is `<Version>` in `Directory.Build.props`.
`RELEASE_NOTES.md` contains user-facing notes with the newest version first,
using headings such as `## 0.1.0`. Do not add a release date before publication.
Versions use `MAJOR.MINOR.PATCH`; the planned release tags use the `v` prefix.

## Release-preparation commit

The final commit before a release tag must have this exact subject, with the
release version substituted:

```text
chore: prepare release vX.Y.Z
```

It must change exactly these two files:

- `Directory.Build.props`, setting the central `<Version>` to `X.Y.Z`.
- `RELEASE_NOTES.md`, containing the matching `## X.Y.Z` section.

Bug fixes, product or UI changes, workflow changes, packaging infrastructure,
general documentation and dependency changes belong in earlier commits. If
Desktop dependencies changed, regenerate and commit `THIRD_PARTY_NOTICES.txt`
before release preparation using the command in the README.

## Prepare and test a Windows package

1. Finish normal feature and fix commits and pass all CI gates.
2. Commit only the central version and release notes to `main` as
   `chore: prepare release vX.Y.Z`.
3. Pass all CI gates again on that release-preparation commit.
4. Run the manual **Package Windows** workflow (`package-windows.yml`) for that
   exact commit and download its installer artifact. The workflow checks the
   commit subject, exact changed-file set, effective MSBuild version and matching
   release-notes heading before building. It checks out the exact run SHA and
   does not replace the product and validation gates in `ci.yml`.
5. Smoke-test the downloaded installer on Windows as described below.
6. Only after those checks pass, create the matching `vX.Y.Z` tag on exactly that
   tested release-preparation commit. A GitHub Release follows later. The package
   workflow itself creates neither tags nor GitHub Releases.

## Bugs found before publication

If a bug is found after release preparation but before tagging or publication:

1. Do not create the tag.
2. Fix the bug in a normal fix commit.
3. Rebase or move the still-unpublished release-preparation commit after the fix,
   preserving its two-file contract and keeping the same version. Do not add an
   empty release-preparation commit.
4. Repeat all CI gates, the package build and the installer smoke test on the new
   release-preparation SHA. Only then tag that commit.

For example, this order is wrong for the final release:

```text
chore: prepare release v0.1.0
fix: correct something
```

Before publication, rebuild it as:

```text
fix: correct something
chore: prepare release v0.1.0
v0.1.0
```

History rewrites are allowed only before a public tag or release. Never rewrite
published release history; a bug found afterward gets a new patch version, such
as `0.1.1`.

## Local Windows package

For a local package build, use Windows with PowerShell 7, .NET 10 SDK, Inno Setup 6
and an activated Python 3.11+ environment containing `help/requirements.txt` (see
[the help build instructions](../help/README.md)). Make `ISCC.exe` available on
`PATH` or use its standard `Program Files (x86)` / `Program Files` installation
directory. From the repository root, run:

```powershell
pwsh ./scripts/package-windows.ps1
```

The script reads the effective version through MSBuild, cleans its own publish
and installer output, checks third-party notices, builds and validates the offline
help, publishes the self-contained `win-x64` desktop application and invokes Inno
Setup. It does not install prerequisites. The result is
`artifacts/windows/installer/SpanDraft-<Version>-win-x64-setup.exe`.

## Manual Windows smoke test

- Use a fresh Windows user profile or a state without `%LOCALAPPDATA%\SpanDraft`.
  Install as a standard user without elevation. Verify the per-user installation
  directory, the SpanDraft Start menu entry and the application icon.
- Verify the optional desktop shortcut task is offered and unchecked by default.
  Select it and confirm the installed shortcut starts SpanDraft.
- Verify the completion page offers to launch SpanDraft, checked by default, and
  that finishing setup with this option selected starts the application.
- Create the first project successfully, check the application version and run a
  simple beam calculation. Also verify starting from the Start menu. Confirm it
  works without a separately installed .NET runtime.
- Verify `THIRD_PARTY_NOTICES.txt` and the complete `LICENSE` are installed beside
  the executable.
- Open the installed offline manual from the Help menu with networking disabled;
  check navigation, search and images.
- Reinstall the same package and verify it remains one installed application.
  Uninstall through Windows Settings and verify the application and Start menu
  entry are removed.

## Planned release automation

The next step is a workflow triggered by a matching `vMAJOR.MINOR.PATCH` tag on
the exact tested commit. It must run all CI gates, verify the tag against the
central version and release-notes heading, and explicitly check notices with
`dotnet run --file scripts/generate-third-party-notices.cs -- --check`.
Only after those checks pass should it use the shared packaging script and
publish a GitHub Release with the matching notes and installer. Pre-release
tags and code signing are not part of the current packaging process.
