# Concept 01 -- Documentation and changelog conventions

**Status:** active

**Covers:** R1.1-R1.15 (see `PLAN.md` Requirement IDs; original wording in `SOURCE.md`
Block A Issue 1, Block B, Block C item 3).

**Depends on:** None. External prerequisite (PR #58 merged) already satisfied at
baseline `499bd4f4`.

## Why this is a stable concept

This is the one concept every other concept reads before writing a line of code or
documentation: it fixes the rules themselves. It touches no production Host or Adapter
code, only convention documents, the changelog, and the repository consistency tooling
that encodes those conventions -- a self-contained, independently reviewable unit.
`ai/context/skse/cpp-style.md` and `tooling/test_repository_consistency.py` are shared
with later concepts (`01.1` removes this concept's "pending relocation" framing once
its file move lands; Concept 05 makes its own residual `R5.6b` trim to `cpp-style.md`
afterward) -- the dependency graph in `PLAN.md` section 5 sequences those edits so no
two concepts touch the same file concurrently.

## Design

- **Addition convention (`ai/context/common.md` "Addition convention" section):**
  Replace the universal append-only rule with a semantic rule (R1.6, R1.13): ordered
  collections whose natural semantics are reverse-chronological (the changelog) keep
  newest-first; class/struct members use the semantic ordering below; append-only
  survives only where a collection's own semantics require it (for example an enum
  whose numeric values are part of a wire contract). State this explicitly rather than
  leaving today's contradiction between the prose (append-to-end), the actual
  `CHANGELOG.md` (newest-first), and
  `tooling/test_repository_consistency.py::test_changelog_matches_the_published_version`
  (which already asserts `entry_versions[0] == VERSION`, i.e. newest-first).
- **Semantic C# member ordering (R1.6):** constants/static state, injected
  dependencies, mutable instance state, constructors, properties/events,
  interface/override methods (grouped together), other public/internal methods,
  private helpers, nested types. Document this in `ai/context/dotnet/csharp-style.md`.
- **C++ member ordering caveat (R1.6):** carry the same semantic-grouping preference
  for methods/behavior into `ai/context/skse/cpp-style.md`, but add the explicit
  warning that data-member declaration order is never reordered without proving
  construction/destruction/initialization/layout safety first.
- **Documentation size/placement/ownership (R1.2-R1.5, R1.9):** add the size
  guidelines, the "what documentation should/should not contain" lists, and the
  information-ownership table to `ai/context/common.md`'s existing "Documentation"
  section (it already has strong bones -- e.g. the no-roadmap-cross-reference rule --
  this concept adds the verbosity ceiling and the history/future-narration exclusions
  that are currently missing).
- **`[Unreleased]` workflow (R1.7, R1.8):** add the section shape and release-promotion
  steps to `CHANGELOG.md`'s own header prose (it currently only documents the release
  cadence, not an `[Unreleased]` step), and cross-reference from
  `ai/context/common.md`'s "Versioning" section so the two files stop describing
  different processes.
- **`[Unreleased]` backfill (R1.15):** before adding the empty section shape, review
  `git log` between the `0.3.3` tag/commit and the `499bd4f4` baseline for
  user/developer-visible outcomes (the Host/Adapter production migration, PR #58's
  outbound priority-lane work, and anything else notable) and write them as concise,
  outcome-focused bullets under the new `[Unreleased]` section -- not a commit-by-commit
  log, not every internal refactor. Judgment call: include what would have been worth
  an `[Unreleased]` bullet had the rule existed since `0.3.3`.
- **CHANGELOG Bridge->DovahLink cleanup (R1.12):** update the changelog's title,
  header prose, release-workflow description, and the new `[Unreleased]` section to
  describe current DovahLink/Host/Adapter terminology. Do not rewrite a versioned
  historical entry merely because it says "Bridge" -- an entry for `0.1.0`-`0.3.x`
  describing the Bridge is historically accurate, since that was the architecture at
  the time, and rewriting it to retroactively describe Host/Adapter would corrupt the
  historical record `CHANGELOG.md` exists to preserve. Edit a versioned entry only when
  its wording is factually misleading about what actually shipped in that release, not
  merely because the terminology is now dated.
- **`cpp-style.md` normative-correctness fix (R1.14, D1):** remove or replace every
  place a *normative* rule depends on the deleted `bridge/` directory or a deleted type
  as its load-bearing example -- the file's opening paragraph, the enum-consolidation
  rule's `bridge/shared/enums.hpp` example, the paired-file-exception rule's
  `IBridgeCallbackRegistry`/`BridgeCallbackRegistry` example, and the
  `TokenStore::Reservation`/`SessionManager::Lease`/`ConnectionSlot::Lease` history in
  the file-organization section. Keep the architectural rule; replace or generalize the
  worked example using `adapter/`'s real module layout
  (`capture/`, `dispatch/`, `identity/`, `ipc/`, `papyrus/`, `plugin/`, `process/`,
  `runtime/`) or implementation-neutral wording. This is the slice of Issue 5's original
  scope reassigned here per `DIVERGENCES.md` D1 -- do not also attempt Issue 5's
  broader Adapter-code/test documentation pass here.
- **Consistency test additions (R1.10):** add a check asserting `[Unreleased]` is the
  first `## [...]` section in `CHANGELOG.md` (distinct from the existing
  `entry_versions[0] == VERSION` check, which already only matches numeric headings and
  is unaffected by adding `[Unreleased]` above it).

## Files this concept may change

- `ai/context/common.md`
- `ai/context/dotnet/csharp-style.md`
- `ai/context/skse/cpp-style.md` (normative-correctness slice only, per D1)
- `CHANGELOG.md`
- `tooling/test_repository_consistency.py`
- Any other convention file discovered during implementation to genuinely inherit or
  contradict these rules (per the original scope note) -- flag any such file before
  editing it, since it is outside the file list above.

## Tests / proof obligations

- `tooling/test_repository_consistency.py` passes, including the new
  `[Unreleased]`-must-be-first check and the existing version-index check.
- No production C#/C++ file changes -- diff review confirms this concept touched only
  documentation/tooling.

## Non-goals

- Applying the new conventions to actual Host or Adapter code (Concepts 02-05).
- The broader Adapter documentation/genealogy sweep (Concept 05) beyond the
  `cpp-style.md` normative slice.
- Any Host composition/DI change (Concept 02) or Adapter runtime composition change
  (Concept 03).

## Completion criteria and evidence

- Every acceptance bullet in `SOURCE.md` Block A Issue 1, plus R1.13-R1.15, is satisfied
  and traceable to a specific edit in the files above.
- `PLAN.md`'s status table updated to `Complete` with this concept's PR number, once
  that PR is actually merged.
- `CONTEXT.md` updated with completed-concept evidence before handoff to Concept 01.1
  -- per `DIVERGENCES.md` D4, Concepts 02/03 no longer follow directly; they wait
  behind the entire 01.1 -> 01.2a -> 01.2b -> 01.3a -> 01.3b -> 01.3c chain.
