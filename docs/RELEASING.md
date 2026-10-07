# Releasing SpanDraft

SpanDraft has not reached its first public release yet. This document defines
the intended process; the current repository only contains the regular CI workflow.

## Prerequisites

Before the first release, add:

- a central `<Version>` property in `Directory.Build.props`
- `RELEASE_NOTES.md`, with the newest release first
- a manual package workflow for the Windows installer
- a release workflow triggered by a version tag

Versions will use `MAJOR.MINOR.PATCH` and matching `v`-prefixed tags.
Pre-release tags are not part of this target process.

## Intended release process

1. Prepare the version and release notes; update user-facing documentation as needed.
2. Commit the changes to `main` and pass the product and independent validation gates.
3. Run the manual package workflow and verify that the Windows installer installs,
   starts and runs the application. This workflow must not publish a tag or release.
4. Tag the exact tested commit with the matching version and push the tag.

## Planned release automation

The tag-triggered workflow must run both CI gates and verify that the tag matches
the central version and a release-notes section. After those checks pass, it must
build the Windows installer and publish a GitHub Release with the matching notes
and installer attached. A failed check must prevent publication.
