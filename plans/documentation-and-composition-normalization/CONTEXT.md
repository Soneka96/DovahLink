# Current phase context

Source: `plans/documentation-and-composition-normalization/SOURCE.md`
Source fingerprint: SHA-256
`6b09702f2b038be5c5d1d1c81048f9c2cce5b7e079c45797bc77c49ed59fea5f`
Phase: Documentation and composition normalization
Package: `plans/documentation-and-composition-normalization/`
Status: active (package frozen 2026-09-13)

## Active concept

- File: `01-conventions-and-changelog.md`
- Status: active
- Prerequisites: PR #58 merged (baseline `499bd4f4`) -- satisfied.
- Next action: Implement Concept 01 on its own branch/PR -- fix
  `ai/context/common.md`'s Addition convention and Documentation sections,
  `ai/context/dotnet/csharp-style.md`'s member ordering, `ai/context/skse/cpp-style.md`'s
  normative Bridge references (R1.14/R5.6a per D1), add the `[Unreleased]` workflow to
  `CHANGELOG.md` backfilled since `0.3.3` (R1.15), and the matching
  `tooling/test_repository_consistency.py` checks.

## Completed concepts

(none yet)

## Decisions and approved deviations

- D1: the normative-correctness slice of `cpp-style.md`'s Bridge-genealogy cleanup
  (originally Issue 5 / R5.6) is reassigned to Concept 01 as R1.14. See
  `DIVERGENCES.md`.
- Execution contract: one PR per concept, review-gated; 02/03 may run in parallel
  once 01 merges; 04 requires 02 merged; 05 requires 03 merged. See `PLAN.md` section 6.
- `[Unreleased]` must be backfilled with notable outcomes merged since the `0.3.3`
  baseline, not introduced empty (R1.15). See `PLAN.md` Requirement IDs and
  `SOURCE.md` Block C item 3.
- Concept files trace to requirement IDs and restate acceptance concisely; `SOURCE.md`
  remains the one verbatim copy of the original requirement text.
- Package-wide invariant: no opportunistic cleanup; preserve runtime/protocol/security/
  public behavior in every concept unless that concept's own scope states otherwise;
  report unrelated findings separately. See `PLAN.md` section 3.
- 2026-09-13 correction pass (five items, approved before freezing the package):
  status tracking drops the merge-SHA column (`Status | PR` only, GitHub owns the SHA);
  `R5.6` is split explicitly into `R5.6a` (Concept 01/`R1.14`, normative correctness)
  and `R5.6b` (Concept 05, non-normative cleanup) rather than reassigned wholesale;
  Concept 01's changelog cleanup no longer rewrites historically-accurate versioned
  entries for saying "Bridge", only header/workflow/`[Unreleased]` prose and factually
  misleading entries; Concept 02's prescriptive startup pseudo-sequence is replaced
  with a requirement to establish and preserve the actual current ordering from
  `Program.cs`/tests, framed as "composition equivalence"; Concept 02's static-
  dictionary example is now explicitly conditional on what the lifetime audit finds.

## Deferred debt

(none yet)

## Changed files

(none yet -- package planning files only: `SOURCE.md`, `PLAN.md`, `DIVERGENCES.md`,
`CONTEXT.md`, and the five concept files under
`plans/documentation-and-composition-normalization/`.)

## Verification

(none yet -- no implementation has started.)

## Handoff

Next concept: `01-conventions-and-changelog.md`
Blocked by: none.
