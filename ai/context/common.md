# Common conventions

These conventions define versioning, repository ownership, and quality rules shared by the Flutter
client, SKSE bridge, protocol, and integration work. AI authority belongs to `AGENTS.md`; branch and
pull request workflow belongs to `CONTRIBUTING.md`.

## Versioning

- Roadmap phase numbers and application versions are separate; completing a phase does not require
  matching the application version to that phase number.
- Do not add a changelog or release artifact until the repository has an approved release workflow.

## Repository boundaries

- `app/` is reserved for the Flutter client.
- `bridge/` is reserved for the native SKSE bridge.
- `protocol/` is the sole home for canonical cross-side schemas and shared protocol fixtures.
- `integration/` is reserved for tests and scenarios that exercise boundaries between areas.
- `test/` is reserved for tests owned by an implementation area; cross-side contract fixtures remain in `protocol/fixtures/`.
- `tooling/` is reserved for maintainer-approved repository scripts and validation tools.
- Root-level configuration belongs only to repository-wide tooling; it must not become a dumping ground for implementation code.
- Generated files belong in the area that owns their source and must never be hand-edited.
- Flutter conventions are the complete set in `ai/context/flutter/architecture.md`,
  `dart-style.md`, `testing.md`, and `error-handling.md`; do not replace them with a summary.
- No area may place its implementation types, private fixtures, or infrastructure in another area's directory.
- `protocol/fixtures/` contains canonical cross-side fixtures; `integration/` contains scenarios and harnesses that consume them.

## Quality floor

- Keep failure behavior explicit and understandable.
- Do not hide stale, missing, or incompatible data behind plausible defaults.
- Keep read-only behavior as the default until an action has an approved safety model.
- Do not introduce a second implementation of a rule that belongs in a shared contract.
