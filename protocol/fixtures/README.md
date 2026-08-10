# Protocol fixtures

Fixtures in this directory are the language-neutral source of truth for cross-side contract tests.

## Rules

- Store one complete JSON message per fixture file.
- Every fixture must contain the protocol version required to decode it. Expected decoded values belong in the consuming test, not in a second fixture metadata format.
- Cover valid negotiation, capabilities, subscriptions, snapshots, events, errors, unknown optional fields, incompatible versions, stale revisions, revision gaps, and old sessions.
- Flutter and SKSE tests consume the same fixture files; they must not recreate equivalent examples separately.
- Feature-local fixtures may test private implementation details, but they must not redefine protocol meaning.
- Fixture filenames must identify the message type and scenario, for example `state-event-revision-gap.json`.
