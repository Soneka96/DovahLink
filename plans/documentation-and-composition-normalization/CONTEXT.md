# Current phase context

Source: `plans/documentation-and-composition-normalization/SOURCE.md`
Source fingerprint: SHA-256
`6b09702f2b038be5c5d1d1c81048f9c2cce5b7e079c45797bc77c49ed59fea5f`
Phase: Documentation and composition normalization
Package: `plans/documentation-and-composition-normalization/`
Status: active (package frozen 2026-09-13)

## Active concept

- File: `01-conventions-and-changelog.md`
- Status: active -- implemented on `docs/conventions-and-changelog`, first review pass
  found six findings (three correctness gaps, two cleanup items, one bookkeeping gap),
  all six corrected in a follow-up pass on the same branch. Awaiting the second review
  pass and merge; not yet `Complete` per the execution contract in `PLAN.md` section 6.
- Prerequisites: PR #58 merged (baseline `499bd4f4`) -- satisfied.
- Next action: push the correction commit(s), re-run
  `python -m unittest tooling.test_repository_consistency -v`, then hand the branch back
  for a second review pass confirming all six findings are actually closed. Concepts 02
  and 03 remain blocked until Concept 01 actually merges -- do not unblock them yet.

## Completed concepts

(none yet -- Concept 01 is implemented and corrected but not merged.)

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

- `task_47bd2093` (spawned during Concept 01, still open): `ai/context/common.md` still
  describes the deleted `bridge/` directory as an active repository boundary ("`bridge/`
  is reserved for the native SKSE bridge") and calls the C++ area "Skyrim bridge" in the
  file-organization pointer list. This is deliberately out of Concept 01's scope (its
  file list never named these lines) and is NOT resolved by anything in this concept --
  do not treat it as accidentally fixed. It remains open for a separate session.

## Changed files

- `ai/context/common.md`: semantic member/collection ordering (replacing append-only),
  documentation size/placement/ownership rules, parameter/return/exception coverage
  added to the doc-coverage list, continuous-`[Unreleased]`-update versioning workflow.
- `ai/context/dotnet/csharp-style.md`: semantic C# member-ordering section; `<param>`/
  `<returns>`/`<exception>` now required concisely rather than only "when useful".
- `ai/context/skse/cpp-style.md`: normative rules no longer depend on deleted `bridge/`
  paths/types (R1.14/R5.6a); C++ member-ordering section and data-member-reorder
  warning; `@param`/`@return`/`@throws` now required concisely; the enum-consolidation
  rule now points at the real `ipc/ipc_enums.hpp` instead of an invented
  `adapter/shared/enums.hpp`; the dependency-edge rule no longer cites the retired,
  self-disclaimed `ai/context/skse/architecture.md` as current authority.
- `CHANGELOG.md`: `[Unreleased]` section above `[0.3.3]`, backfilled since the `0.3.3`
  baseline (including the PR #58 state-subscription/recovery outcome added in the
  correction pass); header prose separates feature-PR roadmap-completion ownership
  from release-PR version/promotion ownership, matching `common.md`'s Versioning
  section (the two contradicted each other in the first pass; fixed).
- `tooling/test_repository_consistency.py`: four new/updated test methods, rewritten in
  the correction pass to check structural invariants (headings, forbidden stale
  literals, required current paths/types, `[Unreleased]`-first, version equality)
  instead of pinning exact prose sentences and line-wrap positions.

## Verification

- `python -m unittest tooling.test_repository_consistency -v`: run after the initial
  four-step implementation (35/35 passed) and again after the correction pass (record
  the result here once the correction commit lands).
- No production Host/Adapter code touched in either pass -- confirmed by diff review
  each step, per Concept 01's scope boundary.

## Handoff

Next concept: `01-conventions-and-changelog.md` (still active -- awaiting second review
pass and merge, not yet handed off).
Blocked by: maintainer review of the correction pass.
