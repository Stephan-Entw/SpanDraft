# SpanDraft
SpanDraft is an open-source desktop application for intuitive beam analysis. Draw and dimension beams, place supports and loads visually, and calculate reactions, shear forces, bending moments, deflection, stresses and safety factors.

The repository contains a tested beam domain model and an Euler-Bernoulli solver:
explicit SI quantities, materials, sections, supports, loads, model validation,
nodal displacements and support reactions. The desktop window is still empty.
Diagrams, stresses, interactive editing and exports are not yet implemented.
Architecture and scope are defined in [the project concept](docs/KONZEPT.md).
Local coordinates, signs and domain validation are documented in [the domain notes](docs/DOMAIN.md).
The numerical formulation, solver API and current limits are documented in [the solver notes](docs/SOLVER.md).

## Development

Install the .NET 10 SDK. Windows is the primary target; development on macOS and
Linux remains possible. All projects target `net10.0` with nullable reference types
and the latest stable C# language version supported by the selected SDK.

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
| `src/SpanDraft.Desktop` | Minimal Avalonia desktop application | Core, Solver |
| `src/SpanDraft.Reporting` | Future PDF and XLSX exports | Core |
| `tests/SpanDraft.Tests` | xUnit domain, analytical solver and dependency tests | Core, Solver |

The reporting library contains no implementation yet. Core does not depend on
Avalonia or Math.NET; Solver and its tests use Math.NET for linear algebra.
The desktop application uses
Avalonia's standard Fluent theme. UI strings
are stored in `src/SpanDraft.Desktop/Resources/Strings.resx` and resolved using the
current UI culture.

Tests use xUnit with Microsoft Testing Platform, selected in `global.json`.

## Deferred decisions

As described in the concept, the model file format, supported UI languages and
concrete PDF/XLSX libraries remain open. UI composition and interaction patterns
will be chosen when the editor is implemented. No reporting libraries are installed.
