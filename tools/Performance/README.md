# Interaction performance runner

This opt-in tool runs the real SpanDraft desktop application with synthetic projects
and isolated in-memory settings, libraries, recovery and project storage. Only the
explicit result file is written to disk. It uses the existing DEV-75 recorder and
native Avalonia `Rendered` endpoint; it does not drive input devices, inspect
screenshots, force visual invalidation or substitute solver timings.

## Build and run

Use .NET 10 and an interactive Windows, macOS or Linux desktop. Keep the application
visible and unobstructed and do not interact with it during measurement. Do not
change displays, scaling or window size during a run. Use a fixed reference machine
for repeatable comparisons; hosted CI is only a functional test environment.

From the repository root:

```sh
dotnet run --project tools/Performance/SpanDraft.Performance.csproj -c Release -- measure --scenario all --warmup 10 --iterations 50 --output artifacts/performance/run.json --environment-id ref-desktop-a
dotnet run --project tools/Performance/SpanDraft.Performance.csproj -c Release --no-build -- compare baseline.json current.json
```

`--scenario` accepts `small`, `typical`, `large` or `all` (default). Warm-up defaults
to 10 and measured attempts to 50 **per scenario/action combination**. Warm-up may
be zero; iterations must be positive. `--output` is required and must not already
exist. The optional environment ID is a neutral label of at most 64 ASCII letters,
digits, dots, underscores or hyphens. Do not use personal identifiers.

A short **functional native acceptance run**, not a performance baseline:

```sh
dotnet run --project tools/Performance/SpanDraft.Performance.csproj -c Release -- measure --scenario typical --warmup 2 --iterations 5 --output artifacts/performance/smoke.json
```

Each successful sample proves native window → confirmed product action → DEV-75
measurement → rendered frame → runner sample. A desktop startup or preparation-frame
failure is a technical error, not an opportunity to generate substitute numbers.
`compare` does not initialize Avalonia and needs no graphical session.

## Measurement protocol v1

`ReferenceScenarios.cs` defines deterministic Small (simple beam), Typical
(multi-section beam; primary reference) and Large (extended multi-span report
reference) projects. Fixed identities, modest stiffness contrasts and ordinary
loads avoid random data and pathological numerical cases. Tests freeze the
reference content and verify successful analyses before and after the actions.
Changing the references, actions, reset sequence or fixed UI settings requires a
new protocol version; do not silently reuse the IDs with different inputs.

The actions change a point-force value, the first segment length through project
setup, and a uniform distributed-load intensity. Draft preparation precedes the
existing product confirmation; parsing and validation within that confirmation
remain in DEV-75 Preparation. Geometry measurements include setup's return to the
editor. All durations come from the existing recorder without changing its phase
boundaries or two-second frame abandonment rule.

The window is fixed at 1300 × 800 DIP. UI and formatting culture are `en-US`, with
Structural Engineering units. Actual client dimensions, cultures and unit profile
are recorded. Display bounds are the dimensions reported by Avalonia's backend;
these are not assumed to be native panel pixels on every platform. Refresh rate
is currently unavailable through the shared backend and is recorded as null,
never estimated from measured frames. No display names, native handles, usernames,
hostnames or personal paths are recorded.

Each scenario/action starts from the same immutable reference. One excluded
priming action and product Undo establish zero undo entries and one equivalent
redo entry. Each subsequent attempt makes the same edit and is followed by
product Undo. The original revision/savepoint is restored, history stays bounded,
and counts of sections, loads and supports do not grow. The runner checks the
state and history before every edit. Pending dispatcher work and a passive
rendered preparation frame settle outside the measured interval. There are no
arbitrary sleeps or forced garbage collections. This is a warmed interaction
protocol, not cold-start performance; reset activity can influence caches and is
therefore applied identically before warm-up and measured attempts.

Attempts, resets and frame observations are sequential. Warm-up, priming and Undo
measurements never enter reference statistics. Discards during measured attempts
count separately. `warmupDiscardedCount` counts excluded warm-up attempts;
`preparationDiscardedCount` counts excluded priming and Undo observations.
Rejected actions, unexpected history changes or missing completion notifications
are technical errors. The runner publishes the JSON only after completing the run.

## JSON schema v1

The root contains `schemaVersion`, independent `protocolVersion`, `durationUnit`,
`spanDraftVersion`, `build`, `startedAtUtc`, `environment`, `configuration` and
`results`. `build.revision` and `build.dirty` describe the source at build time,
not the current checkout at execution time. Both are best effort and individually
nullable if unavailable. Dirty builds are allowed and identified in comparisons.
Running a copied executable does not require Git.

Each result is keyed by `scenarioId` and `actionId` and contains valid/discarded
counts, excluded discard counts, all valid raw `samples`, and statistics for
Preparation, Analysis, Presentation, UI and Total. No project content is exported.
Durations are decimal **milliseconds**, converted exactly from the recorder's
100-ns ticks. The existing Total is retained, not reconstructed from rounded
components. Warm-up values are not exported.

For each phase, sorted valid values define:

- `minimum` and `maximum`: first and last value;
- `median`: middle value, or arithmetic mean of both middle values for even n;
- `p95`: nearest rank, position `ceil(0.95 × n)`, one-based.

There is no outlier removal. With zero valid samples the phase statistics are
null; a discarded sample is never represented by a zero duration. Readers reject
unsupported versions, duplicate/unknown combinations, invalid durations,
inconsistent totals/counts or statistics that disagree with raw samples.

## Compare

For every shared scenario/action, `compare` prints sample counts and all phases'
median and p95: baseline, current, signed absolute delta and relative percentage.
The baseline is the denominator. Zero to zero is 0%; zero to a positive current
value has no defined percentage (`n/a`). Missing samples remain unavailable.
Combinations present in only one run are listed explicitly.

OS, architecture, .NET, scaling, window/display dimensions, available refresh rate,
environment ID, cultures and units are compared. Differences warn that especially
UI/E2E values are not directly equivalent; comparison still continues. Unknown
provenance, dirty builds and different iteration settings are also reported.
Performance changes and environment warnings return exit code 0. Technical errors
or invalid/incompatible files return 1. There are no verdicts or thresholds.

Local files belong under ignored `artifacts/performance` or outside the checkout.
Do not commit a run as a baseline without an explicit decision. CI builds the tool
and runs deterministic contracts; it neither gathers reference timings nor gates
on performance. Native functional acceptance must run on a real desktop.
