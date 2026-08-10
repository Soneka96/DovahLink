# Development workflow

This document describes the controlled development workflow for DovahLink, including work done with AI assistance.

## Code ownership

DovahLink is maintained by its creator. External suggestions, feedback, and feature ideas are welcome through GitHub Issues, but code changes are made only by the maintainer with the help of approved development tools.

External contributors should not open pull requests or implement changes against the repository unless the maintainer has explicitly asked them to do so.

## Using Issues

Use GitHub Issues to propose:

- Product ideas
- Architecture concerns
- Protocol suggestions
- Bug reports
- Usability feedback

Explain the problem, the proposed direction, and any relevant trade-offs. The maintainer will decide whether and how a suggestion fits the project.

## Branch and pull request workflow

The `main` branch is kept stable. Implementation work is performed on feature branches, with one pull request per feature. This workflow applies to both the maintainer and AI-assisted development.

1. Create a branch from the current `main` branch.
2. Keep the branch focused on one feature or one clearly related fix.
3. Make the smallest complete change that can be reviewed.
4. Update relevant documentation and tests with the change.
5. Open one pull request for that feature.
6. Review the result and merge only after the stated checks pass.

Suggested branch names:

```text
feature/connection-proof
feature/minimal-client
fix/reconnect-state
docs/protocol-notes
```

## AI-assisted development

AI tools should inspect this repository's own documents before making changes. Its rules must remain self-contained and portable; AI must not depend on another local project, an absolute path, or unavailable external instructions. They should preserve the documented architecture, avoid speculative structure, and ask for direction before making a change that works around an architectural decision.

The maintainer remains responsible for approving changes, reviewing pull requests, and merging code.

## Scope for the first implementation

The first implementation should prove reliable communication between Skyrim and one external client. Do not add the complete app, a hosted service, or broad abstractions before that path works.

## Code and design expectations

- Prefer clear, small changes over speculative flexibility.
- Keep player-facing failures understandable.
- Treat compatibility and stale data as first-class concerns.
- Preserve a read-only default until game-changing actions have a clear safety model.
- Add a focused check for non-trivial behavior.

## Questions and proposals

Open an Issue with enough context for someone unfamiliar with the idea to understand the problem, the proposed direction, and the trade-offs. Issues are the appropriate place for external suggestions; code changes should follow the maintainer's development workflow.
