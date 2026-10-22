# Project guidelines

- `docs/KONZEPT.md` defines product vision, scope and roadmap.
- Durable technical contracts belong in their dedicated documents:
  `docs/DOMAIN.md`, `docs/SOLVER.md`, `docs/ENGINEERING.md`, `docs/ANALYSIS.md`,
  `docs/PROJECT_FORMAT.md`, `docs/LIBRARIES.md` and `docs/VALIDATION.md`.
- Keep `docs/UI.md` limited to rationale that code and tests do not adequately
  explain. Do not mirror the current interface or require documentation updates
  for changes to controls, menus, dialogs or layout.
- Do not duplicate implementation details, milestone histories or changing test
  counts across documentation. Prefer code and tests for implementation details.
- For UI changes, follow the existing compact Windows-desktop/Fluent design,
  especially the section editor and material library. Inspect and reuse their
  controls, design tokens and layout patterns; avoid web-app styling such as
  dashboard cards, oversized forms or responsive stacking.
- UI layout tests should prefer structural and relative invariants such as
  alignment, containment, equal sizing and absence of clipping or overflow over
  narrow absolute pixel ranges. Use absolute bounds only when the size itself is
  an intentional product constraint.
- SVG files under `src/SpanDraft.Desktop/Assets/Icons/Toolbar` and `Fluent` are
  design sources. After changes, run `dotnet run --file scripts/update-icons.cs`;
  add `-- --check` to verify the committed resources.
- Keep Core independent of UI, numerical libraries, persistence and reporting.
- Section-definition draft invariant: custom and library display names are
  form-local. Switching the section shape must never carry a name into another
  FormDraft; returning to a form restores its own name. See `docs/UI.md`.
- Commit messages follow Conventional Commits: `<type>: <short description>`.
  Use types such as `feat:`, `fix:`, `docs:`, `test:`, `refactor:` or `chore:`;
  write the description in English.
  Example: `feat: add beam domain model with SI units and validation`.
