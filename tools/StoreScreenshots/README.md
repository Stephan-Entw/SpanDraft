# Native Microsoft Store screenshots

Run from the repository root on Windows with the .NET 10 SDK and WinApp CLI:

```powershell
.\tools\StoreScreenshots\capture-screenshots.ps1
.\tools\StoreScreenshots\capture-screenshots.ps1 -Language en-US -Scene Section
.\tools\StoreScreenshots\capture-screenshots.ps1 -Language en-US -Scene Materials
.\tools\StoreScreenshots\capture-screenshots.ps1 -WidthDip 1600 -HeightDip 900
```

The runner uses an already active Windows display at **200% (192 DPI)**,
preferring the primary display. It never changes resolution, scaling, activation,
topology or guest services. An unsuitable scaling configuration fails preflight
with the detected values; there is no automatic correction.

The existing `winapp ui screenshot --window <HWND>` command captures the complete
native window, including portions outside the desktop. The desktop may be smaller
than the requested window. A window-local `WM_GETMINMAXINFO` handler allows the
measured outer HWND size while preserving native minimum/maximize limits. It is
removed when capture finishes. Only the runner's own HWND is resized and moved.

Display snapshots are checked before startup, positioning, each capture and
publishing, and again after cleanup. Cleanup removes only the runner's temporary
application state. No display driver installation or virtual screen is required.
WinApp must be accessible through `winapp.exe` on PATH.

`capture-size.json` is the single source of default dimensions. Dimensions refer
to the **complete visible native window**, including its title bar, rather than
the client area. At 200%, the defaults produce **3200 × 1800 pixel** PNGs. The
runner measures invisible Windows resize borders once and accounts for them in
the outer HWND bounds. Nothing crops, composites or resizes an image.

All ten PNGs are published directly in `artifacts/store-screenshots/`, with no
language subdirectories. `CaptureFiles.FileName` centrally defines the schema
`{index}-{name}-{culture}.png`:

| Motif | German | English |
| --- | --- | --- |
| Start | `01-start-de-DE.png` | `01-start-en-US.png` |
| Project | `02-project-de-DE.png` | `02-project-en-US.png` |
| Section | `03-section-de-DE.png` | `03-section-en-US.png` |
| Report | `04-report-de-DE.png` | `04-report-en-US.png` |
| Materials | `05-materials-de-DE.png` | `05-materials-en-US.png` |

A language or scene filter publishes only that selection. A successful
`result.json` uses these same filenames and describes the captures from that
invocation; previous PNGs are not evidence that a failed invocation succeeded.

Builds go to `artifacts/store-screenshots/build/`, so an existing Desktop process
can keep running. Every language starts a fresh harness EXE from that build. The
harness uses the actual `App`, `MainWindow`, styles, views, and view models.
Windows/WinApp produce the final HWND PNGs. Native caption styles, icon and caption
button bounds, DPI, client pixels, rendering scale and DWM/PNG dimensions are
checked and recorded. Layout waits use animation frames and completed compositor
render batches, followed by DWM synchronization, with bounded timeouts.

`Fixtures/Crane Beam.spandraft` is the supplied project, preserved byte for byte.
Its SHA-256 is checked against the original in the runner and tests. Main view,
section editor and report all use this project; section editing is canceled.
The start page is seeded through the isolated recent-project store with five
display-only paths under `C:\SpanDraft\Examples\`: `Crane Beam.spandraft`,
`Cantilever Beam.spandraft`, `Simply Supported Beam.spandraft`,
`Overhang Beam.spandraft`, and `Fixed-End Beam.spandraft`. These files need not
exist and are not opened. Only the actual demo project has a private byte-identical
fixture copy inside the run directory. Start-page captures happen before opening it.
Both languages use the same demo timestamps. The runner verifies all five
displayed path texts and that every row is within the list viewport, with no
internal or personal paths.

The materials motif opens the existing project material browser, selects the
built-in library and `S355JR (1.0045)`, and brings the selected row into view.
The search is empty and the list and details remain visible. Selection is never
applied to the project and no user-library entries are created or changed.
UI language changes between processes; metric units and German regional number
formatting stay the same. The report date reflects the run time; this workflow
does not promise byte-identical PNGs across dates.

Each invocation creates a unique `.runs/` directory beneath the screenshot output.
All app settings, recovery slots and user libraries are redirected there. The
restricted file store refuses access outside that state and the explicitly allowed
read-only project copies. It does not read the real SpanDraft state, and supplies
no operating-system username. Reparse points and personal profile paths are
rejected. Temporary state is removed on success or failure, and only the runner's
own subprocesses may be terminated on timeout. The repository must use a neutral
path such as `C:\dev\SpanDraft`; internal run paths are never shown on the start page.

`result.json` records success/failure, fixture preservation, display
snapshots before/after and their preservation, native capture measurements
and relative state-access paths, plus the prepared recent entries and selected
built-in material for each language. When a
preflight fails, no application window or demo state is created. Tooling and
fixtures have no dependency edge from the Desktop project and are excluded from
its normal publish and installer input.

Review the ten actual images after a successful run. This tooling makes no
aesthetic adjustments or automatic dimension changes.

Boot settings and installed display drivers are system configuration and are
outside this tool.
