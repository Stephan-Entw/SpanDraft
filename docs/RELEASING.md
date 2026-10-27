# Releasing SpanDraft

GitHub Windows x64 releases are automatic after a successful push CI run on
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
   shortcuts, verifies the project icon and per-user file association, then opens
   a fixture project through the Windows shell. It verifies startup and the
   expected project title, then stops the application and uninstalls it,
   checking association cleanup as well.
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
The script also searches `%LOCALAPPDATA%\Programs\Inno Setup 6`.
From the repository root:

```powershell
pwsh ./scripts/package-windows.ps1
pwsh ./scripts/smoke-test-windows-installer.ps1 -InstallerPath artifacts/windows/installer/SpanDraft-X.Y.Z-win-x64-setup.exe
```

The packaging script checks notices, builds and validates offline help, publishes
the self-contained `win-x64` application and invokes Inno Setup. The installer
registers the `.spandraft` icon and open command per user; Windows controls the
user's default-app choice. The smoke test requires no existing SpanDraft
installation, conflicting shortcuts or file associations, running application
or recovery slots. It checks the default per-user installation under
`%LOCALAPPDATA%\Programs\SpanDraft`, leaves the optional desktop shortcut disabled
and cleans up its own installation
on failure where possible. User data under `%LOCALAPPDATA%\SpanDraft` is retained.

Manual Windows checks remain useful for visible installer UX, new setup options,
larger OS integration changes or significant visual UI changes. As appropriate,
inspect the wizard, optional desktop shortcut, finish-page launch, Start menu
launch, opening a project from a path containing spaces/non-ASCII characters,
first project and calculation, offline help, reinstall and Windows
Settings uninstall. These are checks of the change, not a ritual for every patch
release. The automated startup check does not evaluate rendering or interactive
GUI behavior; product and calculation logic remains covered by the existing tests.

## Independent Microsoft Store release (MSIX)

The Store route is local and manual, independent of GitHub/Inno Setup. Neither
route invokes or requires the other. There is no Store GitHub Action or Partner
Center upload automation. Both can use the same released source commit.

The MSIX tooling was added after `v0.1.0` and is absent from that tag. Use
`-Development` from the current unreleased checkout for local checks; the
Submission instructions below apply to a published tag that includes this
tooling.

### Build from the released source

Use Windows x64 with PowerShell 7, Git, .NET 10, the existing
[offline help prerequisites](../help/README.md) and an installed Windows 10/11 SDK
containing `MakeAppx.exe` and `MakePri.exe`. The script searches PATH and
`Windows Kits/10/bin/<version>/x64`; it installs no tools or certificates. Inno
Setup, SignTool and WACK are not build prerequisites.

From a clean checkout containing the published tag:

```powershell
git switch --detach vX.Y.Z
pwsh ./scripts/package-microsoft-store.ps1
```

The default **Submission** mode requires a clean Git working tree (including
non-ignored untracked files), HEAD exactly equal to `vX.Y.Z`, and the effective
Desktop MSBuild version equal to `Directory.Build.props`. It repeats these checks
before releasing the result and rejects a changed HEAD or version. Fetch missing
published tags explicitly; the script never creates or moves tags.

`Directory.Build.props` remains the sole product version source. Canonical
`X.Y.Z` maps to `X.Y.Z.0`, for example `0.2.0` to `0.2.0.0` and `1.0.0` to
`1.0.0.0`. Each component must fit the MSIX range 0–65535. There is no version
override. Explicit local development builds, including dirty or untagged sources:

```powershell
pwsh ./scripts/package-microsoft-store.ps1 -Development
```

Both modes reuse the existing notices check and offline help build, publish
Release/win-x64 self-contained, create fresh staging, derive the required visual
assets from the existing app and project-file icons, index DPI and unplated
shell variants with MakePri, and run MakeAppx with semantic validation enabled.
The packaging helper then checks the actual package, including hashes of every
staged file. It requires the app/runtime, resource index, notices, license and
offline help. MakeAppx validation alone is not proof that a package installs or runs.

Results are under `artifacts/microsoft-store/submission/` or `development/`:

- `SpanDraft-X.Y.Z.0-x64.msix` (development names end in `-development.msix`).
- The generated `AppxManifest.xml`, SHA-256 sidecar and `build-info.json` recording
  source commit, dirty state, mode, product/package versions and package hash.

A new build clears only its own mode's previous results and working directory.
Copy packages needed for update tests elsewhere first, for example into
`artifacts/microsoft-store/local-tests/`. Development output is not a submission
artifact. A failed build publishes no new final package.

The fixed identity is `S.Antipin.SpanDraft`, publisher
`CN=AC57B091-093F-4A75-87A8-A6DB7BD6BBFF`, publisher display name `S. Antipin`.
The x64 package targets `Windows.Desktop` from `10.0.19041.0`, initially with the
same `MaxVersionTested`. It declares `packagedClassicApp`, `mediumIL` and
`runFullTrust`, following Microsoft's
[packaged desktop configuration](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion).
Its [file association](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap3-filetypeassociation)
passes one quoted `.spandraft` path to the existing desktop startup code. Windows
controls the user's default app choice. There are no MSIX-specific registry writes
or application/persistence branches. Package-specific AppData virtualization is
accepted; sharing or migrating data with Inno installations is not supported.

### Separate local test signing and smoke tests

The submission package is **unsigned**. Microsoft signs packages submitted to the
Store; no commercial signing certificate is needed for this route. See Microsoft's
[signing guide](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide).
Keep that artifact unchanged and sign a separate copy for local installation.

Use a disposable Windows test account or VM with no existing SpanDraft installation,
running instance or SpanDraft LocalAppData directory. Have a suitable, already
trusted test certificate with private key in `Cert:\CurrentUser\My`. Its subject
must exactly match the manifest Publisher. Certificate provisioning and trust are
separate, deliberate machine setup; these scripts never create, import or trust a
certificate. Never commit private keys, PFX files or secrets.

With `SignTool.exe` from the installed Windows SDK on PATH:

```powershell
$unsigned = 'artifacts/microsoft-store/submission/SpanDraft-X.Y.Z.0-x64.msix'
$signed = 'artifacts/microsoft-store/local-tests/SpanDraft-X.Y.Z.0-x64-test.msix'
New-Item -ItemType Directory -Path (Split-Path $signed) -Force | Out-Null
Copy-Item -LiteralPath $unsigned -Destination $signed
$thumbprint = 'THUMBPRINT_OF_EXISTING_TEST_CERTIFICATE'
signtool sign /fd SHA256 /s My /sha1 $thumbprint $signed
if ($LASTEXITCODE -ne 0) { throw 'Test signing failed.' }
signtool verify /pa /v $signed
if ($LASTEXITCODE -ne 0) { throw 'Test signature verification failed.' }
pwsh ./scripts/smoke-test-microsoft-store.ps1 -PackagePath $signed
```

The smoke test validates the package and trusted signature, installs it with
`Add-AppxPackage`, checks registered identity/version/architecture and installed
files, launches via the registered application ID, verifies a fresh process
starts successfully from the package install location, and uninstalls its
own test installation. It uses the Windows PowerShell Appx module through
PowerShell 7 compatibility. It does not inspect or automate the UI.

To exercise an update, retain a signed older package and sign the newer package
with the same certificate/identity. Build each from its corresponding source
version; do not add a separate package version or override the central version:

```powershell
pwsh ./scripts/smoke-test-microsoft-store.ps1 `
  -PreviousPackagePath artifacts/microsoft-store/local-tests/SpanDraft-OLDER-x64-test.msix `
  -PackagePath artifacts/microsoft-store/local-tests/SpanDraft-NEWER-x64-test.msix
```

This installs and launches the older version, writes a unique marker in its
package LocalState, updates to the strictly newer version, verifies unchanged
package family and marker, launches again and uninstalls. The marker checks OS
package-data retention; it does not prove real settings/library behavior.

### Manual Windows and Store acceptance

For interactive checks, install the separate signed test copy with
`Add-AppxPackage -Path <signed-test.msix>` and perform the following on the test
account. The automated smoke test uninstalls its package, so it is a separate run.

- Launch from Start and use **Open with → SpanDraft** for a `.spandraft` file in
  a path containing spaces/non-ASCII characters. Confirm the expected project
  opens. If testing a default association, select it through Windows Settings.
- Open an existing project, modify it, save/save as, close and reopen it. Check
  calculation and offline help.
- Change settings, add material and section library entries, open another project,
  then restart. Verify settings, both libraries and recent projects persist.
- Make unsaved project changes, allow recovery to be written, terminate the test
  process and restart. Verify recovery; repeat for an unfinished setup draft.
- Generate a PDF report, save it outside the package, reopen the PDF and print
  manually. Exercise Windows file dialogs and ordinary user document paths.
- Before an update, create recognizable settings/library entries and saved project
  data; also prepare recovery. Close/terminate as appropriate, install the newer
  signed package without uninstalling the older one, and verify actual settings,
  libraries, recents, projects and recovery survive. Repeat open/save/PDF checks.
- Uninstall through Windows Settings and confirm registration is removed and
  external project/PDF files remain. No data-migration or Inno/MSIX side-by-side
  acceptance is required.

Optionally run the installed Windows App Certification Kit (WACK) against the
test package and review its report. It is an additional local check, never a
build dependency or a substitute for Store certification.

After acceptance, verify the original unsigned submission package against its
SHA-256 sidecar and manually upload **that package** to the matching Partner
Center product. Do not upload the locally signed test copy or a development
package. Review Store certification results and restricted-capability requests;
publication remains a manual Partner Center action.

For Store listing images, the independent
[native screenshot workflow](../tools/StoreScreenshots/README.md) captures the
current application in German and English. It is not a packaging or release gate.
