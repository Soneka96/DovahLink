# Development workflow

This document describes the controlled development workflow for DovahLink, including work done with AI assistance.

## Code ownership

DovahLink is maintained by its creator. External suggestions, feedback, and feature ideas are welcome through GitHub Issues, but code changes are made only by the maintainer with the help of tools available and explicitly approved for the task.

External users may provide suggestions, bug reports, and proposals through Issues. They must not submit code changes or pull requests. The maintainer alone decides, implements, and integrates changes, with AI assistance where appropriate.

## Using Issues

Use GitHub Issues to propose:

- Product ideas
- Architecture concerns
- Protocol suggestions
- Bug reports
- Usability feedback

Explain the problem, the proposed direction, and any relevant trade-offs. The maintainer will decide whether and how a suggestion fits the project.

## Branch and pull request workflow

The `main` branch is kept stable. After the documentation baseline, every repository change,
including documentation and governance, is performed on a feature branch with one pull request per
feature or clearly related fix. This workflow applies to both the maintainer and AI-assisted
development.

Direct commits, pushes, merges, and force-pushes to `main` are prohibited. Only the maintainer performs the final merge; approval does not delegate merge authority to AI or any external contributor.

1. Create a branch from the current `main` branch.
2. Keep the branch focused on one feature or one clearly related fix.
3. Keep a cross-area feature in one branch when its protocol, bridge, and client changes must land
   together.
4. Explain and document a new decision and its important trade-offs before encoding it in code or
   a shared contract.
5. Make the smallest complete change that can be reviewed.
6. Update relevant documentation and tests with the change.
7. Open one pull request for that feature.
8. Review the result and merge only after the stated checks pass.

## Merge gate

Before a pull request is considered ready, verify all of the following:

- The change has a direct maintainer-approved scope and stays within one feature or clearly related fix.
- The work was performed on a feature branch, not `main`.
- Relevant product, architecture, protocol, security, AI-convention, and test documentation is updated when the change affects it.
- Focused tests cover non-trivial behavior and its failure paths.
- Required tests or documentation checks pass, and any intentionally untested behavior is recorded.
- No secrets, credentials, generated artifacts, unrelated cleanup, or unowned abstractions were added.
- The maintainer has reviewed and explicitly approved the final diff. A pull request is merge-ready only after this approval and all checks pass; the maintainer performs the merge manually.

Branch names:

- When a branch implements a roadmap phase (or sub-phase), name it
  `feature/<phase-number>-<phase-name-slug>`, using that phase's exact heading number and a
  kebab-case slug of its name, so the branch and its PR are traceable to the phase they implement at
  a glance: `roadmap/03-local-device-pairing-and-reconnection.md`'s "## 3.1 Live Pairing Challenge UX" becomes
  `feature/3.1-live-pairing-challenge-ux`.
- For a fix or a change with no corresponding roadmap phase, use a short kebab-case description
  instead.
- For a release (the version bump, its `CHANGELOG.md` entry, and every synced version literal --
  see `ai/context/common.md`'s "Versioning"), name it `release/<version-string>`, cut from `main`,
  as its own dedicated pull request separate from any feature/phase branch.

```text
feature/3.1-live-pairing-challenge-ux
fix/reconnect-state
docs/protocol-notes
release/0.3.3
```

## Staged-code formatting

The repository's shared pre-commit hook formats only supported code files that are already staged.
It rejects partially staged supported files so formatting cannot capture unrelated work, fails when
a required formatter is unavailable, and leaves formatter changes in the worktree for review. A
formatting change makes the hook exit nonzero; review the diff, stage the intended files manually,
and retry the commit.

Enable it once per checkout:

```text
git config core.hooksPath .githooks
```

On Unix-like hosts, also make the hook executable with `chmod +x .githooks/pre-commit` before
committing it. Repository CI fails closed if it cannot determine the changed-file set for its
formatter check; it never treats a diff error as “nothing to format.”

The formatter matrix is:

| File types | Formatter |
| --- | --- |
| Dart (`.dart`) | `dart format` |
| C/C++ (`.cc`, `.cpp`, `.h`, `.hpp`) | `clang-format` |
| C# (`.cs`) | `dotnet format whitespace`, limited to the owning project and staged paths |
| Python (`.py`) | `ruff format` |
| PowerShell (`.ps1`) | PSScriptAnalyzer's `Invoke-Formatter` through `pwsh` |

Other files remain outside this hook until the repository adopts a formatter for them. The same
formatter entry point checks changed supported files in repository CI.

## AI-assisted development

AI tools must follow `AGENTS.md`, which owns implementation authority, decision priority, and
AI-specific operating rules. This document owns the branch, pull request, review, and merge
workflow.

The maintainer remains responsible for approving changes, reviewing pull requests, and merging code.
