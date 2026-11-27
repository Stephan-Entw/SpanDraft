# Project guidelines

## Sources of truth

- `docs/KONZEPT.md` defines product vision, scope and roadmap.
- Durable technical contracts belong in their dedicated documents:
  `docs/DOMAIN.md`, `docs/SOLVER.md`, `docs/ENGINEERING.md`, `docs/ANALYSIS.md`,
  `docs/PROJECT_FORMAT.md`, `docs/LIBRARIES.md` and `docs/VALIDATION.md`.
- Read only the documentation relevant to the current task.
- `docs/UI.md` contains rationale that code and tests do not adequately explain.
  Do not mirror routine control, menu, dialog or layout changes there.
- `docs/TESTING.md` defines the testing strategy. Read it before changing test
  structure, coverage, tolerances, isolation or test levels.
- Do not duplicate implementation details, milestone histories or changing test
  counts across documentation. Prefer code and tests for implementation details.

## Architecture and UI

- Keep Core independent of UI, numerical libraries, persistence and reporting.
- For UI changes, follow the existing compact Windows-desktop/Fluent design.
  Inspect and reuse established controls, design tokens and layout patterns,
  especially in the section editor and material library. Avoid web-app styling
  such as dashboard cards, oversized forms or responsive stacking.
- Prefer structural and relative UI layout invariants such as alignment,
  containment, equal sizing and absence of clipping or overflow. Use absolute
  bounds only when the size itself is an intentional product constraint.

## Agent workflow

- Prefer repository-provided agent commands over ad-hoc equivalent build, test,
  generation or launch commands.
- On macOS agent environments use:
  - `./scripts/agent build`
  - `./scripts/agent test [module] [-class ... | -method ...]`
  - `./scripts/agent help [--regenerate-assets]`
  - `./scripts/agent icons [--check]`
  - `./scripts/agent ui-launch`
  - `./scripts/agent verify <step>` (`validation`, `diff`, `ignored-sources`,
    `notices` or `help-assets`)
- Do not probe equivalent raw `dotnet` commands first when a repository command
  covers the operation. Outside agent environments, use the standard README and
  CI workflows.
- Keep repository exploration targeted: search first, then read only the files or
  ranges needed for the task. Avoid dumping multiple complete files, generated
  artifacts or persisted tool outputs unless their full contents are required.
- When `graphify-out/graph.json` exists, use Graphify first for codebase
  navigation, dependency/impact analysis and locating relevant tests before broad
  repository searches. Reuse the existing graph; do not rebuild or rescan it routinely.
- For uncommitted or newly modified files, verify the current source directly rather
  than relying on Graphify, because the graph may not yet contain those changes.
- Prefer the agent's search/read tools; otherwise use `rg`. Quote glob arguments
  under zsh and use macOS/BSD-compatible shell syntax.
- Request host access directly for checks the sandbox is known to block (IPC,
  process inspection, GUI; see `docs/TESTING.md`, "Execution environment").
  Do not start runs that are known to fail in the sandbox. Keep other checks
  sandboxed.
- After a sandbox, permission, IPC, cache or display-state failure, identify the
  failure class before retrying; never retry command variants blindly. Report
  missing access as blocked, never as passed, and never change or skip tests to
  work around the environment.
- For long-running commands, use the agent runtime's background/wait mechanism
  rather than repeated `sleep` polling.
- Follow locally started checks until the process exits and evaluate its exit
  code and result before closing the task. Do not wait for CI after a push.
- Native macOS UI checks require the interactive WindowServer/display session.
  Run `./scripts/agent ui-launch` directly with the agent runtime's escalated
  execution, outside the sandbox. Request approval for that command if needed
  and do not try it in the sandbox first. After the launch verifies the process
  against the current checkout and build, continue with the planned UI checks.

## Generated resources

- SVG files under `src/SpanDraft.Desktop/Assets/Icons/Toolbar` and `Fluent` are
  design sources. After changing them, use `./scripts/agent icons`; use
  `./scripts/agent icons --check` to verify committed generated resources.
- Do not edit generated resources when a source or generator exists.

## Testing

- Follow `docs/TESTING.md` and test each contract on the cheapest reliable level.
- Verification levels: during development run only the relevant classes or
  methods with a current build; at task completion run risk-based checks of the
  affected modules and integration contracts plus `verify diff` and
  `verify ignored-sources` (documentation-only changes: diff check only), and
  the conditional checks in `docs/TESTING.md` (notices, icons, help assets,
  independent solver validation). The quality gate is the complete `CI`.
- No full local verify or manual platform switch is required for normal tasks.
  GitHub CI provides the complete cross-platform quality gate.
- Report status as locally verified, CI pending, fully accepted or blocked.
  Merge and release require fully accepted.

## Test integrity

- Preserve existing test contracts or replace them traceably with equivalent
  or stronger checks.
- Removing, weakening or skipping a contract, or changing numerical expected
  values, tolerances or independent reference data, requires explicit approval.
- Back critical new or corrected regression tests with a targeted negative
  control.
- Report changes to quality gates and shared test infrastructure explicitly.

## Commits

- Commit messages follow Conventional Commits: `<type>: <short description>`.
- Use types such as `feat:`, `fix:`, `docs:`, `test:`, `refactor:` or `chore:`;
  write the description in English.
