# Flutter testing

## Test priorities

- Test every intentional behavior branch in domain logic and reducers. Do not claim completion from a percentage alone; list intentionally untested defensive or platform branches and explain why they are excluded.
- Widgets and sections should have behavior-focused tests rather than snapshots.
- Screens should test the important user-visible states, failure states, and accessibility behavior.
- Protocol mapping tests belong at the client boundary and should use representative wire fixtures.
- Tests for JSON models must consume representative protocol fixtures and exercise every generated
  mapping direction the model exposes. Models intended to round-trip assert both `fromJson`
  decoding and `toJson` output. Generated source itself is not hand-tested or edited; handwritten
  boundary validation is tested explicitly.
- Model tests must also assert that each model is usable as its corresponding domain entity; entity behavior tests belong beside the entity when the entity contains behavior beyond value declarations.
- For each behavior exposed by a client boundary, tests must cover the applicable accepted and
  rejected messages, recovery requests, correlation IDs, session-generation invalidation, stale
  publication suppression, malformed or unsupported messages, duplicate and stale messages,
  revision gaps, snapshot recovery, and late messages after disposal.

## Test structure

- Mirror the source tree under `test/`.
- Add a dedicated test file for each source unit with behavior or failure logic; group trivial declarations with their owning behavior test.
- Keep ordinary client fixtures in `test/features/<feature>/fixtures/`, close to the feature that
  owns them. Use descriptive `.fixture.dart` names and share builders only within that feature. A
  fixture is a top-level `build<Type>({...})` function with named parameters, each defaulting to one
  representative value; a test that wants the default calls it with no arguments
  (`buildPairingHandshakeEntity()`), and a test that needs one field different overrides only that
  parameter (`buildPairingHandshakeEntity(trusted: false)`). Do not export a fixture as a bare
  `const`/`final` value: a constant cannot be varied per test case without either duplicating the
  whole value under a second name or mutating a shared instance, and the fixture file's own job is to
  make that variation cheap. Cross-side protocol fixtures are an explicit exception and live in
  `protocol/fixtures/`; those canonical fixtures take precedence for contract tests.
- Mock the interface the code depends on; do not mock a concrete implementation when an interface exists.
- Assert exact arguments for delegated calls.
- Test both the action/call path and the matching no-action/no-call path when behavior is conditional.
- Group tests by method or behavior. Do not nest groups, and keep every test inside a group.
- Define `setUp` per group, or in `main` when setup is shared by the whole file.
- Test descriptions name the actual method or action. Use literal Dart syntax for equality
  conditions, including enum values and null checks.
- Primitive properties use two assertions in order: `isA<T>()`, then the exact expected value.

## Layer-specific tests

- Models have an identity group proving they extend the correct entity and a methods group for
  `fromJson`/`toJson` or other mapping methods.
- Use cases have one group named `Usecase [UseCaseName] returns the correct value`. Mock the
  repository interface and test success and every relevant failure pass-through.
- Repositories have an identity group and one behavior group per method, including exact datasource
  calls and symmetric no-call branches.
- Reducers test handled actions and a separate unhandled-action pass-through case.
- Datasource tests cover every distinct catch, guard, explicit throw, and malformed-input branch.

## Test naming

- Use `contains` for structural widget presence, `displays` for visible text, and `calls` or
  `dispatches` for side effects.
- Use “in state” for values inside `AppState`; reserve “in the store” for the `Store` instance.
- Add “with correct parameters” when the test verifies a configured state rather than bare
  presence.

## Widget tests

- Test behavior through stable keys or semantics, not incidental widget types.
- Provide the smallest real app/store context required by the widget.
- Stub every dependency the widget reads during build.
- For every state exposed by the client-state contract, test the corresponding presentation behavior. If a screen intentionally does not render a state, test that it delegates to the approved fallback and document the decision. Cover loading, success, empty, error, disconnected, stale-data, recovering, and disposed-subscription behavior where exposed.

## Accessibility

Accessibility checks must use concrete assertions or approved tooling: semantic labels, minimum tap-target dimensions, text-scaling layout behavior, focus traversal order, and contrast against approved theme colors. A screen change is incomplete if an applicable check is absent or skipped without maintainer approval. Keep these checks in the screen's own test file rather than a global accessibility file.

## What not to test

- Generated code
- Framework behavior
- Pure wiring with no decision or failure path
- Snapshot output
