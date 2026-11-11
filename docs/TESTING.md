# Testing strategy

SpanDraft is an engineering calculation application. Its test system must protect numerical correctness, persisted project data, application behavior and user-visible output while remaining deterministic, maintainable and efficient enough to run routinely.

The central rule is:

> Test each product contract on the cheapest technical level that can detect its regression reliably.

Runtime optimization must never weaken meaningful engineering, persistence or behavioral coverage. When an expensive test can be replaced by cheaper equivalent coverage, move the contract to the cheaper level instead of simply removing the test.

This document defines durable testing principles. Product behavior and numerical contracts remain defined by the corresponding domain, solver, engineering, persistence and validation documentation.

## 1. Testing responsibilities

Tests are classified by the environment required to prove a contract, not by the production namespace containing the code.

The main responsibilities are:

| Area | Purpose |
| --- | --- |
| Product | Domain logic, numerical algorithms, state, ViewModels, formatting, persistence, libraries and workflows that do not require real Avalonia controls or rendering |
| Desktop | Behavior that genuinely depends on Avalonia controls, styles, layout, focus, input, bindings or the visual tree |
| Rendering | Behavior that genuinely depends on Skia, fonts, bitmap rendering, report pagination or rendered geometry |
| Tooling | Generators, screenshot tools, Store tooling, packaging helpers and other development/release infrastructure |
| Validation | Independent numerical evidence against analytical properties and external/reference implementations |
| Release smoke | Boundaries that can only be proven on built or packaged application artifacts |

These are logical responsibilities. They do not require one test project per category.

Create or split test projects only when a separate execution environment, isolation requirement, dependency boundary or CI policy provides a concrete benefit.

## 2. Product tests

Product tests are the default.

Use them whenever the regression can be detected without creating real Avalonia controls or invoking a rendering backend.

Typical subjects include:

- domain models and invariants
- units and conversions
- materials and material libraries
- section geometry and section properties
- solver behavior
- engineering evaluation
- analysis
- formatting and parsing
- localization logic
- application state and ViewModels
- project workflows using deterministic boundaries
- project persistence and migration
- library codecs and storage behavior

Product tests should normally be deterministic, inexpensive and parallelizable.

A production type living in `SpanDraft.Desktop` does not by itself justify a Desktop test. If its behavior can be verified through a pure state object, ViewModel, layout calculation or formatter, use a Product test.

## 3. Desktop tests

Use Desktop tests only when correctness depends on Avalonia behavior.

Examples include:

- control templates
- styles and theme application
- `Measure` and `Arrange`
- actual visibility
- focus behavior
- keyboard navigation
- pointer interaction
- bindings
- visual-tree composition
- framework-specific control behavior

Desktop tests may use an inert platform environment instead of native operating-system windows when that environment proves the relevant contract faithfully.

Avalonia has thread-affine and process-global state, including dispatcher, application services, themes and platform registrations.

Do not increase in-process parallelism unless isolation has been demonstrated to be safe.

Prefer process- or test-module isolation over unsafe parallel execution of Avalonia tests.

Do not use a real Control merely to verify behavior that can be proven at a lower level.

## 4. Rendering tests

Use Rendering tests only when the rendered result is part of the contract.

Typical subjects include:

- report layout
- report pagination
- Skia rendering
- fonts and glyph shaping
- bitmap output
- clipping
- rendered diagrams
- render-dependent dimensions

Prefer structural and relative invariants.

Good rendering contracts include:

```text
text remains inside its assigned area
columns remain aligned
a diagram does not overlap another report element
content remains above the footer
glyphs are not clipped
```

Avoid fragile absolute pixel assertions unless the exact size or position is itself intentional product behavior.

Golden-image or pixel-snapshot testing should not be introduced merely because output is visual. Use it only where image equality or a defined visual tolerance represents a stable and useful product contract.

Rendering tests may remain internally serial when required by shared rendering or font state.

## 5. Numerical correctness

Numerical correctness requires several independent forms of evidence.

SpanDraft uses three complementary approaches:

| Evidence | Purpose |
| --- | --- |
| Analytical reference cases | Compare results with independently derived closed-form engineering solutions |
| Mathematical invariants | Verify equilibrium, continuity, jumps, symmetry, superposition, scaling and other fundamental properties |
| Independent validation | Compare SpanDraft with independently implemented external or reference solvers |

None of these approaches replaces another.

A passing external comparison does not replace analytical references. Analytical examples do not replace invariants. Tests based on the same implementation assumptions do not replace independent validation.

Do not reduce Solver, Engineering or section-property verification merely to reduce CI runtime.

## 6. Independence of expected results

Expected values must be sufficiently independent from the production path being tested.

Do not calculate an expected value by calling the same production implementation through a different wrapper.

Analytical expected values should come from independently derived equations, fixed reference data or clearly separate reference implementations.

Independent validation must remain isolated from the production implementation except for the explicit boundary required to compare results.

Reference datasets, tools and external solver versions must be reproducibly pinned or versioned where applicable.

The former finite-element solver and the solver-rework prototype are frozen comparison
solvers in `tests/SpanDraft.Tests/SolverReference`. They are test-only, are not developed
further and must not be referenced from `src`. Tests may compare the product solver with
them, but must not derive expected values for the product from the product solver itself.

## 7. Numerical tolerances

Numerical tolerances are part of the test contract.

Use exact equality for discrete and exactly representable contracts where appropriate.

Use tolerances only for quantities affected by floating-point or numerical approximation.

A tolerance must be justified by the numerical scale, algorithm or independent reference accuracy. Do not loosen a tolerance merely to make an unstable or failing test pass.

Prefer shared, domain-specific assertion helpers for quantities such as force, moment, displacement, rotation and section properties.

When a calculation spans very small and very large magnitudes, test relative and absolute behavior intentionally rather than relying on one arbitrary global epsilon.

A tolerance change that materially weakens an engineering assertion should be reviewed as a behavioral test change, not treated as test maintenance.

## 8. Numerical test matrices

Broad matrices are desirable when individual cases are inexpensive.

Examples include:

```text
all supported section shapes × relevant axes
representative load signs and positions
supported unit systems × relevant quantities
extreme but representable numerical scales
all supported project-format versions
```

Do not reduce valuable numerical matrices simply because the total test count is large.

The relevant cost is execution environment and diagnostic value, not the number of discovered test cases.

## 9. Expensive test matrices

Apply stricter rules to Desktop, Rendering and other expensive environments.

Before multiplying a test across several dimensions, determine whether those dimensions genuinely interact.

Avoid Cartesian products such as:

```text
all states × all cultures × all window sizes × all shapes
```

when the individual contracts can be verified independently.

Prefer:

```text
all shapes                 → Product
all parsing cultures       → Product
state combinations         → Product
layout boundaries          → Desktop
representative long labels → Desktop / Rendering
rendering edge cases       → Rendering
```

A Cartesian matrix is justified only when the interaction between its dimensions is itself a meaningful regression risk.

## 10. Workflow and integration coverage

Tests spanning several product components are useful when the interaction itself is the contract.

Do not use a complete application workflow merely to exercise combinatorial business or validation logic.

For example, exhaustive combinations of required and optional form fields should normally be verified at the state or ViewModel level.

Higher-level workflow coverage can then verify representative cases such as:

```text
incomplete input is rejected
complete input creates the intended project
optional data remains optional
the final required value enables the command
cancel leaves committed state unchanged
```

This preserves the contract while avoiding unnecessary repeated execution of expensive infrastructure.

## 11. Persistence and compatibility

`.spandraft` files are durable product contracts.

Compatibility takes precedence over test-suite speed.

Keep immutable fixtures for supported historical formats and verify:

- loading supported historical versions
- migration to the current model
- preservation of engineering-relevant values
- required fields
- invalid input behavior
- current-format round trips

Historical fixtures must not be silently regenerated by normal tests.

Changing or replacing a compatibility fixture is an explicit persistence-format decision.

Apply the same principle to persistent material and section-library formats.

## 12. Localization and culture

Tests depending on culture must set their required culture explicitly and restore global state afterwards.

Formatting and parsing matrices belong primarily in Product tests.

Do not repeat every culture through every Desktop or Rendering scenario unless culture materially affects the contract being tested.

Use representative UI and rendering cases for risks such as:

- longer translated labels
- decimal and grouping separators
- localized report content
- Unicode and diacritics
- layout-sensitive technical notation

## 13. Global state

Tests that modify process-global state must restore it reliably.

Examples include:

- `CultureInfo.CurrentCulture`
- `CultureInfo.CurrentUICulture`
- Avalonia application state
- theme state
- dispatcher state
- platform service registrations
- static caches where mutable

If reliable restoration is not possible, isolate the tests in a dedicated process or test module.

Do not solve global-state conflicts by adding arbitrary delays or relying on execution order.

## 14. Determinism

Normal CI tests must be reproducible.

They must not depend unintentionally on:

- current wall-clock time
- real user profile contents
- machine-specific paths
- previous test execution
- network availability
- mutable external services
- random test ordering

Use fixed clocks, isolated temporary storage, deterministic fakes and explicit cultures.

Randomized or property-based tests are acceptable only when failures provide enough information, including a seed where applicable, to reproduce the exact case.

Normal Product, Desktop and Rendering test suites should not require external network access.

## 15. Flaky tests

Flakiness is a defect.

A test that fails intermittently without a product regression must be investigated and corrected.

Do not normalize flakiness through:

- arbitrary sleeps
- repeated execution until success
- permanently increased timeouts
- weakened assertions
- ignored failures

Retries may be used temporarily for diagnosis or for an unavoidable external boundary, but they must not hide instability in normal deterministic tests.

If a test must temporarily be disabled, the reason and follow-up must be explicit. Disabled tests must not become an indefinite substitute for fixing the underlying problem.

## 16. Waiting and asynchronous behavior

Prefer deterministic synchronization with the state being tested.

Wait for a concrete event, task, state transition or dispatcher condition rather than sleeping for a guessed amount of time.

Timeouts are safety bounds, not synchronization mechanisms.

Tests should fail with diagnostics that identify the unmet condition.

## 17. Tooling tests

Development and release tools are separate from normal application behavior.

Examples include:

- screenshot generation
- Microsoft Store tooling
- generated-resource tooling
- packaging helpers
- publication utilities

Keep Tooling tests separately identifiable and executable so their cost and failures can be distinguished from product behavior.

A Tooling test may remain part of normal CI when it protects a required product or build contract.

Do not remove a Tooling test from normal CI solely because it is slow. First identify the protected risk and whether an equivalent cheaper check exists.

## 18. Generated resources

Generated resources must remain reproducible from checked-in sources.

CI may cache deterministic immutable tooling to avoid rebuilding it repeatedly.

A cache key must include the inputs that determine the generated output or generator binary.

Caches are performance optimizations only. A clean environment must always be able to reproduce and verify the committed result.

## 19. Validation

Independent numerical validation remains separate from normal Product tests.

Its purpose is not merely to increase test count but to provide evidence that does not share SpanDraft's implementation assumptions.

Validation inputs, expected results and comparison rules must remain reproducible.

Changes to solver behavior must not silently redefine their own accepted validation baseline.

Where validation uses a source hash or equivalent provenance mechanism, retain that protection unless it is replaced by a stronger reproducibility mechanism.

## 20. Release smoke tests

Release smoke tests cover boundaries that cannot be fully proven from source-level tests.

Examples include:

- produced executable starts
- installer or package contains the required files
- package metadata is correct
- representative project files can be opened by the published application
- installed application reaches a usable initial state

Keep these tests small and diagnostic.

Do not create a large native GUI end-to-end suite to repeat behavior already covered more reliably at Product, Desktop or Rendering level.

Platform-specific integration must be tested on the relevant operating system when an inert or cross-platform test environment cannot prove the same contract.

## 21. Parallelization

Parallelization is an execution optimization, not a coverage mechanism.

Pure and isolated tests should be allowed to execute in parallel.

Tests sharing process-global state must remain serial or be isolated into separate test processes/modules.

Prefer parallel execution of independent test modules over unsafe parallel execution within Avalonia or rendering environments.

Do not remove serialization solely to improve timing.

First determine why serialization is required and whether the underlying dependency can safely be eliminated.

## 22. CI structure

CI should fail as early and clearly as practical.

Cheap deterministic checks should run before expensive environments where dependencies permit it.

Build production code once per job and reuse the output for tests where practical.

Avoid repeated restore and build work solely to create additional concurrency unless measured benefit justifies the additional runner usage and complexity.

Independent concerns such as Product, Validation and Help may use separate parallel jobs when their isolation improves speed and diagnostics.

Within a Product job, independent test modules may execute concurrently when their state and resource requirements allow it.

A normal pull request and the main branch must run the complete required quality gates. Selective test execution is useful during local development but must not become the only merge/release gate.

## 23. Test-suite performance

Test-suite runtime is an engineering property and should be observed.

When runtime grows materially, measure where the time is spent before optimizing.

Prefer improvements in this order:

```text
remove unnecessary expensive dependencies
move contracts to cheaper test levels
remove redundant high-level combinations
reuse build outputs and deterministic setup
run independent modules concurrently
optimize infrastructure and caches
increase low-level parallelism only when proven safe
```

Do not trade correctness or determinism for a faster headline number.

Evaluate significant CI performance changes over several comparable runs rather than a single runner result.

## 24. Test count and coverage metrics

Test count is not a quality metric.

Line coverage is not a correctness metric.

A smaller suite may provide stronger evidence if it tests clearer contracts. A very large suite may still miss an engineering error.

Coverage tools may be used diagnostically to identify suspiciously untested areas.

Do not introduce a global percentage gate without a concrete quality requirement.

A conservative minimum expected test count may be used to detect accidental test-discovery failures, but it must not become a requirement to preserve an exact historical count.

## 25. Test naming and diagnostics

A test name should describe the contract being protected, not the implementation steps performed.

Prefer names that make the expected behavior and condition understandable from CI output.

When possible, failure messages should include the important inputs and expected/actual engineering quantities.

Large scenario tests should not combine unrelated contracts merely to reduce the number of methods.

If one failure can have many unrelated causes, consider whether the test should be split into clearer concerns.

## 26. Test support code

Share test helpers only when they encode a stable testing concept or remove meaningful duplication.

Do not create large abstract test frameworks merely to make test files appear smaller.

Test helpers must not hide the contract being asserted.

Avoid coupling otherwise independent test modules through a broad shared support library unless the benefit clearly outweighs the dependency.

## 27. Organizing test files

Organize large test areas by coherent behavior.

Do not require one test class per production class.

When a file becomes difficult to navigate or diagnose, split it along meaningful contracts.

For example, report tests may naturally separate into:

```text
layout
pagination
localization
tables
diagrams
rendering
```

File splitting should improve readability and failure ownership without creating artificial abstraction.

## 28. Changing existing tests

Refactoring tests must preserve meaningful coverage.

An existing assertion may be:

- retained at its current level
- moved to a cheaper level
- replaced by stronger equivalent coverage
- removed because the protected product contract intentionally no longer exists
- removed because equivalent coverage is demonstrably redundant

Do not delete assertions merely to reduce test runtime or make a refactor easier.

When performing a large test-architecture migration, avoid mixing unrelated product behavior changes into the same work. This keeps before/after coverage and failures understandable.

## 29. Adding a new test

Before adding a test, answer:

```text
What concrete regression should this test detect?

What is the lowest-cost environment that can prove that contract?

Can it be a Product test?

What Avalonia-specific behavior would require Desktop?

What rendering-specific behavior would require Rendering?

Is it instead a Tooling, Validation or Release boundary?

Which input dimensions actually affect the risk?

Can broad coverage live at a cheaper level while an expensive level uses representative cases?

Does an existing test already protect the same contract?
```

Choose the lowest reliable level.

Do not introduce a Desktop or Rendering test without being able to identify the framework- or rendering-specific behavior that requires it.

## 30. New testing techniques

Do not create new test categories merely because a feature does not fit an existing filename.

New infrastructure is justified by a distinct quality need.

Potential future techniques include:

- property-based testing
- metamorphic testing
- mutation testing
- performance regression testing

Property and metamorphic testing are particularly suitable for mathematical invariants and numerical transformations.

Mutation testing may be useful periodically for critical Solver/Core logic to assess whether existing assertions detect realistic defects.

Performance regression tests should be introduced only when a concrete performance contract exists.

These techniques do not require empty projects or CI jobs in advance.

## 31. Quality objective

The intended testing model is:

```text
broad and inexpensive engineering/product coverage
                     ↓
targeted workflow coverage
                     ↓
small and precise Avalonia coverage
                     ↓
isolated rendering verification

plus independent numerical validation

plus minimal release-level smoke coverage
```

As SpanDraft grows, additional engineering cases, materials, sections, loads and file formats should primarily increase Product and Validation coverage.

Desktop and Rendering runtime should grow only when genuinely new framework- or rendering-dependent behavior is introduced.

The goal is not the smallest test suite and not the highest test count.

The goal is high-confidence evidence with clear failure diagnostics, strong numerical independence, deterministic execution and the lowest reasonable execution cost.