# SpanDraft

[![CI](https://github.com/Stephan-Entw/SpanDraft/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Stephan-Entw/SpanDraft/actions/workflows/ci.yml)

SpanDraft is an open-source desktop application for intuitive beam analysis. Start with one straight beam, edit its dimensions, and eventually place supports and loads visually to calculate reactions, shear forces, bending moments, deflection, stresses and safety factors.

The repository contains a tested beam domain model, an independently validated
Euler-Bernoulli solver, engineering assessment and an application analysis API:
explicit SI quantities, materials, sections, supports, loads, model validation,
nodal displacements, support reactions, analytical extrema, elastic bending stress
and a yield-strength safety factor. `BeamAnalysis.Analyze(beam)` combines solving
and engineering assessment with structured solver failures. The light Fluent
desktop starts with section/material setup and opens a technical beam editor with
a horizontal toolbar, project information, editable length on the dimension
line, and a compact calculation status bar. The initial 1000 mm beam
has no supports or loads, so calculation is unavailable until support placement
is implemented. Placement tools and the future results action are disabled.
Saving, diagrams and exports are not yet implemented.
Architecture and scope are defined in [the project concept](docs/KONZEPT.md).
Local coordinates, signs and domain validation are documented in [the domain notes](docs/DOMAIN.md).
The numerical formulation, solver API and current limits are documented in [the solver notes](docs/SOLVER.md).
The implemented assessment and its limits are documented in [the engineering notes](docs/ENGINEERING.md).
The application entry point and success/failure contract are documented in [the analysis notes](docs/ANALYSIS.md).
The implemented desktop and binding placement/flyout interaction rules are documented in [the UI specification](docs/UI.md).

## Development

Install the .NET 10 SDK. Windows is the primary target; development on macOS and
Linux remains possible. All projects target `net10.0` with nullable reference types
and the latest stable C# language version supported by the selected SDK.

GitHub Actions automatically runs the Product and Validation gates for pushes to
main and pull requests targeting main. Normal CI requires neither Python nor
external solver installations.

After installing the SDK, reopen your terminal (and restart your IDE if its
terminal cannot find `dotnet`). Verify the installation with `dotnet --version`.
Run the following commands from the repository root:

```sh
dotnet restore SpanDraft.sln
dotnet build SpanDraft.sln --no-restore
dotnet test --solution SpanDraft.sln --no-build --no-restore
dotnet run --project src/SpanDraft.Desktop
```

The repository root contains a solution, not an executable project. A bare
`dotnet run` there reports that no runnable project was found. Use the `--project`
command above, or run `dotnet run` from `src/SpanDraft.Desktop`.

## Projects

| Project | Purpose | Project references |
| --- | --- | --- |
| `src/SpanDraft.Core` | Beam domain model and validation | None |
| `src/SpanDraft.Solver` | Euler-Bernoulli beam solver; Math.NET Numerics | Core |
| `src/SpanDraft.Engineering` | Elastic bending assessment of an existing solution | Core, Solver |
| `src/SpanDraft.Analysis` | Application entry point; solver and engineering orchestration | Core, Solver, Engineering |
| `src/SpanDraft.Desktop` | Avalonia setup and sketch-first beam editor | Core, Analysis |
| `src/SpanDraft.Reporting` | Future PDF and XLSX exports | Core |
| `tests/SpanDraft.Tests` | xUnit domain, solver, engineering, analysis, desktop state and dependency tests | Core, Solver, Engineering, Analysis, Desktop |

The reporting library contains no implementation yet. It will consume Analysis
results; its Analysis reference will be added when reporting is implemented.
Core does not depend on
Avalonia or Math.NET; Solver and its tests use Math.NET for linear algebra.
The desktop application uses
Avalonia's standard Fluent theme, explicitly set to Light. English default and
German UI strings are stored in `Resources/Strings.resx` and
`Resources/Strings.de.resx` inside the Desktop project. Text, length parsing and
formatting use the current UI culture. Inline length editing is transactional:
Enter commits, Escape cancels, and invalid input never changes the project.

The initial square tube is an idealized sharp-cornered geometric Core template,
not a normative or manufacturer profile. S235JR (E = 210 GPa, Re = 235 MPa)
is a provisional material template, not a normative material database.

Tests use xUnit with Microsoft Testing Platform, selected in `global.json`.

Independent solver acceptance lives in the separate
[validation solution](validation/README.md); it is not included in `SpanDraft.sln`
and adds no Python requirement to the product. Engineering and Analysis are not
part of this separate solution. Current evidence and the passing
acceptance gate are documented in [VALIDATION.md](docs/VALIDATION.md).

## Deferred decisions

As described in the concept, the model file format, additional UI languages and
concrete PDF/XLSX libraries remain open. One-shot placement with transactional
object flyouts is specified for the next editor milestone. Settings, persistence,
theme switching and profile libraries remain deferred. No reporting libraries are installed.
