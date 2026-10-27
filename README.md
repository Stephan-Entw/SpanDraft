# SpanDraft

[![CI](https://github.com/Stephan-Entw/SpanDraft/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Stephan-Entw/SpanDraft/actions/workflows/ci.yml)

SpanDraft is an open-source desktop application for intuitive beam analysis.

It is designed for mechanical-engineering workflows between hand/Excel
calculations and large general-purpose FEM systems: define one straight beam,
place supports and loads directly in a technical sketch, edit exact dimensions
and inspect the resulting structural response.

SpanDraft has an initial public release, `v0.1.0`, and remains under active
development. This README describes the current checkout, including changes made
after that release.

## Current capabilities

The current desktop application supports:

- one straight beam with constant material and cross-section
- parametric and manual cross-section setup
- built-in and custom material libraries
- reusable custom section presets
- explicit bending-axis selection
- fixed, pinned and roller supports
- point forces and point moments
- constant distributed loads
- direct graphical placement and editing
- exact numeric positioning
- schematic layout for dense models without changing physical coordinates
- undo and redo
- versioned `.spandraft` project files
- crash recovery
- project information with calculation title, author, optional project title and reference
- automatic analysis after committed model changes
- compact display of maximum deflection, bending moment, bending stress and
  safety factor against the material's yield or proof strength
- calculation report preview, PDF export and Windows printing
- German and English interface text, independent regional number formatting and
  configurable technical units and display precision

The numerical core provides support reactions and continuous analytical fields
for displacement, rotation, axial force, shear force and bending moment.

The desktop displays support reactions and live diagrams for displacement, shear
force and bending moment. Reports capture confirmed project inputs, existing
results and calculation assumptions.

The published Windows x64 release uses an Inno Setup installer. The current
checkout also provides an independent Microsoft Store/MSIX packaging path; see
[RELEASING.md](docs/RELEASING.md) for both workflows and their prerequisites.

## Engineering scope

SpanDraft currently uses a linear-elastic Euler-Bernoulli beam formulation with
small displacements and rotations.

The current model is intentionally focused on small single-beam problems.
It is not a general-purpose FEM system and does not provide nonlinear analysis,
buckling, fatigue, contact, 2D/3D frames or normative design verification.

The displayed bending stress and safety factor are simple elastic assessments,
not a code-based structural verification.

Built-in material properties are curated SpanDraft reference values assembled
from publicly available technical information and cross-checked against multiple
sources. They do not replace material standards, manufacturer specifications or
material certificates.

The solver has been independently validated against analytical references,
IndeterminateBeam and PyCBA. See
[VALIDATION.md](docs/VALIDATION.md) for the validation scope and evidence.

## Development

Requirements:

- .NET 10 SDK

Windows is the primary target. Development on macOS and Linux is supported.

From the repository root:

```sh
dotnet restore SpanDraft.sln
dotnet build SpanDraft.sln --no-restore
dotnet test --solution SpanDraft.sln --no-build --no-restore
dotnet run --project src/SpanDraft.Desktop
```

GitHub Actions runs the product, independent validation and [offline help](help/README.md)
gates on pushes to all branches and pull requests targeting `main`.

On macOS, Desktop builds also create a development `SpanDraft.app` bundle inside
the corresponding build output. It uses the locally installed .NET runtime and
is intended for development only.

### Third-party notices

After changing Desktop dependencies, regenerate and commit `THIRD_PARTY_NOTICES.txt`:

```sh
dotnet run --file scripts/generate-third-party-notices.cs
```

Verify the committed file with:

```sh
dotnet run --file scripts/generate-third-party-notices.cs -- --check
```

Desktop builds and publishes ship the committed file beside the application.

## Architecture

The application is split into small layers with explicit responsibilities:

| Project | Responsibility |
| --- | --- |
| `SpanDraft.Core` | domain model, units and validation |
| `SpanDraft.Solver` | Euler-Bernoulli beam mechanics |
| `SpanDraft.Engineering` | elastic engineering assessment |
| `SpanDraft.Analysis` | application-facing analysis orchestration |
| `SpanDraft.Desktop` | Avalonia desktop application |
| `SpanDraft.Reporting` | frozen A4 report composition for preview, PDF export and Windows printing |
| `SpanDraft.Tests` | product and regression tests |

Core is independent of UI, numerical libraries, persistence and reporting. Desktop consumes the analysis
API rather than implementing mechanical formulas itself.

## Documentation

- [KONZEPT.md](docs/KONZEPT.md) — product vision, scope and roadmap
- [DOMAIN.md](docs/DOMAIN.md) — domain model, units and sign conventions
- [SOLVER.md](docs/SOLVER.md) — numerical formulation and solver contract
- [ENGINEERING.md](docs/ENGINEERING.md) — engineering assessment and limits
- [ANALYSIS.md](docs/ANALYSIS.md) — application analysis API
- [UI.md](docs/UI.md) — rationale for editor state and interaction
- [PROJECT_FORMAT.md](docs/PROJECT_FORMAT.md) — `.spandraft` file format
- [LIBRARIES.md](docs/LIBRARIES.md) — built-in materials and local reusable libraries
- [VALIDATION.md](docs/VALIDATION.md) — independent solver validation
- [RELEASING.md](docs/RELEASING.md) — release and packaging workflows

## License

SpanDraft is licensed under the GNU General Public License v3.0.
See [LICENSE](LICENSE).
Third-party package notices are listed in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
