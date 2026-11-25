# Native screenshots for Microsoft Store and Penpot

This independent, manual export uses the real SpanDraft application and native
Windows HWND capture. A complete run creates ten Microsoft Store PNGs and two
Penpot hero PNGs. The website imports the two original Penpot hero PNGs from
`src/assets/` in the website repository and builds responsive WebP images with
Astro. Copy the `penpot/hero-project-{culture}.png` files there under their
existing names. This tool has no website dependency or WebP processing.

## Prerequisites and invocation

Use Windows, PowerShell 7, the .NET 10 SDK and WinApp CLI, with `winapp.exe` on
PATH. Use a neutral checkout path such as `C:\dev\SpanDraft`; personal profile
paths are rejected. From the repository root:

```powershell
.\tools\Screenshots\capture-screenshots.ps1
```

The defaults are **Store at 250%**, followed by **Penpot at 200%**. The runner
automatically selects an already matching active display, preferring the primary.
If none matches, it temporarily changes an active display's Windows scaling,
preferring a secondary display so the primary can remain untouched. One display
is sufficient: it is automatically switched between phases without a terminal
pause. Two displays already configured at 250% and 200% need no scaling changes.

The original scaling of every changed display is restored before publication,
including after errors or Ctrl+C. Restoration ignores capture cancellation and
is verified using native DPI. A restoration failure fails the run and publishes
no PNGs. Force-killing the runner, signing out or shutting down Windows prevents
cleanup; if that happens, restore Windows scaling manually.

Resolution, display activation, positions, topology, drivers and guest services
are never changed. Automatic scaling uses the private Windows DisplayConfig DPI
packets (-3/-4), isolated in `NativeDisplayScaling.cs`; these are undocumented
and may be unavailable on a Windows/driver combination. Unsupported requests
fail explicitly instead of substituting another percentage. No registry editing,
sign-out or display driver installation is used.

The production path uses Store at 250% and Penpot at 200%. Scaling remains
configurable for visual experiments:

```powershell
# Individual Store motif or language:
.\tools\Screenshots\capture-screenshots.ps1 -Target Store -Language en-US -Scene Section
.\tools\Screenshots\capture-screenshots.ps1 -Target Store -Scene Materials -ScalePercent 240

# Penpot alone uses its own requested scale:
.\tools\Screenshots\capture-screenshots.ps1 -Target Penpot -PenpotScalePercent 200

# Both Project motifs, or both profiles at one common scale:
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
Redirected input is supported; no interactive confirmation is needed.

## Profiles and native validation

| Profile | Complete visible window | Windows scaling | Motifs | Languages |
| --- | --- | --- | --- | --- |
| Store | **3312 × 1863 px**, PNG, exactly 16:9 | **250%** | Start, Project, Section, Report, Materials | de-DE, en-US |
| PenpotHero | **2400 × 1868 px**, PNG | **200%** | Project | de-DE, en-US |

Profiles in `CaptureConfiguration.cs` define actual pixel targets, including the
native title bar. Scaling determines the effective UI size inside those pixels;
it never changes the pixel target. Nothing crops, composites, resizes, upscales,
downsamples or otherwise post-processes a captured image. Penpot is captured
independently, never derived from Store.

The Store resolution is deliberately smaller than 4K: at native 250% it provides
1324.8 × 745.2 DIP, approximately the logical workspace of 4K at 289.9%.
The desired look comes solely from the smaller native window at real 250%;
the runner neither requests 290% Windows DPI nor resamples the screenshot.
Store requires 240 DPI and Avalonia `RenderScaling = 2.5`; Penpot requires
192 DPI and `RenderScaling = 2.0` at its unchanged 200% default.

Penpot's native height provides room for the divider, all three result diagrams
and the spacing below them at 200% scaling, without a vertical scrollbar.
The runner rejects Penpot captures with a remaining vertical scroll range in
the editor, diagrams or project overview.

Native DPI is measured with short-lived, hidden, per-monitor-aware probe HWNDs.
Automatic changes use Windows' supported discrete steps, including 200% and
250%, within the selected display's reported range. Custom percentages such as
240% or 260% can still be captured on an already matching display; they are not
automatically set. An existing custom scale that differs from the discrete DPI
offset cannot be changed automatically, since it could not be restored reliably.
Requested percent is converted to the nearest whole DPI (`percent × 96 / 100`).
The real application HWND must use that DPI on the selected display, and Avalonia
RenderScaling must match `DPI / 96`. The runner waits for native DPI after every
change and fails if Windows does not apply it within the timeout.

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
capture, phase completion, and final publication after restoration. Within a
phase, the entire snapshot must remain unchanged. DPI may change only during
the runner's automatic transitions and restoration. Identity, activation,
resolution and position must remain unchanged throughout. The manifest records
phase snapshots, automatic transitions, and verified restoration of the initial
display configuration.

## Historical output and failure behavior

```text
artifacts/screenshots/
├── build/
├── .runs/
├── .capture.lock
└── 1.2.0_2026-11-06_18-22-31.0000000Z/
    ├── store/
    │   ├── DE/
    │   │   ├── 01-project-de-DE.png
    │   │   ├── 02-section-de-DE.png
    │   │   ├── 03-materials-de-DE.png
    │   │   ├── 04-report-de-DE.png
    │   │   └── 05-start-de-DE.png
    │   └── EN/
    │       ├── 01-project-en-US.png
    │       ├── 02-section-en-US.png
    │       ├── 03-materials-en-US.png
    │       ├── 04-report-en-US.png
    │       └── 05-start-en-US.png
    ├── penpot/
    │   ├── hero-project-de-DE.png
    │   └── hero-project-en-US.png
    ├── hero/                      (only with -Hero)
    └── result.json
```

The runner creates one UTC timestamp for the whole run after acquiring its lock.
Names use `<version>_yyyy-MM-dd_HH-mm-ss.fffffffZ`, where `<version>` is the
captured build's product version (also recorded as `productVersion` in
`result.json`). Within a version they sort chronologically and receive a
numeric suffix if a name is already occupied. Existing runs are never overwritten.
`CaptureFiles.FileName` centrally defines Store's `{index}-{scene}-{culture}.png`
and Penpot's `hero-project-{culture}.png` schema. Store files are grouped under
`store/DE` and `store/EN`, numbered Project 01, Section 02, Materials 03, Report 04
and Start 05. The capture sequence still prepares Start before opening the project.

After a successful run, the runner opens the timestamped output directory in
Explorer. It also prints the absolute directory and `result.json` paths to the
terminal. If opening Explorer fails, the saved screenshots remain successful
and the printed path can be opened manually.

The runner stages every requested PNG under its GUID-owned `.runs/` directory.
Worker state is removed, then fixture preservation, display restoration and the
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
run/phase/capture times, automatic scale transitions, before/after display
snapshots, preservation within phases, scaling restoration, fixture SHA-256,
profile/language/motif,
requested scale and actual native measurements, process provenance, relative
state-access paths and verified scene state. Diagnostic paths are repo-relative;
error messages redact checkout and user-profile paths.

## Store hero image (HTML template)

`Hero/` renders the first Microsoft Store image (3840 × 2160 px) from `hero.html` with
headless Chromium (Playwright), replacing the manual Penpot composition. Layout values come
from the Penpot SVG export; all language-dependent text lives in `Hero/texts/<culture>.json`
and appears in the template as `{{placeholders}}`. The logo is the app icon source SVG, the
fonts (Oxanium 600, Golos Text 400/500, SIL OFL) are in `Hero/fonts/`.

The Penpot capture `penpot/hero-project-<culture>.png` is inserted 1:1 (natural size, no
scaling; position and width as in the Penpot original, height read from the image). The
renderer fails when the image is scaled, a placeholder stays unfilled or a text line wraps.

```bash
cd tools/Screenshots/Hero && npm ci && npx playwright install chromium && node render-hero.mjs
# optional: --shots <dir with hero-project-*.png> --out <dir> --lang de-DE|en-US
```

Without `--shots` the newest run under `artifacts/screenshots/` is used. Output goes to
`hero/` inside that run (git-ignored). `capture-screenshots.ps1 -Hero` runs it after a
capture that includes the Penpot Project motif.

## Fixture and isolation

`Fixtures/Crane Beam.spandraft` is preserved byte for byte, with its original
SHA-256 checked by runner, workers and tests. Each worker prepares a private copy
with the language's project filename, calculation and project titles, and author.
The copy omits the saved section name so the application generates it from the
geometry in the active UI language. All motifs use the same preparation, with
identical engineering data. Workers open the copy through the real project-opening
workflow and check its hash at the end of capture. Section and material editing
is canceled, and the project session must stay clean and solve successfully.

All settings, recovery slots and user libraries are redirected to worker-local
state. The restricted file store rejects access outside that state and the
explicit read-only project copies. Real SpanDraft user state is never read and
no operating-system username is supplied. Personal paths and visible internal
run paths are rejected.

The isolated start page contains five display-only recent paths under
`C:\SpanDraft\Examples\`. The example filenames are localized and both languages
use the same prepared timestamps. The paths need not exist and are never opened.
All five rows and their paths must be visible within the viewport before the
fixture copy is opened.

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
