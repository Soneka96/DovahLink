# Contributing

Thanks for helping build DovahLink.

## Development workflow

The `main` branch is kept stable. Documentation establishes the direction first; implementation work should be proposed as focused feature branches.

1. Create a branch from the current `main` branch.
2. Keep the branch focused on one feature or one clearly related fix.
3. Make the smallest complete change that can be reviewed.
4. Update relevant documentation and tests with the change.
5. Open one pull request for that feature.
6. Merge only after the pull request is reviewed and the stated checks pass.

Suggested branch names:

```text
feature/connection-proof
feature/minimal-client
fix/reconnect-state
docs/protocol-notes
```

## Pull requests

Each pull request should explain:

- What changed
- Why it changed
- How it was tested
- Any known limitations or follow-up work

Avoid bundling unrelated cleanup into a feature pull request. If a change affects the product direction or protocol, update the corresponding document in the same pull request.

## Scope for the first implementation

The first implementation should prove reliable communication between Skyrim and one external client. Do not add the complete app, a hosted service, or broad abstractions before that path works.

## Code and design expectations

- Prefer clear, small changes over speculative flexibility.
- Keep player-facing failures understandable.
- Treat compatibility and stale data as first-class concerns.
- Preserve a read-only default until game-changing actions have a clear safety model.
- Add a focused check for non-trivial behavior.

## Questions and proposals

Open an issue or pull request with enough context for someone unfamiliar with the change to understand the problem, the proposed direction, and the trade-offs.
