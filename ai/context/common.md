# Common conventions

These rules apply to every DovahLink area: the Flutter client, SKSE bridge, protocol, and integration work.

## Ownership and scope

- The maintainer owns product direction, architecture, protocol decisions, and final approval.
- Only a direct maintainer instruction in the current task that clearly names the scope authorizes implementation of a new feature or architectural change; roadmap items and discussion alone do not.
- Read `README.md`, `PRODUCT.md`, `ARCHITECTURE.md`, and `ROADMAP.md` before changing a design.
- Work on one feature or one clearly related fix at a time.
- Do not add a future layer, package, service, abstraction, or empty folder speculatively.
- Do not modify unrelated files or perform opportunistic cleanup.
- Ask before working around, replacing, or significantly changing a documented decision.

## Change approval

- Changes to architecture, protocol meaning, security, runtime support, dependencies, or repository boundaries require explicit maintainer approval before implementation.
- A request to implement a feature does not automatically approve unrelated structural changes needed to support it.
- If an existing rule blocks the requested change, report the conflict and ask; do not silently reinterpret the rule.

## Repository workflow

- Keep `main` stable.
- Use one feature branch and one pull request per feature.
- Never commit, push, merge, or force-push directly to `main` without explicit maintainer instruction.
- Keep a cross-area feature in one branch when its protocol, bridge, and client changes must land together.
- Review the complete diff for unrelated changes before presenting the work.
- Update the relevant product, architecture, roadmap, or convention document when a decision changes.

## Repository boundaries

- `app/` is reserved for the Flutter client.
- `bridge/` is reserved for the native SKSE bridge.
- `protocol/` is the sole home for canonical cross-side schemas and shared protocol fixtures.
- `integration/` is reserved for tests and scenarios that exercise boundaries between areas.
- `test/` is reserved for tests owned by an implementation area; cross-side contract fixtures remain in `protocol/fixtures/`.
- `tooling/` is reserved for maintainer-approved repository scripts and validation tools.
- Root-level configuration belongs only to repository-wide tooling; it must not become a dumping ground for implementation code.
- Generated files belong in the area that owns their source and must never be hand-edited.
- For Dart JSON models, use `json_serializable` with `build_runner`; keep generated output beside its source and regenerate it instead of editing it manually.
- Keep one public model or class per file; only action declaration files and the documented Flutter `StatefulWidget`/`State<T>` pairing may contain multiple related classes.
- No area may place its implementation types, private fixtures, or infrastructure in another area's directory.
- `protocol/fixtures/` contains canonical cross-side fixtures; `integration/` contains scenarios and harnesses that consume them.

## AI workflow

1. Read the relevant local convention files before changing code or design.
2. Identify the boundary the change belongs to before choosing a folder or type.
3. Prefer the smallest complete change that fits the existing architecture.
4. Explain a new decision before encoding it in code or a shared contract.
5. Add focused tests for non-trivial behavior and its failure paths.
6. Never assume that a technically working alternative fits the project.

## Quality floor

- Keep failure behavior explicit and understandable.
- Do not hide stale, missing, or incompatible data behind plausible defaults.
- Keep read-only behavior as the default until an action has an approved safety model.
- Do not introduce a second implementation of a rule that belongs in a shared contract.
