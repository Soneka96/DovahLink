# Concept 04 -- Host documentation and member organization sweep

**Status:** pending

**Covers:** R4.1-R4.11 (see `PLAN.md` Requirement IDs; original wording in `SOURCE.md`
Block A Issue 4).

**Depends on:** Concept 02 merged to `main` (documenting/reorganizing Host code before
composition settles would mean redoing this work when Concept 02 moves things).

## Why this is a stable concept

A documentation-and-ordering-only pass over an already-composed `DovahLink.Host` is a
distinct review from the composition change itself: no production logic changes here,
only what is written about it and where declarations sit within their file. Reviewing
"did composition change correctly" and "did documentation get more scannable" together
would mix two very different review questions in one PR.

## Design

- Repository-wide pass over `host/DovahLink.Host/` and `host/DovahLink.Host.Tests/`
  applying Concept 01's corrected conventions: every handwritten declaration keeps its
  documentation; oversized `<summary>`/`<param>`/`<returns>` blocks shrink to current-
  contract information; historical/future narration ("previous implementation",
  "before this change", "Stage X", PR/reviewer references) is removed or rewritten as
  current-state wording; long in-method comment blocks are cut to short why-comments
  (synchronization ordering, lock avoidance, security ordering, cancellation/lifetime
  edge cases) or removed if they merely narrate the code.
- Preserve, without shortening past the point of clarity, documentation protecting:
  authentication/admission, trust, session invalidation, pairing, outbound priority,
  recovery barriers, baseline-before-events, revision ordering, play-context
  transitions, bounded queues, shutdown, concurrency, ownership/lifetime, and failure
  behavior.
- Reorder class members to the semantic ordering Concept 01 documented (constants/
  static state, injected dependencies, instance state, constructors, properties/
  events, interface/override methods grouped together, other public/internal methods,
  private helpers, nested types) wherever doing so is a pure reordering with no
  behavior change.
- Use `<inheritdoc/>` for implementations/overrides whose contract is unchanged;
  document only implementation-specific additions.
- Rewrite test documentation to describe the current invariant/scenario/non-obvious
  timing setup rather than regression genealogy.

## Files this concept may change

- All of `host/DovahLink.Host/**` and `host/DovahLink.Host.Tests/**` -- documentation
  and member ordering only, no logic changes.
- `tooling/test_repository_consistency.py` or other doc-consistency tooling, only if a
  specific check needs updating to match the corrected member-ordering/documentation
  convention (not a broad rewrite).

## Tests / proof obligations

- Full Host test suite green, unchanged pass/fail outcomes versus the pre-sweep
  baseline (a member-reorder or doc-only diff must not change behavior).
- Spot-check: for a sample of reordered classes, diff shows only declaration
  reordering and documentation text, no logic edits.

## Non-goals

- Any composition/lifetime change (that was Concept 02; do not revisit it here even if
  something looks improvable while documenting it -- report separately per the
  package-wide no-opportunistic-cleanup invariant).
- Adapter documentation (Concept 05).
- Reducing documentation coverage anywhere.

## Completion criteria and evidence

- Every R4.x acceptance bullet satisfied and traceable to specific files.
- Full Host test suite green.
- `PLAN.md` status table updated with this concept's PR number, marked `Complete` once
  merged.
