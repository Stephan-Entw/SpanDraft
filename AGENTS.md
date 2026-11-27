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
  - `./scripts/agent verify`
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
- After a sandbox, permission, IPC, cache or display-state failure, do not retry
  command variants blindly. Use the documented repository path or identify the
  failure class before retrying.
- For long-running commands, use the agent runtime's background/wait mechanism
  rather than repeated `sleep` polling.
- Follow long-running checks until the process exits and evaluate its exit code
  and result before closing the task. A promise to report later is insufficient.
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
- During iteration, run the narrowest relevant tests.
- Do not weaken analytical, numerical or compatibility coverage to make tests
  pass or improve runtime.
- Use `./scripts/agent verify` for broad changes or final local verification.
  Independent solver validation remains a separate workflow and is run only when
  required by the affected contract or validation documentation.

## Commits

- Commit messages follow Conventional Commits: `<type>: <short description>`.
- Use types such as `feat:`, `fix:`, `docs:`, `test:`, `refactor:` or `chore:`;
  write the description in English.