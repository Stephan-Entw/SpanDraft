# Solver validation (development only)

`SpanDraft.sln` remains the product solution. The separate
`validation/SpanDraft.Validation.sln` contains the validation library, runner,
and xUnit acceptance tests, with project references to Core and
Solver. No validation project is included in the product solution.

Both solutions build and test independently, without Python:

```sh
# From the repository root: normal product build and existing tests.
dotnet restore SpanDraft.sln
dotnet build SpanDraft.sln --configuration Release --no-restore
dotnet test --solution SpanDraft.sln --configuration Release --no-build --no-restore \
  --max-parallel-test-modules 2 --report-xunit-xml --results-directory artifacts/test-results

# Separate validation build and tests against checked-in golden files.
dotnet restore validation/SpanDraft.Validation.sln
dotnet build validation/SpanDraft.Validation.sln --configuration Release --no-restore
dotnet test --solution validation/SpanDraft.Validation.sln --configuration Release --no-build --no-restore
```

The full acceptance gate requires **both** test runs and the acceptance comparison:

```sh
dotnet run --project validation/dotnet/SpanDraft.Validation.Runner --configuration Release --no-build -- compare \
  --report validation/results/local-run/acceptance.json
```

The M2 boundary/exact tests run in the complete Product suite. They are separate
from `compare`, which checks the 18 cases and 45 golden references, including
external/analytical comparisons, physical checks and metamorphic relations.
Use a fresh `compare` without `--actual` for product acceptance so all results
come from the current public solver in that run.

Python and external solvers are only needed for deliberate reference regeneration,
never for restore, build, .NET tests, acceptance against golden files or the
desktop application. See [VALIDATION.md](../docs/VALIDATION.md) for the reference
methods, conventions, acceptance matrix and limitations.

The current M4 product acceptance is recorded separately in
[acceptance-m4.json](results/acceptance-m4.json), with its solver source hash and
public entry point. [VALIDATION.md](../docs/VALIDATION.md#4-finale-m4-produktabnahme)
records the actual local platform/build/test evidence. The historical
[acceptance.json](results/acceptance.json) from 2026-10-03 remains unchanged.
Neither dated report is overwritten by routine runs or used as the CI baseline.
CI pins its expected hash separately in `.github/workflows/ci.yml` and compares
it with `Acceptance.SolverHash()` from a fresh acceptance run. At M4 closure the
pinned baseline and M4 report hashes must match exactly. Later hashed-source
changes require new acceptance evidence and an explicit baseline update, while
the M4 report remains historical evidence.

Both acceptances are **PASS**: all 18 cases pass. Only V14's six
IndeterminateBeam extremum-position comparisons are **NOT APPLICABLE**, because
its native roundoff fields cannot establish the physical zero-field location
set. All six extremum values remain mandatory and pass. See
[VALIDATION.md](../docs/VALIDATION.md) for the full evidence and comparison counts.

The reviewed V14 investigation is recorded in
[v14-native-fields.json](results/v14-native-fields.json). Its
[standalone script](investigations/v14_native_fields.py) reads native solver
expressions and verifies reproduction of the V14 golden. Repeat that investigation
in a separate checkout: the script writes to the fixed, versioned evidence path
and has no configurable report output.

## Reference regeneration

Use CPython **3.9.6**. The lock includes hashes for the fixed versions' published
artifacts. No package is automatically upgraded. Pandas is necessary because
IndeterminateBeam 2.4.0 imports it unconditionally, despite omitting it from its
wheel dependencies; no pandas analysis/reporting code is used by these scripts.

```sh
python3 -m venv validation/python/.venv
validation/python/.venv/bin/python -m pip install pip==25.2
validation/python/.venv/bin/python -m pip install --require-hashes \
  -r validation/python/requirements.txt
validation/python/.venv/bin/python validation/python/generate_references.py \
  --output validation/results/local-run/references

dotnet run --project validation/dotnet/SpanDraft.Validation.Runner -- export \
  --output validation/results/local-run/spandraft.json
dotnet run --project validation/dotnet/SpanDraft.Validation.Runner -- compare \
  --references validation/results/local-run/references \
  --actual validation/results/local-run/spandraft.json \
  --report validation/results/local-run/acceptance.json
```

Use a new reference directory for each regeneration; replace `local-run` consistently
in the commands when repeating it. Generated files under `results/local-run/` are
ignored. The versioned reports in `results/` record dated acceptances and
are retained unchanged. Generation refuses to overwrite reference files or write into
`references/`. Review provenance, coverage, convergence and comparison reports
before explicitly promoting candidates:

```sh
validation/python/.venv/bin/python validation/python/promote_references.py \
  --from validation/results/local-run/references --reviewed
git diff -- validation/references
```

Promotion is never called by tests or CI and makes no commit. FAIL/PARTIAL
references remain evidence rather than disappearing from the catalog. Unresolved
applicable requirements fail acceptance; the explicitly investigated V14
position limitations are classified as N/A in the comparison layer.

To verify a regeneration independently against the golden files, ignoring only
generation timestamps, and calibrate both external solvers against all nine
analytical cases:

```sh
validation/python/.venv/bin/python validation/python/audit_references.py \
  --directory validation/results/local-run/references \
  --against validation/references \
  --report validation/results/local-run/reproduction.json
```

The generator returns **1** for the full catalog because it reproduces V14's
historical PARTIAL extraction metadata; all 45 files are still generated. The
audit returns **0** when calibration and reproduction match. In the documented
acceptance, the comparison CLI and both complete .NET test runs returned **0**.
V14's legacy extraction status is resolved only for its six documented position
limitations; other incomplete extraction requirements remain PARTIAL.

## Layout

- `cases/`: 18 neutral versioned SI input definitions; source of truth for every adapter.
- `references/`: 36 external and 9 analytical golden files, with provenance and input SHA-256.
- `python/`: isolated external adapters, analytical provider and explicit generation/promotion commands.
- `dotnet/`: public-solver comparisons, physical checks, metamorphic tests and CLI.
- `results/`: reviewed acceptance evidence; transient candidate/export/run files are ignored.

The runner's `export` and `compare` commands accept `--output` and `--report`
respectively; `compare` also accepts `--references` and `--actual`. It writes a
structured report and returns nonzero for failed/incomplete comparisons.
Since M3 the public `EulerBernoulliBeamSolver` is the only entry and runs the
kernel; the former `--kernel` switch is gone. Exports and reports identify the
entry point, and `compare --actual` rejects exports of the retired internal
entry. The M2 coverage audit and exact-test mapping are in
[M2_ACCEPTANCE.md](investigations/solver-rework/M2_ACCEPTANCE.md).
Relative deviation is undefined (JSON null) for a zero reference. Timestamps do
not constitute a numerical difference; input hashes, versions and coverage do.
