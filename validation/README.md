# Solver validation (development only)

`SpanDraft.sln` remains the product solution. The separate
`validation/SpanDraft.Validation.sln` contains the validation library, runner,
and xUnit acceptance tests, with project references to the unchanged Core and
Solver. No validation project is included in the product solution.

Both solutions build and test independently, without Python:

```sh
# From the repository root: normal product build and existing tests.
dotnet restore SpanDraft.sln
dotnet build SpanDraft.sln --no-restore
dotnet test --solution SpanDraft.sln --no-build --no-restore

# Separate validation build and tests against checked-in golden files.
dotnet restore validation/SpanDraft.Validation.sln
dotnet build validation/SpanDraft.Validation.sln --no-restore
dotnet test --solution validation/SpanDraft.Validation.sln --no-build --no-restore
```

The full acceptance gate requires **both** test runs. Python and external solvers
are only needed for deliberate reference regeneration, never for restore, build,
.NET tests or the desktop application. See [VALIDATION.md](../docs/VALIDATION.md)
for the reference methods, conventions, acceptance matrix and limitations.

Current acceptance is **PASS**: all 18 cases pass. Only V14's six
IndeterminateBeam extremum-position comparisons are **NOT APPLICABLE**, because
its native roundoff fields cannot establish the physical zero-field location
set. All six extremum values remain mandatory and pass. Both solutions build;
969 product tests and 195 validation tests pass (1164 total, none skipped).
The acceptance runner performs 13,472 applicable comparisons; six N/A positions
are reported separately. No product source, tolerance or golden file changed.

The native V14 field investigation can be reproduced with:

```sh
validation/python/.venv/bin/python validation/investigations/v14_native_fields.py
```

It reads native solver expressions and verifies exact reproduction of the
unchanged V14 golden; evidence is in `results/v14-native-fields.json`.

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
  --output validation/results/candidate-references

dotnet run --project validation/dotnet/SpanDraft.Validation.Runner -- export
dotnet run --project validation/dotnet/SpanDraft.Validation.Runner -- compare \
  --references validation/results/candidate-references
```

Candidate directories must be new/empty: generation refuses to overwrite files
and refuses to write into `references/`. Review provenance, coverage, convergence
and comparison reports before explicitly promoting candidates:

```sh
validation/python/.venv/bin/python validation/python/promote_references.py \
  --from validation/results/candidate-references --reviewed
git diff -- validation/references
```

Promotion is never called by tests or CI and makes no commit. FAIL/PARTIAL
references remain evidence rather than disappearing from the catalog. Unresolved
applicable requirements fail acceptance; the explicitly investigated V14
position limitations are classified as N/A in the comparison layer. Regenerate into a different directory for subsequent runs.

To verify a regeneration independently against the golden files, ignoring only
generation timestamps, and calibrate both external solvers against all nine
analytical cases:

```sh
validation/python/.venv/bin/python validation/python/audit_references.py \
  --directory validation/results/candidate-references \
  --against validation/references --report validation/results/reproduction.json
```

The unchanged generator returns **1** because it reproduces V14's historical
PARTIAL extraction metadata. All 45 files are still generated. The audit returns
**0** when calibration and reproduction match. The comparison CLI and both
complete .NET test runs return **0**. V14's legacy extraction status is resolved
only for its six documented position limitations; other incomplete extraction
requirements remain PARTIAL. The golden payload and adapter provenance remain
unchanged.

## Layout

- `cases/`: 18 neutral versioned SI input definitions; source of truth for every adapter.
- `references/`: 36 external and 9 analytical golden files, with provenance and input SHA-256.
- `python/`: isolated external adapters, analytical provider and explicit generation/promotion commands.
- `dotnet/`: public-API-only comparisons, physical checks, metamorphic tests and CLI.
- `results/`: reviewed acceptance evidence; transient candidate/export/run files are ignored.

The runner's `export` and `compare` commands accept `--output` and `--report`
respectively; `compare` also accepts `--references` and `--actual`. It writes a
structured report and returns nonzero for failed/incomplete comparisons.
Relative deviation is undefined (JSON null) for a zero reference. Timestamps do
not constitute a numerical difference; input hashes, versions and coverage do.
