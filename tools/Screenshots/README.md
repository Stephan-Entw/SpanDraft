# Native screenshots for Microsoft Store and Penpot

This independent, manual export uses the real SpanDraft application and native
Windows HWND capture. A complete run creates ten Microsoft Store PNGs and two
Penpot hero PNGs. The website is outside this tool: Store Project PNGs can later
be copied separately into the website repository and processed responsively by
Astro there. This tool has no website dependency or WebP processing.

## Prerequisites and invocation

Use Windows, PowerShell 7, the .NET 10 SDK and WinApp CLI, with `winapp.exe` on
PATH. Use a neutral checkout path such as `C:\dev\SpanDraft`; personal profile
paths are rejected. From the repository root:

```powershell
.\tools\Screenshots\capture-screenshots.ps1
```

The defaults are **Store at 250%**, followed by **Penpot at 200%**. Start with an
active display at 250%. After both Store languages finish and their workers exit,
the runner pauses. Manually change Windows scaling to 200%, then press Enter in
the terminal. The runner validates the new native DPI before starting Penpot.
Type `q` or press Ctrl+C to cancel. The entire selection remains temporary until
both phases succeed. One display is sufficient; two simultaneously configured
displays are not required.

The runner never changes Windows scaling, resolution, display activation,
positions, topology, drivers or guest services, and never restores settings
at the end. A scale change must become effective within the running Windows
session. Signing out terminates the run; it cannot resume across sign-out.

Scaling is deliberately configurable for visual experiments, not a fixed
production design contract:

```powershell
# Individual Store motif or language:
.\tools\Screenshots\capture-screenshots.ps1 -Target Store -Language en-US -Scene Section
.\tools\Screenshots\capture-screenshots.ps1 -Target Store -Scene Materials -ScalePercent 240

# Penpot alone uses its own requested scale:
.\tools\Screenshots\capture-screenshots.ps1 -Target Penpot -PenpotScalePercent 200

# Both Project motifs, or both profiles at one common scale without a phase pause:
.\tools\Screenshots\capture-screenshots.ps1 -Scene Project
.\tools\Screenshots\capture-screenshots.ps1 -ScalePercent 200 -PenpotScalePercent 200
```

| Parameter | Values | Default |
| --- | --- | --- |
| `-Language` | `All`, `de-DE`, `en-US` | `All` |
| `-Scene` | `All`, `Start`, `Project`, `Section`, `Report`, `Materials` | `All` |
| `-Target` | `All`, `Store`, `Penpot` | `All` |
| `-ScalePercent` | Positive integer, Store scale | `250` |
| `-PenpotScalePercent` | Positive integer, Penpot scale | `200` |

`Target=Penpot` and `Target=All` permit only `Scene=All` or `Scene=Project`.
For an individual non-Project motif, specify `-Target Store`; unsupported
combinations fail explicitly. Filters export exactly their selection into a new
run, without merging earlier results. Free DIP/size overrides are not supported.
A required phase transition needs an interactive terminal without redirected input.

## Profiles and native validation

| Profile | Complete visible window | Motifs | Languages |
| --- | --- | --- | --- |
| Store | **3840 × 2160 px**, PNG | Start, Project, Section, Report, Materials | de-DE, en-US |
| PenpotHero | **2400 × 1800 px**, PNG | Project | de-DE, en-US |

Profiles in `CaptureConfiguration.cs` define actual pixel targets, including the
native title bar. Scaling determines the effective UI size inside those pixels;
it never changes the pixel target. Nothing crops, composites, resizes or upscales
a captured image. Penpot is captured independently, never derived from Store.

Native DPI is measured with short-lived, hidden, per-monitor-aware probe HWNDs.
There is no whitelist of Windows percentage steps: 240%, 250% and 260% can be
requested if Windows actually supplies the corresponding DPI. Requested percent
is converted to the nearest whole DPI (`percent × 96 / 100`). The real application
HWND must use that DPI on the selected display, and Avalonia RenderScaling must
match `DPI / 96`. Missing, inactive, wrong or unknown-DPI displays fail with the
detected values; no automatic correction is attempted. The primary matching
display is preferred, otherwise an already active matching display is used.

The real window minimums are retained. In particular, Penpot at 250% would be
narrower than MainWindow's 1100-DIP minimum; its separate 200% default avoids
forcing a narrower product layout. Incompatible scale/size combinations fail
instead of overriding the minimum or resizing the PNG.

Each profile/language starts a fresh worker from the current build, using the
actual `App`, `MainWindow`, styles, views and view models. Process path and startup
time are checked against the completed build. Native caption styles, app icon,
caption buttons, window/display assignment, client pixels, rendering scale and
DWM/PNG dimensions are checked and recorded. Layout waits use animation frames,
completed compositor render batches and DWM synchronization with bounded timeouts.

WinApp's `ui screenshot --window <HWND>` captures the complete native window,
including portions outside the desktop. The desktop may be smaller than the
window. Measured invisible Windows resize borders are added to the outer HWND
size. The window-local `WM_GETMINMAXINFO` handler extends only the maximum track
size, preserving minimum/maximize constraints, and is removed on worker exit.
Only the worker's own HWND is moved or resized.

Display snapshots are checked before application startup, positioning, each
capture, phase completion, and final publication after temporary state cleanup.
Within a phase, the entire snapshot must remain unchanged. Only at the explicit,
confirmed boundary between different scale groups may DPI change. Identity,
activation, resolution and position must remain unchanged even at that boundary.
The manifest records phase snapshots and confirmation times, rather than
claiming the whole run left scaling unchanged.

## Historical output and failure behavior

```text
artifacts/screenshots/
├── build/
├── .runs/
├── .capture.lock
└── 2026-11-06_18-22-31.0000000Z/
    ├── store/
    │   ├── 01-start-de-DE.png
    │   ├── 02-project-de-DE.png
    │   ├── 03-section-de-DE.png
    │   ├── 04-report-de-DE.png
    │   ├── 05-materials-de-DE.png
    │   ├── 01-start-en-US.png
    │   ├── 02-project-en-US.png
    │   ├── 03-section-en-US.png
    │   ├── 04-report-en-US.png
    │   └── 05-materials-en-US.png
    ├── penpot/
    │   ├── hero-project-de-DE.png
    │   └── hero-project-en-US.png
    └── result.json
```

The runner creates one UTC timestamp for the whole run after acquiring its lock.
Names use `yyyy-MM-dd_HH-mm-ss.fffffffZ`, sort chronologically, and receive a
numeric suffix if a name is already occupied. Existing runs are never overwritten.
`CaptureFiles.FileName` centrally defines Store's `{index}-{scene}-{culture}.png`
and Penpot's `hero-project-{culture}.png` schema.

The runner stages every requested PNG under its GUID-owned `.runs/` directory.
Worker state is removed, then fixture preservation, display preservation and the
complete expected file set are validated. The directory containing just the
validated PNGs and successful manifest is published with one same-volume rename.
No final PNGs are exposed while later captures or validations can still fail.

A failed runner invocation publishes only `result.json` with `success=false` in
its own timestamp directory, with no PNGs. Older results remain unchanged. The
terminal prints the exact result path. Build failures, an unavailable output lock
or filesystem failures that prevent manifest publication are reported directly;
no older manifest is presented as their result. Cancellation and timeout terminate
only the runner's own subprocesses. Temporary cleanup is restricted to directly
owned GUID directories and refuses reparse-point traversal. An unsafe cleanup
failure is reported; such temporary data is never published.

`result.json` records the selection and expected filenames, success/failure,
run/phase/capture times, manual transition confirmations, before/after display
snapshots, preservation within phases, fixture SHA-256, profile/language/motif,
requested scale and actual native measurements, process provenance, relative
state-access paths and verified scene state. Diagnostic paths are repo-relative;
error messages redact checkout and user-profile paths.

## Fixture and isolation

`Fixtures/Crane Beam.spandraft` is preserved byte for byte, with its original
SHA-256 checked by runner, workers and tests. Store Project and Penpot Project
open identical private copies through the same real project-opening workflow.
There is no specially modified hero project. Section and report also use this
fixture. Section/material editing is canceled; the project session must remain
unchanged and solve successfully.

All settings, recovery slots and user libraries are redirected to worker-local
state. The restricted file store rejects access outside that state and the
explicit read-only project copies. Real SpanDraft user state is never read and
no operating-system username is supplied. Personal paths and visible internal
run paths are rejected.

The isolated start page contains five display-only recent paths under
`C:\SpanDraft\Examples\`: Crane Beam, Cantilever Beam, Simply Supported Beam,
Overhang Beam and Fixed-End Beam. They need not exist and are never opened. Both
languages use the same prepared timestamps. All five rows and their neutral paths
must be visible within the viewport before the fixture copy is opened.

The materials motif uses the real browser, the built-in library and selected
`S355JR (1.0045)`, scrolled into view with an empty search. Selection is not applied
to the project and no user-library entries are created. UI language changes
between workers; metric units and German regional number formatting stay the
same. Report dates reflect run time, so PNGs need not be byte-identical across dates.

Builds stay in the shared `build/` directory, independent of ordinary Desktop
output. Tooling and fixtures are excluded from the normal Desktop publish and
installer input. Review all selected native images after a successful Windows
run; the tool makes no aesthetic adjustments. Final production scaling remains
subject to that visual acceptance.
