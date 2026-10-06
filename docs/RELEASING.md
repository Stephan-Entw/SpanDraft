# Releasing SpanDraft

This document describes the release process for SpanDraft.

The goal is to keep releases simple, reproducible, and consistent.
A release is created only from a tested commit on `main`.

## 1. Prepare the version

Update the version in `Directory.Build.props`:

```xml
<Version>0.1.0</Version>
```

This is the application version used by the build and installer.

## 2. Update the release notes

Add or finalize the corresponding section in `RELEASE_NOTES.md`.

Example:

```markdown
## 0.1.0

First public release of SpanDraft.

### Added
- ...
- ...
```

The newest release should always be placed at the top of the file.

The release workflow uses this section as the GitHub Release description.

## 3. Update the README

Update `README.md` if the current release changes the documented feature set, screenshots, installation instructions, or other user-facing information.

## 4. Commit and push

Commit all release-related changes normally and push them to `main`.

The release commit should include at least:

- the final application code
- the updated version
- the release notes
- README changes, if required

## 5. Test the installer

Run the manual package workflow in GitHub Actions.

Download the generated Windows installer and verify that the application installs, starts, and works as expected.

This package build does not create a Git tag or GitHub Release and may be repeated as often as necessary.

## 6. Create the release tag

Once the tested commit is ready for release, create a tag matching the application version:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The tag must point to the exact commit that was tested.

## 7. Automated release

Pushing a release tag starts the GitHub Actions release workflow.

The workflow:

1. runs the automated tests
2. verifies that the tag matches the version in `Directory.Build.props`
3. verifies that a matching section exists in `RELEASE_NOTES.md`
4. builds SpanDraft
5. creates the Windows installer
6. creates the GitHub Release
7. uses the matching `RELEASE_NOTES.md` section as the release description
8. attaches the installer to the GitHub Release

If one of the validation steps fails, no release should be published.

## Version format

SpanDraft uses semantic version numbers:

```text
MAJOR.MINOR.PATCH
```

Examples:

```text
0.1.0
0.2.0
0.2.1
1.0.0
```

Git tags use the same version prefixed with `v`:

```text
v0.1.0
v0.2.0
```

Pre-release tags such as `alpha`, `beta`, or `rc` are not currently used.
