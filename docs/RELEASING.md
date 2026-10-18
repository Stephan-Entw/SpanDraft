# Releasing SpanDraft

Future Windows x64 releases are automatic after a successful push CI run on
`main`. The published `v0.1.0` tag, release, notes and history remain unchanged.

## Version and release preparation

The central product version is `<Version>` in `Directory.Build.props`.
`RELEASE_NOTES.md` contains user-facing notes with the newest version first and
exact headings such as `## 0.1.1`. Versions use `MAJOR.MINOR.PATCH`, without leading
zeros or suffixes; tags use the `v` prefix. Do not add a release date before
publication.

The final commit on `main` must have this exact subject:

```text
chore: prepare release vX.Y.Z
```

It must change exactly these two files:

- `Directory.Build.props`, setting the effective Desktop version to `X.Y.Z`.
- `RELEASE_NOTES.md`, containing exactly one non-empty `## X.Y.Z` section.

Product changes, fixes, dependencies, packaging, workflows and general
documentation belong in earlier commits. If Desktop dependencies changed,
regenerate and commit `THIRD_PARTY_NOTICES.txt` before release preparation using
the command in the README.

**Pushing this final release-preparation commit to `main` is the deliberate
authorization to publish. A successful CI and installer smoke test will publish
automatically, without another approval.**

## Normal release

1. Finish features and fixes. Perform targeted manual product or installer checks
   during development when the change needs them.
2. Create the final two-file `chore: prepare release vX.Y.Z` commit and push `main`.
3. The existing CI runs Product, Validation and Help. Only a successful push run
   on `main` can trigger release processing; PRs and manually started CI cannot
   publish.
4. The Release workflow validates the subject, exact changed-file set, effective
   MSBuild version and matching non-empty notes section. Ordinary commits finish
   successfully with `is_release=false`; malformed release-preparation subjects
   fail.
5. On `windows-2025`, the existing packaging script builds the installer once.
   The automated smoke test installs that EXE, checks the installed contents and
   shortcuts, starts the application for five seconds, stops it and uninstalls it.
6. Only after that test passes, the EXE is hashed with SHA-256 and transferred as
   an internal Actions artifact. The publish job verifies its exact filename and
   hash before publishing that same EXE.
7. GitHub creates `vX.Y.Z` on the tested commit and publishes `SpanDraft vX.Y.Z`.
   Notes are only the contents under `## X.Y.Z` up to the next `## ` heading or
   EOF. The installer is the only custom asset; GitHub's source archives are normal.

The workflow checks that remote `main` still points to the candidate before
packaging and immediately before publication. Release concurrency cancels older
release runs when another run starts. A final check detects changes during
publication. These checks cannot atomically prevent a simultaneous push between
the last check and GitHub publication; they do not roll back a public release.

No manual package download, VM installation, tag creation, release creation,
asset upload or notes copying is required for a normal release. The workflow
defaults to `contents: read`; only the publish job receives `contents: write`.

## Failures and bugs before publication

A failure before `gh release create` creates neither a tag nor a release.
Inspect the failed Actions job before retrying. A tag left by a partial publish
may be reused only if it points to exactly the same release SHA. An existing
release or a tag on another commit blocks publication; automation never
overwrites or deletes either. A partial failure during publication may leave a
tag or draft release that needs investigation. Do not assume a failed run means
nothing became public.

If a product bug is found before publication:

1. Stop any still-running release attempt and confirm that no release was
   published. A later commit on `main` also makes the old candidate stale.
2. Revert the unpublished release-preparation commit with a normal commit.
3. Commit the fix normally, then create a new final release-preparation commit
   with the same two-file contract. The same version may be reused while it has
   never been publicly released.
4. Push the new final SHA; the complete CI, packaging and smoke-test path repeats.

Do not rewrite history or force-push. After public publication, every bug fix
requires a new patch version. Never change published tags, assets or release notes.

## Optional Windows package and manual checks

The manual **Package Windows** workflow remains available to build or inspect an
installer for an exact release-preparation commit. It validates that commit and
uploads an internal artifact; it creates no tag or GitHub Release and is not a
required step in the normal release path.

For a local build, use Windows with PowerShell 7, .NET 10 SDK, Inno Setup 6 and
Python 3.11+ with `help/requirements.txt` installed (see the
[help build instructions](../help/README.md)). Make `ISCC.exe` available on `PATH`
or in its standard `Program Files (x86)` / `Program Files` installation directory.
From the repository root:

```powershell
pwsh ./scripts/package-windows.ps1
pwsh ./scripts/smoke-test-windows-installer.ps1 -InstallerPath artifacts/windows/installer/SpanDraft-X.Y.Z-win-x64-setup.exe
```

The packaging script checks notices, builds and validates offline help, publishes
the self-contained `win-x64` application and invokes Inno Setup. The smoke test
requires no existing SpanDraft installation or conflicting shortcuts. It checks
the default per-user installation under `%LOCALAPPDATA%\Programs\SpanDraft`,
leaves the optional desktop shortcut disabled and cleans up its own installation
on failure where possible. User data under `%LOCALAPPDATA%\SpanDraft` is retained.

Manual Windows checks remain useful for visible installer UX, new setup options,
larger OS integration changes or significant visual UI changes. As appropriate,
inspect the wizard, optional desktop shortcut, finish-page launch, Start menu
launch, first project and calculation, offline help, reinstall and Windows
Settings uninstall. These are checks of the change, not a ritual for every patch
release. The automated startup check does not evaluate rendering or interactive
GUI behavior; product and calculation logic remains covered by the existing tests.
