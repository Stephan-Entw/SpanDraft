# SpanDraft

[![CI](https://github.com/Stephan-Entw/SpanDraft/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Stephan-Entw/SpanDraft/actions/workflows/ci.yml)

SpanDraft is an open-source desktop application for intuitive beam analysis.

It is designed for mechanical-engineering workflows between hand/Excel
calculations and large general-purpose FEM systems: define one straight beam,
place supports and loads directly in a technical sketch, edit exact dimensions
and inspect the resulting structural response.

SpanDraft is under active development and has not reached a stable release yet.

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
- automatic analysis after committed model changes
- compact display of maximum deflection, bending moment, bending stress and
  yield-strength safety factor

The numerical core provides support reactions and continuous analytical fields
for displacement, rotation, axial force, shear force and bending moment.

The desktop displays live diagrams for displacement, shear force and bending moment.
A dedicated results view and PDF/XLSX export are planned.

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

### Native icon resources

The SVGs in `src/SpanDraft.Desktop/Assets/Icons/Toolbar` and `Fluent` are design
sources; they are not embedded in the application. After adding or changing a
source, regenerate and commit the native dictionaries in `Assets/Icons/Generated`:

```sh
dotnet run --file scripts/update-icons.cs
dotnet run --file scripts/update-icons.cs -- --check
```

The explicit generator downloads and builds unmodified
[SvgToXaml at commit `1beb52777c564c218f460ee0510f73cba21c6d69`](https://github.com/wieslawsoltes/SvgToXaml/tree/1beb52777c564c218f460ee0510f73cba21c6d69),
verifies its source archive checksum and caches it under `.artifacts/icon-converter`.
The published `svgxaml` 11.2.0 tool loses solid brushes and pens; this upstream
revision fixes that issue. The .NET 10 SDK is sufficient; the converter rolls
forward to the installed runtime. First generation needs GitHub and NuGet access.
Regular builds use the committed AXAML and require neither the converter nor SVG
parsing. Fluent sources must convert to one opaque solid filled geometry; other
artwork needs an explicit representation review. Preserve the Fluent license and
NOTICE files. Compare rendered icons before committing source or generator changes.

### Third-party notices

After changing Desktop dependencies, regenerate the committed notices from the
repository root using the .NET 10 SDK:

```sh
dotnet run --file scripts/generate-third-party-notices.cs
```

The script restores the pinned local ThirdLicense tool and the Desktop project,
then updates `THIRD_PARTY_NOTICES.txt` with direct and transitive runtime packages
for all desktop platforms. Test, validation and build-only packages are excluded.
The notices contain ThirdLicense's package metadata and license references.
Generation requires access to the configured NuGet feeds and runs only when
explicitly requested; regular builds do not generate notices.

To verify the committed file without changing it, use:

```sh
dotnet run --file scripts/generate-third-party-notices.cs -- --check
```

The check returns a nonzero exit code if generation fails or the file is missing
or outdated. Commit updated notices alongside dependency changes.

## Architecture

The application is split into small layers with explicit responsibilities:

| Project | Responsibility |
| --- | --- |
| `SpanDraft.Core` | domain model, units and validation |
| `SpanDraft.Solver` | Euler-Bernoulli beam mechanics |
| `SpanDraft.Engineering` | elastic engineering assessment |
| `SpanDraft.Analysis` | application-facing analysis orchestration |
| `SpanDraft.Desktop` | Avalonia desktop application |
| `SpanDraft.Reporting` | reserved for future reports and exports |
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
- [VALIDATION.md](docs/VALIDATION.md) — independent solver validation
- [RELEASING.md](docs/RELEASING.md) — intended release process

## License

SpanDraft is licensed under the GNU General Public License v3.0.
See [LICENSE](LICENSE).
Third-party package notices are listed in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
