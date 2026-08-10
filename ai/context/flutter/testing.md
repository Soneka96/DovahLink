# Flutter testing

## Test priorities

- Domain use cases and reducers/selectors, once introduced, should have complete branch coverage.
- Widgets and sections should have behavior-focused tests rather than snapshots.
- Screens should test the important user-visible states, failure states, and accessibility behavior.
- Protocol mapping tests belong at the client boundary and should use representative wire fixtures.

## Test structure

- Mirror the source tree under `test/`.
- Use one test file per source file once implementation begins.
- Keep fixtures close to the feature that owns them.
- Mock the interface the code depends on; do not mock a concrete implementation when an interface exists.
- Assert exact arguments for delegated calls.
- Test both the action/call path and the matching no-action/no-call path when behavior is conditional.

## Widget tests

- Test behavior through stable keys or semantics, not incidental widget types.
- Provide the smallest real app/store context required by the widget.
- Stub every dependency the widget reads during build.
- Cover loading, success, empty, error, disconnected, and stale-data states when the widget renders them.

## Accessibility

Every screen test should eventually cover contrast, tap-target size, semantic labels, text scaling, and focus traversal. Keep these checks in the screen's own test file rather than a global accessibility file.

## What not to test

- Generated code
- Framework behavior
- Pure wiring with no decision or failure path
- Snapshot output
