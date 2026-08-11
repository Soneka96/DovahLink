# Flutter testing

## Test priorities

- Test every intentional behavior branch in domain logic and reducers. Do not claim completion from a percentage alone; list intentionally untested defensive or platform branches and explain why they are excluded.
- Widgets and sections should have behavior-focused tests rather than snapshots.
- Screens should test the important user-visible states, failure states, and accessibility behavior.
- Protocol mapping tests belong at the client boundary and should use representative wire fixtures.
- Tests for JSON models must consume representative protocol fixtures and assert both generated `fromJson` decoding and generated `toJson` round-tripping. Generated source itself is not hand-tested or edited; handwritten boundary validation is tested explicitly.
- Model tests must also assert that each model is usable as its corresponding domain entity; entity behavior tests belong beside the entity when the entity contains behavior beyond value declarations.
- Client-boundary tests must assert both accepted and rejected messages, emitted recovery requests, correlation IDs, session-generation invalidation, suppression of stale publications, malformed or unsupported protocol messages, duplicate and stale messages, revision gaps, snapshot recovery, and late messages after disposal.

## Test structure

- Mirror the source tree under `test/`.
- Add a dedicated test file for each source unit with behavior or failure logic; group trivial declarations with their owning behavior test.
- Keep ordinary client fixtures close to the feature that owns them. Cross-side protocol fixtures are an explicit exception and live in the shared protocol area; those fixtures take precedence for contract tests.
- Mock the interface the code depends on; do not mock a concrete implementation when an interface exists.
- Assert exact arguments for delegated calls.
- Test both the action/call path and the matching no-action/no-call path when behavior is conditional.

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
