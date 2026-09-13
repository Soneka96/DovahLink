# Current phase context

Source: `plans/documentation-and-composition-normalization/SOURCE.md`
Source fingerprint: SHA-256
`6b09702f2b038be5c5d1d1c81048f9c2cce5b7e079c45797bc77c49ed59fea5f`
Phase: Documentation and composition normalization
Package: `plans/documentation-and-composition-normalization/`
Status: active (package frozen 2026-09-13)

## Active concept

- File: `01-conventions-and-changelog.md`
- Status: active -- implementation and all three correction passes complete on
  `docs/conventions-and-changelog` (six findings, then three more, then four
  bookkeeping-only fixes plus one Concept 01.1 wording fix -- see Decisions below for
  all three). A fourth maintainer review pass found no remaining implementation
  blockers; awaiting merge. Not yet `Complete` per the execution contract in `PLAN.md`
  section 6 until that merge actually happens.
- Prerequisites: PR #58 merged (baseline `499bd4f4`) -- satisfied.
- Next action: merge Concept 01. After merge, record its PR number and flip its
  status table row (`PLAN.md` section 8) to `Complete`. Concept 01.1 and Concept 02
  then become eligible to start (both depend only on Concept 01). Concept 03 remains
  blocked until Concept 01.1 merges, not just Concept 01 -- do not unblock it early.

## Completed concepts

(none yet -- Concept 01 is implemented and corrected but not merged.)

## Decisions and approved deviations

- D1: the normative-correctness slice of `cpp-style.md`'s Bridge-genealogy cleanup
  (originally Issue 5 / R5.6) is reassigned to Concept 01 as R1.14. See
  `DIVERGENCES.md`.
- Execution contract: one PR per concept, review-gated; 02 may proceed once 01 merges;
  01.1 may also start once 01 merges and must merge before 03 begins (per D2, this
  superseded the original "02/03 may run in parallel" wording -- 03 now depends on
  01.1, not 01 directly); 04 requires 02 merged; 05 requires 03 merged. See `PLAN.md`
  sections 5-6.
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
- 2026-09-13 second correction pass (three items): `common.md`'s remaining stale Bridge
  terminology (intro, repository boundaries, file-org pointer list, Pre-release
  compatibility) is fixed to Adapter/Host language -- `task_47bd2093` is resolved and
  dismissed, not deferred. The enum-consolidation rule the first correction pass wrote
  (point at real `ipc/ipc_enums.hpp`, "extend that same file" for future modules) was
  itself flawed: it made `ipc/` the physical owner of any future non-IPC enum. D2
  records the fix and the new Concept 01.1: the intended convention is one project-wide
  `adapter/enums.hpp` and one project-wide `adapter/constants.hpp` (mirroring
  `csharp-style.md`'s per-project `Enums.cs`/`Constants.cs`), with domain namespaces
  preserved inside each file; `cpp-style.md` now documents that target rule while
  honestly describing `ipc/ipc_enums.hpp` and the current per-module `constants.hpp`
  files as pending relocation, not the intended location. Concept 01.1 (new concept
  file, see `PLAN.md` section 5 and `DIVERGENCES.md` D2) performs the actual physical
  move as its own concept, between Concept 01 and Concept 03 -- not folded into either.

## Deferred debt

(none currently open.)

## Changed files

- `ai/context/common.md`: semantic member/collection ordering (replacing append-only),
  documentation size/placement/ownership rules, parameter/return/exception coverage
  added to the doc-coverage list, continuous-`[Unreleased]`-update versioning workflow;
  second pass: all remaining Bridge terminology replaced with Adapter/Host (intro,
  repository boundaries -- now lists `adapter/` and `host/` -- file-org pointer list,
  Pre-release compatibility).
- `ai/context/dotnet/csharp-style.md`: semantic C# member-ordering section; `<param>`/
  `<returns>`/`<exception>` now required concisely rather than only "when useful".
- `ai/context/skse/cpp-style.md`: normative rules no longer depend on deleted `bridge/`
  paths/types (R1.14/R5.6a); C++ member-ordering section and data-member-reorder
  warning; `@param`/`@return`/`@throws` now required concisely; the dependency-edge
  rule no longer cites the retired, self-disclaimed `ai/context/skse/architecture.md`
  as current authority; second pass: enum and constants rules now state the intended
  one-project-wide-file-each target explicitly (`adapter/enums.hpp`,
  `adapter/constants.hpp`, domain namespaces preserved), with `ipc/ipc_enums.hpp` and
  the current per-module `constants.hpp` files honestly described as pending
  relocation rather than the destination.
- `CHANGELOG.md`: `[Unreleased]` section above `[0.3.3]`, backfilled since the `0.3.3`
  baseline (including the PR #58 state-subscription/recovery outcome added in the
  correction pass); header prose separates feature-PR roadmap-completion ownership
  from release-PR version/promotion ownership, matching `common.md`'s Versioning
  section (the two contradicted each other in the first pass; fixed).
- `tooling/test_repository_consistency.py`: test methods rewritten across both
  correction passes to check structural invariants (headings, forbidden stale
  literals, required current paths/types, `[Unreleased]`-first, version equality)
  instead of pinning exact prose sentences and line-wrap positions; one new method
  added for `common.md`'s Bridge-free repository boundaries.
- `plans/documentation-and-composition-normalization/01.1-adapter-enum-and-constants-physical-normalization.md`
  (new): the physical file-move concept, see `PLAN.md` section 5 and `DIVERGENCES.md`
  D2.
- `plans/documentation-and-composition-normalization/PLAN.md`,
  `03-adapter-runtime-composition.md`, `DIVERGENCES.md`: updated concept graph, status
  table, and Concept 03's dependency to record Concept 01.1's insertion.
- 2026-09-13 third correction pass (bookkeeping only, no `ai/context/`/`CHANGELOG.md`
  changes): `PLAN.md`'s "five concepts" wording (section 1's package-status note and
  section 9's phase completion gate) now references the status table generically
  instead of a fixed count, so an approved divergence adding/removing a concept can't
  leave it stale again; `PLAN.md` section 10 now names both D1 and D2 instead of only
  D1; `CONTEXT.md`'s own Decisions log had a stale "02/03 may run in parallel" line
  contradicting the correct 01→01.1→03 dependency stated elsewhere in this same file --
  fixed to match. `01.1`'s CMake requirement was verified against the actual
  `adapter/CMakeLists.txt` (it declares no per-header source list, only an include-root
  directory) and softened from a hard requirement to "verify at implementation time,
  likely no change needed."

## Verification

- `python -m unittest tooling.test_repository_consistency -v`: initial four-step
  implementation 35/35 passed; first correction pass 35/35 passed; second correction
  pass 36/36 passed; third correction pass (bookkeeping-only, this session) re-run
  clean at 36/36 -- unaffected, since no `ai/context/`/`CHANGELOG.md`/test-file content
  changed in this pass.
- No production Host/Adapter code touched in any pass -- confirmed by diff review each
  step, per Concept 01's scope boundary. The one production-code follow-up this review
  surfaced (moving `ipc/ipc_enums.hpp` and consolidating constants) is deliberately
  deferred to the new Concept 01.1, not done here.

## Handoff

Next concept: `01-conventions-and-changelog.md` (still active -- the fourth review
pass found no remaining implementation blockers and approved the engineering/content,
the plan/dependency model, and the Concept 01.1 split; only awaiting the actual merge
now, not another review pass).
Blocked by: the maintainer performing the merge (AI does not merge, per `AGENTS.md`).
