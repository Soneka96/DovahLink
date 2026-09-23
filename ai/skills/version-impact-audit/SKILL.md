---
name: version-impact-audit
description: Manually audit a phase, protocol change, or bug fix for compatibility, persistence, runtime, changelog, and semantic-version impact.
---

# Version-impact audit

Use this workflow when a maintainer asks for a version-impact audit of a roadmap phase, protocol
change, compatibility-affecting fix, or other explicitly named change. It produces an evidence-based
recommendation; it is not a release operation.

## Manual invocation

Ask an agent: `Use ai/skills/version-impact-audit/SKILL.md to audit <scope> against <baseline>.`
Name the complete phase range for a phase-end audit, not only its latest pull request. For a bug fix,
name the fix range and any baseline needed to establish its compatibility effect. The agent reports
the range it actually inspected and explains why the baseline represents the relevant prior
contract.

## Audit procedure

1. Read the requested scope, its acceptance criteria, and current repository rules for versioning,
   compatibility, security, changelog ownership, and release workflow.
2. Establish the comparison baseline from repository history, releases, version literals, and the
   contract before the audited changes. Inspect the complete requested range; do not infer impact
   from the latest pull request alone. When a migration changed implementation ownership, separate
   that architectural move from public behavior or compatibility changes.
3. Read the affected implementation, tests, fixtures, public exports, and authoritative design
   documents. Use a repository code index when available to find likely affected paths, then verify
   findings against repository sources.
4. Assess each area below. Record `none` when inspection finds no impact; do not omit a category:
   - Host changes and Host/client ownership;
   - native Adapter changes and Skyrim/SKSE/CommonLib runtime support;
   - public SDK/API changes, including work pulled forward from a later phase;
   - Flutter/application compatibility and remaining client work;
   - public protocol/schema changes and canonical fixtures;
   - persistence formats and migration requirements;
   - security, authentication, authorization, and transport behavior;
   - compatibility behavior and unsupported-version handling;
   - tests, independent validators, and public exports;
   - current version ownership and changelog impact.
5. Classify the release impact from the actual public contract change. Do not assume a patch release,
   and do not force a contract-breaking bug fix into a patch classification. Distinguish Skyrim
   runtime support from Host/client protocol compatibility. Apply the repository's current
   pre-release compatibility policy and version ownership rules.
6. Report recommended documentation and changelog actions, any required follow-up release work, and
   blockers. If a phase audit finds a blocker, recommend leaving the phase open.

## Required report

Return these fields in order:

1. **Audited range** — starting and ending commits or equivalent identifiers.
2. **Comparison baseline** — commit/version and why it is the correct prior contract.
3. **Affected components** — Host, Adapter, SDK, app, protocol, tests, and validators as applicable.
4. **Protocol impact** — public messages, fields, semantics, fixtures, and exports.
5. **Persistence impact** — changed formats, ownership, migration, or none.
6. **Security impact** — authentication, authorization, exposure, input limits, or none.
7. **Runtime impact** — supported game/runtime/toolchain changes, distinct from protocol support.
8. **Compatibility impact** — clients, supported ranges, and enforcement behavior.
9. **Recommended version classification** — patch, minor/breaking, major, or another justified result.
10. **Current version and recommended next release** — identify the current repository version,
    then give the recommended next version and reasoning.
11. **Changelog actions** — entries to add or revise under the current repository rules; do not create
    a versioned release section during an ordinary feature or fix branch.
12. **Compatibility-documentation actions** — authoritative files that need updates, or none.
13. **Required follow-up release work** — release branch and synchronized version tasks, or none.
14. **Unresolved blockers** — explicit blockers, or none.

## Boundaries

- The audit is read-only unless the maintainer separately asks to implement specific findings.
- Never silently change unrelated files or widen the named audit scope.
- Never commit, push, merge, publish, or alter the staging area.
- Never automatically classify a change as a patch.
- Never bump `VERSION`, synchronize release-version literals, or create the final dated release section
  on an ordinary feature or fix branch. Recommend the release work for a later dedicated
  `release/<version>` branch from `main`.
- Do not mark a roadmap phase complete unless the maintainer requested phase closure and every
  documented completion requirement has evidence with no unresolved blocker.
