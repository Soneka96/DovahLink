# Discussion and suggestions

Thanks for helping shape DovahLink.

## Code ownership

DovahLink is currently maintained by its creator. External suggestions, feedback, and feature ideas are welcome, but code changes are made only by the maintainer.

Please do not open pull requests or implement changes against the repository unless the maintainer has explicitly asked you to do so.

## Using Issues

Use GitHub Issues to propose:

- Product ideas
- Architecture concerns
- Protocol suggestions
- Bug reports
- Usability feedback

Explain the problem, the proposed direction, and any relevant trade-offs. The maintainer will decide whether and how a suggestion fits the project.

## Development workflow

The `main` branch is kept stable. The maintainer plans and performs implementation work on feature branches, with one pull request per feature.

1. Create a branch from the current `main` branch.
2. Keep the branch focused on one feature or one clearly related fix.
3. Make the smallest complete change that can be reviewed.
4. Update relevant documentation and tests with the change.
5. Open one pull request for that feature.
6. Merge only after the stated checks pass.

Suggested branch names:

```text
feature/connection-proof
feature/minimal-client
fix/reconnect-state
docs/protocol-notes
```

## Scope for the first implementation

The first implementation should prove reliable communication between Skyrim and one external client. Do not add the complete app, a hosted service, or broad abstractions before that path works.

## Code and design expectations

- Prefer clear, small changes over speculative flexibility.
- Keep player-facing failures understandable.
- Treat compatibility and stale data as first-class concerns.
- Preserve a read-only default until game-changing actions have a clear safety model.
- Add a focused check for non-trivial behavior.

## Questions and proposals

Open an Issue with enough context for someone unfamiliar with the idea to understand the problem, the proposed direction, and the trade-offs. Do not open a pull request unless the maintainer has explicitly requested one.
