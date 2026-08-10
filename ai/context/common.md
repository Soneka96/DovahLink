# Common conventions

These rules apply to every DovahLink area: the Flutter client, SKSE bridge, protocol, and integration work.

## Ownership and scope

- The maintainer owns product direction, architecture, protocol decisions, and final approval.
- Read `README.md`, `PRODUCT.md`, `ARCHITECTURE.md`, and `ROADMAP.md` before changing a design.
- Work on one feature or one clearly related fix at a time.
- Do not add a future layer, package, service, abstraction, or empty folder speculatively.
- Do not modify unrelated files or perform opportunistic cleanup.
- Ask before working around, replacing, or significantly changing a documented decision.

## Repository workflow

- Keep `main` stable.
- Use one feature branch and one pull request per feature.
- Keep a cross-area feature in one branch when its protocol, bridge, and client changes must land together.
- Review the complete diff for unrelated changes before presenting the work.
- Update the relevant product, architecture, roadmap, or convention document when a decision changes.

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
