# Concept 05 -- Adapter documentation and member organization sweep

**Status:** pending

**Covers:** R5.1-R5.5, R5.6b, R5.7-R5.10 (see `PLAN.md` Requirement IDs; original
wording in `SOURCE.md` Block A Issue 5). `R5.6`'s other half, `R5.6a` (= `R1.14`), is
owned by Concept 01; see `DIVERGENCES.md` D1.

**Depends on:** Concept 03 merged to `main` (documenting/reorganizing Adapter code
before composition settles would mean redoing this work when Concept 03 moves things).

## Why this is a stable concept

Mirrors Concept 04's rationale on the Adapter side: a documentation-and-ordering-only
pass over already-composed Adapter code is a distinct review from the composition
change itself, and must not be mixed with it.

## Design

- Repository-wide pass over the Adapter production code, plugin entry point, and
  Adapter tests, applying Concept 01's corrected conventions (which, per D1, already
  made `ai/context/skse/cpp-style.md`'s normative rules Bridge-free before this concept
  starts).
- `R5.6b`: once `cpp-style.md`'s rules are already correct per Concept 01/`R5.6a`,
  trim any remaining non-normative verbosity or historical color left in that file's
  prose -- illustrative examples, worked-through genealogy of a since-replaced type,
  and similar -- without touching the normative rules themselves.
- Every handwritten class/interface, struct, enum and enum value, type alias,
  constructor/destructor, field, method, free function, and private/file-local/test
  helper keeps concise Doxygen-compatible documentation; shrink oversized blocks to
  current-contract information.
- Remove documentation whose main purpose is migration genealogy (what the old Bridge
  did, which type existed before, which PR/roadmap stage changed it, what a future
  stage may replace it with) from Adapter *implementation and test* files. This is the
  R5.5 slice that remains here after D1 moved the convention-*document* slice to
  Concept 01/R1.14.
- Preserve, concisely, every comment protecting a non-obvious runtime constraint: SKSE
  initialization ordering, game-thread requirements, CommonLib/runtime restrictions,
  callback/async-completion lifetime, exception boundaries, process-lifetime ownership,
  the Windows loader lock, why `DllMain` must not join/wait/destroy worker-owning
  services, include-order constraints, ownership of Skyrim/runtime values, bounded work
  inside game callbacks, and thread handoff requirements.
- Use `@copydoc` for unchanged inherited/interface contracts instead of duplicating
  documentation between interface and implementation.
- Reorganize API-facing/interface methods and private helpers into consistent, clearly
  separated groups wherever doing so is a pure reordering with no behavior change.
- Do not reorder C++ data members unless the reordering is proven safe for
  construction, destruction, aggregate/designated initialization, and layout/ABI
  assumptions -- when in doubt, leave the order exactly as-is and document why if it
  looks reorderable but is not.

## Files this concept may change

- All current Adapter production code, plugin entry point, and Adapter tests --
  documentation and member ordering only, no logic changes.
- `ai/context/skse/cpp-style.md`, for `R5.6b`'s residual non-normative example
  trimming only (the `R5.6a`/`R1.14` normative-correctness slice is already done in
  Concept 01; do not re-touch normative rules here).

## Tests / proof obligations

- Full Adapter/C++ test suite green, identical pass/fail outcomes versus the pre-sweep
  baseline.
- Spot-check: for a sample of reordered types, diff shows only member reordering and
  documentation text, no logic edits, and no data-member reordering without an explicit
  safety justification recorded in the PR description.

## Non-goals

- Any composition/lifetime change (that was Concept 03; report anything newly noticed
  separately per the package-wide no-opportunistic-cleanup invariant).
- Host documentation (Concept 04).
- Reducing documentation coverage anywhere.
- Reopening `cpp-style.md`'s normative rules (Concept 01's responsibility).

## Completion criteria and evidence

- Every covered R5.x acceptance bullet satisfied and traceable to specific files.
- Full Adapter/C++ test suite green.
- `PLAN.md` status table updated with this concept's PR number, marked `Complete` once
  merged -- this is the package's final concept; on completion, verify the phase
  completion gate in `PLAN.md` section 9.
