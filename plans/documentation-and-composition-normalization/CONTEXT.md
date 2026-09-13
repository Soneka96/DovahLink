# Current phase context

Source: `plans/documentation-and-composition-normalization/SOURCE.md`
Source fingerprint: SHA-256
`6b09702f2b038be5c5d1d1c81048f9c2cce5b7e079c45797bc77c49ed59fea5f`
Phase: Documentation and composition normalization
Package: `plans/documentation-and-composition-normalization/`
Status: active (package frozen 2026-09-13)

## Active concept

- File: `01.2a-active-documentation-and-instruction-terminology.md`
- Status: planned -- not yet started.
- Prerequisites: Concept 01.1 merged (`main` @ `77f31fa0`, PR #61) -- satisfied.
- Next action: begin Concept 01.2a's own implementation (re-run its Bridge-terminology
  inventory against current `main` first, per its safety checklist). Per D4, Concept 02
  still waits behind the entire 01.1 -> 01.2a -> 01.2b -> 01.3a -> 01.3b -> 01.3c chain,
  same as Concept 03 -- do not unblock 02 or 03 until 01.3c actually merges.

## Completed concepts

- `01-conventions-and-changelog.md` -- merged to `main` via PR #60 (merge commit
  `8847fcdc`, 2026-09-13).
- `01.1-adapter-enum-and-constants-physical-normalization.md` -- merged to `main` via
  PR #61 (merge commit `77f31fa0`, 2026-09-13).

## Decisions and approved deviations

- D1: the normative-correctness slice of `cpp-style.md`'s Bridge-genealogy cleanup
  (originally Issue 5 / R5.6) is reassigned to Concept 01 as R1.14. See
  `DIVERGENCES.md`.
- Execution contract: one PR per concept, review-gated. Original wording ("02/03 may
  run in parallel once 01 merges") was superseded twice: first by D2 (03 depends on
  01.1, not 01 directly), then by D4 (both 02 and 03 now wait behind the entire
  01.1 -> 01.2a -> 01.2b -> 01.3a -> 01.3b -> 01.3c chain -- 02 is blocked by 01.3c;
  03 is blocked by 01.1 AND 01.3c, both named explicitly). 04 requires 02 merged; 05
  requires 03 merged. See `PLAN.md` sections 5-6 for the authoritative current graph.
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
- D4 (2026-09-13, from a user-supplied agent brief,
  `DovahLink_Bridge_Terminology_Normalization_Agent_Brief.md`, reviewed and approved
  with two corrections): inserts five new concepts -- `01.2a` (active docs/
  instructions terminology), `01.2b` (internal code/test/tooling terminology),
  `01.3a` (design-only public vocabulary + instance-identity decision, no wire
  implementation), `01.3b` (compatibility/version vocabulary cutover, implements
  01.3a exactly), `01.3c` (public authoritative-instance identity cutover, implements
  01.3a exactly) -- between Concept 01.1 and Concept 03/02. Why now: a full-repository
  inventory found 236 files / 1,342 case-insensitive `bridge` hits that are not one
  kind of debt (stale active terminology, stale internal naming, public wire fields
  `bridgeVersion`/`bridgeInstanceId`, deliberate historical references, generic
  unrelated usage), and Concept 02/03 composing Host/Adapter against names scheduled
  for imminent rename would be wasted work. Blind global rename is explicitly
  forbidden; `01.3b`/`01.3c` are the package's only concepts allowed to touch public
  wire behavior, only to the exact extent `01.3a` decides -- the one approved
  exception to the package-wide "no protocol change" invariant. A hard 100-file/PR
  cap applies (target <=80); an atomic migration that cannot fit stops for maintainer
  review rather than inventing a compatibility shim. The two corrections applied
  before insertion: the brief's own proposed identifier ("D3") collided with this
  package's actual D3 above and was renumbered D4; Concept 02's and Concept 03's
  dependency lines were both updated explicitly (02: blocked by 01.3c; 03: blocked by
  01.1 AND 01.3c, both named even though 01.1 is transitively implied, since they
  document Adapter composition's two independent prerequisites).

## Deferred debt

(none currently open. Note: the D4 inventory below is a planning-time estimate at
directory/file granularity, not a certified hit-by-hit classification -- each of
01.2a/01.2b/01.3a/01.3b/01.3c is required to re-run and re-classify its own inventory
before it starts implementing, per the brief's own safety checklist. This is by
design, not debt.)

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
- 2026-09-13 D4 planning insertion (this session, planning-only -- no `ai/context/`,
  production source, protocol schema, or test file touched): added
  `01.2a-active-documentation-and-instruction-terminology.md`,
  `01.2b-internal-code-test-and-tooling-terminology.md`,
  `01.3a-public-vocabulary-and-identity-semantics.md`,
  `01.3b-compatibility-version-vocabulary-cutover.md`,
  `01.3c-public-authoritative-instance-identity-cutover.md`; updated `PLAN.md`'s
  concept graph, status table, execution contract (100-file/PR cap), phase completion
  gate (D4's protocol exception), and Objective/Non-goals (D4's scope and exception);
  added `DIVERGENCES.md` D4; updated `02-host-composition-and-di-lifetimes.md`'s and
  `03-adapter-runtime-composition.md`'s `Depends on` lines to the new chain.
- 2026-09-13 D4 correction pass (this session, planning-only, four items from
  maintainer review plus one scope broadening): (1) the inventory baseline was
  mislabeled "main @ 456f03b" -- 456f03b is a commit on this branch, never on `main`
  (`main` is `499bd4f4`); re-ran both required searches (content and path/filename)
  against the actual pre-D4 commit `9ef61c51699bfd78f3810d99f684025f4c6009ad` and
  relabeled it "planning inventory baseline" everywhere it was cited (D4, `01.2a`,
  here) -- counts were unchanged (236 files, 1,342 hits), only the label was wrong.
  (2) Fixed three stale dependency/handoff statements that survived D4: `01`'s
  completion criteria said "handoff to Concepts 02/03" (now: Concept 01.1); `01.1`'s
  said merging it "unblocks Concept 03" (now: unblocks 01.2a, with 03 noted as
  requiring the full chain); D2's "Concept 02 is unaffected" claim is now explicitly
  annotated as superseded by D4 rather than silently left to contradict it. (3)
  Broadened `01.2a` to own active documentation/comment prose regardless of physical
  file type (Markdown, Doxygen/Dart doc comments, Papyrus/YAML comments), since public
  SDK doc comments like `dovahlink_client.dart`'s "if the bridge rejects
  authentication" were previously owned by neither `01.2a` (scoped to `.md`/
  `ai/context/` only) nor `01.2b` (identifiers only) -- with an explicit rule
  distinguishing general architectural prose (01.2a) from wire-field-specific
  documentation that stays `01.3a`-`01.3c`'s territory (`envelope.dart`'s
  `bridgeInstanceId` doc comment, `HelloAckPayload.cs`'s legacy-field-name comment)
  and from identifier-bound comments that stay `01.2b`'s
  (`adapter_task_marshaller.hpp`'s stale `IBridgeCallbackRegistry` reference).
  Re-estimated `01.2a`'s file count to ~30-40 (up from ~20-30, not the ~72 a naive
  "any comment containing bridge" sweep would suggest, since that sweep does not
  distinguish the three categories above). (4) Removed `01.3c`'s unsafe
  split-by-fixture-family example -- splitting one shared-envelope-header field's
  rename across fixture families would create a broken intermediate wire state,
  contradicting D4's own "never leave main broken" rule two sentences later -- and
  replaced it with a tiered policy (<=80 normal PR; 81-100 stop for maintainer
  review, who may approve one large atomic PR; >100 return to `01.3a` for a genuinely
  staged design or stop). Confirmed via fresh-eyes phrase search that no other D4
  file has the same unsafe pattern.
- 2026-09-13 post-merge bookkeeping (this session, planning-only): `PLAN.md`'s status
  table row for Concept 01.1 updated `In progress` -> `Complete | #61` (PR #61 merged
  to `main` as `77f31fa0`); Concept 01.2a's row updated `Blocked by 01.1` -> `Planned`.
  `CONTEXT.md`'s Active concept, Completed concepts, and Handoff sections updated to
  match -- Concept 01.1 moved to Completed, Concept 01.2a is now Active/next.

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
- `python -m unittest tooling.test_repository_consistency -v`: re-run after the D4
  planning insertion, 36/36 passed -- unaffected, since this pass touched only the
  `plans/` package and no `ai/context/`/`CHANGELOG.md`/test-file content.
- Bridge inventory planning baseline @ `9ef61c51699bfd78f3810d99f684025f4c6009ad`
  (the pre-D4 commit on this branch -- not `main`, which is `499bd4f4`; see `01.2a`
  for the full category breakdown and both required search commands): 236 files,
  1,342 case-insensitive `bridge`
  hits. Planning-time estimate only; each implementation concept re-runs it.
- `python -m unittest tooling.test_repository_consistency -v`: re-run after the D4
  correction pass, 36/36 passed -- unaffected, planning-only.
- `git diff --check`: clean (no whitespace errors; only routine LF/CRLF line-ending
  notices, not errors).
- Whole-PR changed-file count vs. `main` (`git merge-base HEAD main` = `499bd4f4`,
  then `git diff --name-only base...HEAD`): **20 files**, unchanged by this
  correction pass (it only edited files already modified earlier on this branch) --
  comfortably under both the 80 re-plan threshold and the 100 hard stop.
- Fresh-eyes phrase search (`456f03b`, `unblocks Concept 03`, `handoff to Concepts
  02/03`, `02/03 may`, `Concept 02 is unaffected`, `Bridge/Core`, `>80`, `fixture
  family`) across the whole planning package: every remaining hit is either already
  fixed, historical record of a prior correction, or `SOURCE.md`'s frozen text --
  none is a live stale claim.
- Confirmed via `git log`/`git show` that PR #61 is actually merged to `main`
  (`77f31fa0`, parents `8847fcdc`+`3d2215c5`) before marking Concept 01.1 `Complete` --
  not inferred from the branch's own prior "implementation complete, awaiting merge"
  note, per this plan's own rule that `Complete` is authoritative only once GitHub's
  merge record confirms it (`PLAN.md` section 8).

## Handoff

Next concept: `01.2a-active-documentation-and-instruction-terminology.md` -- planned,
not yet started. Concept 01.1 merged to `main` via PR #61 (merge commit `77f31fa0`,
2026-09-13); `PLAN.md`'s status table and this file's Active/Completed sections are
updated accordingly. Blocked by: nothing -- 01.2a's prerequisite (01.1 merged) is
satisfied. Before implementing, 01.2a must re-run its own Bridge-terminology inventory
against current `main` rather than relying on the pre-D4 planning-baseline counts
recorded above, per D4's own safety checklist.
