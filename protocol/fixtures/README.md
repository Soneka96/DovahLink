# Protocol fixtures

Fixtures in this directory are the language-neutral source of truth for cross-side contract tests.
They are organized by protocol feature so related examples remain easy to find as coverage grows.

```text
fixtures/
  connection/
  capabilities/
  subscriptions/
  state/
    character/
  errors/
```

## Rules

- Store one complete JSON message per fixture file.
- Organize fixtures by protocol feature; use a state-area subdirectory under `state/` when one exists.
- The directory path and filename together must identify the message type and scenario.
- Expected decoded values belong in the consuming test, not in a second fixture metadata format.
- Flutter and SKSE tests consume the same fixture files; they must not recreate equivalent examples separately.
- Feature-local fixtures may test private implementation details, but they must not redefine protocol meaning.
- Validation tooling and contract tests must discover fixtures recursively below this directory.

Both native and Flutter contract tests recursively enumerate every `.json` fixture and decode its
complete envelope. Focused tests remain responsible for message-specific meaning.
