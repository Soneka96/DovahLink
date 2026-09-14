# Current phase context

Source: `plans/documentation-and-composition-normalization/SOURCE.md`
Source fingerprint: SHA-256
`6b09702f2b038be5c5d1d1c81048f9c2cce5b7e079c45797bc77c49ed59fea5f`
Phase: Documentation and composition normalization
Package: `plans/documentation-and-composition-normalization/`
Status: active (package frozen 2026-09-13)

## Active concept

- File: `01.3a-public-vocabulary-and-identity-semantics.md`
- Status: Complete on branch `docs/01.3a-public-vocabulary-and-identity-semantics`,
  not yet opened as a PR. Design-only gate, all six decisions final:
  - Compatibility authority is the Host release version: `bridgeVersion` -> `hostVersion`.
  - `bridgeInstanceId` -> `stateAuthorityId`: a Host authoritative-state continuity
    epoch, not a process/instance identity and not a public counterpart of
    `adapterInstanceId`.
  - Rotation happens at continuity-loss detection (Host restart, or Adapter/IPC
    connection loss observed) -- not when the resync/rebind that follows later
    succeeds.
  - One unresolved continuity break is exactly one epoch through every failed
    reconnect/resync attempt; a later loss starts a new epoch only once a fresh
    authoritative baseline has been successfully established internally under the
    current value -- client snapshot publication is never the epoch boundary.
  - A runtime mint failure after a continuity break is detected is a fatal Host
    invariant failure: no retained/`null` value, no degraded serving, normal
    deterministic shutdown (a startup mint failure uses the existing fail-closed
    startup path instead).
  - Cache/revision scope is `(stateAuthorityId, playContextId, stateArea)`.
  - Exact wire presence: only `hello_ack`, `state_snapshot`, and `state_event` carry
    `stateAuthorityId`, required and non-null; no other message does.
  - `01.3b` and `01.3c` are two separate implementation PRs but one atomic
    public-contract release boundary -- no supported/versioned release may be cut
    between them merging.
  - Migration policy is intentionally breaking; no aliases, no compatibility shims.
  `ai/context/protocol/compatibility.md`'s two deferred-decision sections are updated
  to record the decision as "decided, pending implementation." No protocol schema,
  fixture, Host, SDK, or app source file changed -- decision documentation only, per
  this concept's own scope.
- Prerequisites: Concept 01.2b merged (`main` @ `d4734dba`, PR #63) -- satisfied.
- PR: #64, open against `main`, under maintainer review.
- Next action: address maintainer/review findings on PR #64, then merge it. The branch
  already records `Complete` in `PLAN.md`'s status table, per the pre-PR-Complete
  workflow `PLAN.md` section 8 documents; merging PR #64 is what makes that state
  authoritative on `main`, which is what actually satisfies `01.3b`'s own stated
  dependency ("Concept 01.3a merged to `main`") -- not the branch-level `Complete`
  label by itself, and `01.3b` stays `Blocked by 01.3a` in `PLAN.md`'s status table
  until that merge happens. Per D4, Concept 02 still waits behind the entire
  01.1 -> 01.2a -> 01.2b -> 01.3a -> 01.3b -> 01.3c chain, same as Concept 03 -- do not
  unblock 02 or 03 until 01.3c actually merges. Do not begin `01.3b` on this branch.

## Completed concepts

- `01-conventions-and-changelog.md` -- merged to `main` via PR #60 (merge commit
  `8847fcdc`, 2026-09-13).
- `01.1-adapter-enum-and-constants-physical-normalization.md` -- merged to `main` via
  PR #61 (merge commit `77f31fa0`, 2026-09-13).
- `01.2a-active-documentation-and-instruction-terminology.md` -- merged to `main` via
  PR #62 (merge commit `bc86f4cc`, 2026-09-13).
- `01.2b-internal-code-test-and-tooling-terminology.md` -- merged to `main` via PR #63
  (merge commit `d4734dba`, 2026-09-13).

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
  01.3a exactly), `01.3c` (public state-authority continuity identity cutover, implements
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
- 2026-09-13 second post-merge bookkeeping (this session, planning-only): `PLAN.md`'s
  status table row for Concept 01.2a updated `In progress | #62` -> `Complete | #62`
  (PR #62 merged to `main` as `bc86f4cc`); Concept 01.2b's row updated
  `Blocked by 01.2a` -> `Planned`. `CONTEXT.md`'s Active concept, Completed concepts,
  and Handoff sections updated to match -- Concept 01.2a moved to Completed, Concept
  01.2b is now Active/next. This mirrors the same gap the first post-merge bookkeeping
  pass fixed for 01.1/01.2a: the concept's own PR (122c226a) could only record
  `In progress` before merging, and no follow-up commit had flipped it to `Complete`
  until now.
- 2026-09-13 Concept 01.2b scope-boundary correction (this session): PR #63 had
  correctly implemented the `BridgeEntity`->`HostEntity` cluster, Adapter's
  `adapter_task_marshaller.hpp` `IBridgeCallbackRegistry` cross-reference fix, and the
  tooling `BridgeBuilder`->`DovahLinkBuilder` fixture rename, but had also rewritten
  runtime/user-visible string literals (app status labels, pairing failure/rejection
  text, default display names) and public SDK diagnostic strings
  (`DovahLinkProtocolException`/`DovahLinkConnectionException.message`, exported from
  the SDK barrel) from "bridge" to "host" wording -- a behavior-neutrality violation
  this concept's own scope grants no exception for; `message` is documented as
  diagnostic-only, never branched on, so the compatibility risk is low, but the text is
  still publicly observable (logs, error UI) and the concept's rule has no low-risk
  carve-out. Two category-A comments the PR had also touched despite not being
  identifier-bound (`failures.dart`'s `PairingFailure`/`SessionInvalidatedFailure` doc
  comments, `app_test.dart`'s two inline comments) were reverted alongside the strings,
  per 01.2a's own scope rule that general architecture-narrating prose is not 01.2b's to
  edit; not fixed forward here, left for an 01.2a correction. Every reverted string was
  re-verified against a matching test assertion; `dart analyze`/`dart test` (SDK,
  623/623) and `flutter analyze`/`flutter test` (app, 349/349) re-run clean after each
  revert. `PLAN.md`'s status table row and this file's Active concept section, still
  reading `Planned`/"not yet started" despite PR #63 already existing with commits on
  its branch, are updated to `In progress | #63` to match reality -- caught here before
  merge, the same gap the 01.1/01.2a post-merge bookkeeping entries above fixed after
  merge.
- 2026-09-13 Concept 01.2b scope-boundary reversal (this session): the maintainer
  reviewed the prior scope-boundary correction above and concluded 01.2b's original
  reading -- "behavior-neutral" means no observable text may change -- was too narrow
  for the actual goal: retiring active "Bridge" terminology everywhere it safely can,
  not just in non-public identifiers. `01.2b-internal-code-test-and-tooling-terminology.md`'s
  Goal and "Public boundary exclusion" sections are rewritten: human-readable UI
  labels, log messages, diagnostic/exception message text, test descriptions, and
  architecture-describing comments/docs are now in scope for this concept, even where
  a consumer can observe them (logs, error UI, test output). The exclusion narrows to
  what is actually compatibility-bearing: `bridgeVersion`, `bridgeInstanceId`,
  JSON/wire fields, persisted keys, public SDK API identifiers a consumer compiles
  against, error codes, and CLI/env/config contracts -- unless a specific piece of
  human-readable text is itself explicitly documented or consumed as a stable external
  contract, in which case it stays excluded and defers to `01.3a`. "Behavior-neutral"
  is redefined to mean no functional/protocol/wire change, not "no text a user or
  consumer can see changes." Package-wide invariant compliance: this does not touch
  public *behavior* (protocol/security/runtime), only display/diagnostic text, so it
  does not conflict with the package-wide "preserve runtime/protocol/security/public
  behavior" invariant or with `01.3a`'s wire-vocabulary-only territory (confirmed by
  re-reading `01.3a-public-vocabulary-and-identity-semantics.md`, which governs only
  `bridgeVersion`/`bridgeInstanceId` wire semantics, not UI/diagnostic wording). The
  two strings from `48265b69`/`3410a9bd` are being restored under the corrected
  boundary, and the full inventory is being re-run to confirm no other in-scope
  occurrence was missed.

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
- 2026-09-13 Concept 01.2a / PR #62 verification (this session, implementation --
  supersedes the planning-only evidence above for this concept; base `77f31fa0` is
  unchanged, but the file count and every check below are 01.2a's own, not D4's
  planning-time estimate): base `77f31fa0`, head `39d565d3e2948b456a74f940c91ab7c444aff04d`,
  `git diff --name-only base...HEAD` = **34 files**. `python -m unittest discover -s
  tooling -p "test_*.py"`: 166/166 passed. `dart analyze` (`sdk/dart/dovahlink_client`):
  no issues found. `dart test` (same package): 623/623 passed. Final Bridge inventory
  on this concept's two prose-fix targets (`ai/context/integration/testing.md`,
  `ai/context/sdk/api-design.md`): the only remaining case-insensitive `bridge` hits
  are `Bridge/client version`, `Bridge compatibility mechanics`, `Bridge version`,
  `SDK/Bridge compatibility`, `bridgeInstanceId`, and `"Bridge is older/newer than
  supported"` -- all category D (compatibility/identity vocabulary reserved for
  `01.3a`-`01.3c`); no category-A occurrence remains. Diff audit across all 34 changed
  files: every `+`/`-` line in the 10 touched `.dart` source files is a `///`/`//`
  comment line (verified by filtering the diff for non-comment changes -- none found);
  the remaining 24 files are Markdown/YAML/Papyrus documentation, the planning
  package, and `tooling/test_repository_consistency.py`'s assertion literals. No
  identifier, signature, executable-statement, serialization, or wire-contract change
  anywhere in the diff.
- Confirmed via `git log`/`git show` that PR #62 is actually merged to `main`
  (`bc86f4cc`, merging `a23286e8` into `77f31fa0`) before marking Concept 01.2a
  `Complete` -- not inferred from the branch's own prior "implementation complete,
  awaiting merge" note, per this plan's own rule that `Complete` is authoritative only
  once GitHub's merge record confirms it (`PLAN.md` section 8).
- 2026-09-13 Concept 01.2b / PR #63 category-B inventory re-run (this session, under
  the corrected boundary from the scope-boundary reversal above): case-insensitive
  `git grep -ilI bridge` across `app/lib`, `app/test`, `sdk/dart`, `host/`, `adapter/`,
  `tooling/`, `integration/` (git-tracked files only) found 95 files. Every hit was
  classified: (A) current Host/Adapter/DovahLink meaning -> renamed; (B)
  `bridgeVersion`/`bridgeInstanceId` wire fields and their direct property/doc-comment
  bindings, plus wire fixtures -- kept, reserved for `01.3a`-`01.3c`; (C) general
  architecture-narrating comments not bound to an identifier -- kept, `01.2a`'s
  (already-merged) territory; (D) historical/migration references -- kept.
  Category-A occurrences found and fixed, all test-description/local-variable wording,
  no production string changes beyond what the scope-boundary reversal's own restore
  already covered: `pairing_remote.datasource_test.dart`, `pairing.selectors_test.dart`
  (plus its one arbitrary placeholder value, `'Bridge unavailable'` ->
  `'Host unavailable'`), `dovahlink_client_test.dart`, `pairing_service_test.dart`,
  `reconnect_rejection_classifier_test.dart`, `session_service_test.dart`,
  `envelope_test.dart` (test description and a local variable,
  `bridgeMessages` -> `hostMessages`). One out-of-category item was also fixed:
  `tooling/test_format_staged.py`'s illustrative example path `bridge/main.cpp`
  (arbitrary, unrelated to any real logic or the retired architecture) ->
  `adapter/main.cpp`, which required re-sorting one test's expected list order
  (`adapter/` now sorts before `app/` alphabetically) -- caught by re-running the
  suite, not by inspection. Explicitly NOT renamed despite superficially matching
  "bridge version" wording: `pairing_handshake.entity_test.dart`,
  `pairing.reducer_test.dart` (four occurrences), `pairing.selectors_test.dart`'s own
  test description, `pairing.state_test.dart`, and
  `authentication_service_test.dart` -- each of these describes the `bridgeVersion`
  field/property itself (verified by reading each test's body), which stays excluded
  per the corrected boundary; renaming only the prose while the field keeps its name
  would make the documentation wrong, not more correct. Zero E (ambiguous) items
  remain unresolved. Verification after all renames: `dart analyze`/`dart test`
  (`sdk/dart/dovahlink_client`) clean, 623/623; `flutter analyze`/`flutter test`
  (`app/`) clean, 349/349; `python -m unittest tooling.test_format_staged
  tooling.test_repository_consistency -v`: 62/62 passed.
- 2026-09-13 Concept 01.2b second scope-boundary reversal (this session): the
  maintainer reviewed a contradiction between the widened "Public boundary exclusion"
  (which listed "comments describing current architecture" as in-scope) and the
  still-narrow "Boundary against 01.2a" (which deferred exactly that category to
  01.2a). Resolved by broadening, not narrowing: `01.2b-internal-code-test-and-tooling-terminology.md`'s
  "Why this is a stable concept" and "Boundary against 01.2a" sections are rewritten
  so the 01.2a boundary is drawn by file ownership (01.2a owns standalone
  `ai/context/`/`roadmap/`/documentation files; 01.2b owns comments/prose inside the
  source, test, and tooling files it already owns) rather than by token type
  (identifier vs. comment). This reopens category C from the prior inventory pass,
  which had deferred general-architecture comments to 01.2a.
- 2026-09-13 Concept 01.2b category-C sweep (this session): re-read every category-C
  line individually (not just the prior pass's per-file counts) across all 92
  previously-inventoried files. Zero renames were needed in `host/` or `tooling/`:
  every host/ "bridge" occurrence is either a `bridgeVersion`/`bridgeInstanceId`
  identifier, or an exact quoted excerpt from `ai/context/protocol/security.md`/
  `protocol/schema/README.md` (host C# `<c>...</c>` doc comments quote those files
  verbatim in quotation marks; rewording the quote without the source would misquote
  it, and those docs are outside 01.2b's file scope -- flagged, not fixed);
  `tooling/test_repository_consistency.py` is the terminology-guard suite itself,
  meta-code asserting what *other* files should/shouldn't say, not itself describing
  current architecture. 21 files needed a same-meaning word swap in comments
  genuinely describing current Host behavior: `app/lib/features/pairing/data/
  datasources/pairing_remote.datasource.dart`, `.../domain/entities/
  pairing_handshake.entity.dart`, `.../domain/repositories/pairing_repository.dart`,
  `.../domain/usecases/observe_connection_status.usecase.dart`, `.../presentation/
  state/pairing.actions.dart`, `.../pairing.middleware.dart`, `.../presentation/
  widgets/pairing_loading.widget.dart`, `pairing_renotify_button.widget.dart`,
  `pairing_request_code_button.widget.dart`, `pairing_trusted.widget.dart`,
  `app/lib/shared/constants/enums.dart`, `app/lib/shared/failures/failures.dart` (the
  2 lines deferred by the first scope-boundary correction), `app/test/app/app_test.dart`
  (its 2 inline comments, same deferral), `sdk/dart/dovahlink_client/lib/src/protocol/
  capabilities_payload.dart`, `envelope.dart`, `envelope_validator.dart`,
  `protocol_timestamp_validator.dart`, `subscription_ack_payload.dart`,
  `sdk/dart/dovahlink_client/lib/src/shared/enums.dart` (10 occurrences),
  `sdk/dart/dovahlink_client/test/dovahlink_client_test.dart` (2 inline comments),
  `test/internal/requests/message_router_test.dart`. Explicitly kept, verified
  individually rather than trusted from bucket counts: `bridgeVersion`/
  `bridgeInstanceId` identifiers and their direct field doc comments; `capability.dart`
  and `host/DovahLink.Host/Client/Protocol/CapabilityDescriptor.cs`'s deliberate
  "independent of the Bridge/Host release version" phrasing (touches the unresolved
  compatibility-authority question `01.3a` Section A decides -- choosing either name
  now would prejudge that decision); `dpapi_client_storage.dart`'s "mirroring the
  Bridge's own DPAPI decision" (historical design precedent);
  `host/DovahLink.Host/Constants.cs`'s `PublicProtocolTransitionalBridgeVersion`
  constant (identifier, deliberately named for its transitional nature, out of a
  comment-only sweep's scope regardless). Two findings recorded but not fixed, both in
  `sdk/dart/dovahlink_client/test/transport/websocket_transport_test.dart`: (1) lines
  15-16's cross-reference to `websocket_transport_bridge_test.dart` is not merely
  stale wording -- that file was deleted in commit `dd16065a` ("remove Bridge-specific
  SDK real-process harness... since bridge/ no longer exists"), so the comment is now
  factually wrong, a dangling-reference bug distinct from a terminology rename; (2)
  lines 90-92's claim that the fake test server "accepts immediately, unlike the real
  Bridge (whose connection slot is only released once its own worker thread notices
  the closed socket)" describes a specific claimed implementation detail of the
  retired native Bridge's threading model, which may not hold for the current C# Host
  -- renaming the word without knowing whether the underlying claim still holds would
  risk turning a correct historical statement into an incorrect current one; left as
  category E (ambiguous), not renamed. Verification after this sweep: `dart analyze`/
  `dart test` (`sdk/dart/dovahlink_client`) clean, 623/623; `flutter analyze`/
  `flutter test` (`app/`) clean, 349/349; `python -m unittest
  tooling.test_repository_consistency -v`: 40/40 passed.
- 2026-09-13 Concept 01.2b `websocket_transport_test.dart` comment cleanup (this
  session): closes both findings the category-C sweep above left flagged rather than
  fixed. (1) Lines 15-16's file-header comment no longer cross-references the deleted
  `websocket_transport_bridge_test.dart`; it now states only what this suite actually
  proves ([WebSocketTransport]'s connection-lifecycle and framing mechanics against
  [FakeWebSocketServer]), with no reference to a file that no longer exists. (2) Lines
  90-92's comparison to "the real Bridge (whose connection slot is only released once
  its own worker thread notices the closed socket)" is removed rather than reworded to
  "Host" -- that implementation detail was never verified against the current C# Host,
  so deleting the unverifiable claim was correct where a word-swap would have asserted
  something unproven. Neither fix touched `bridgeVersion`/`bridgeInstanceId` or any
  legitimate historical Bridge reference. Verification: `dart analyze`/`dart test`
  (`sdk/dart/dovahlink_client`) clean, 623/623; `python -m unittest
  tooling.test_repository_consistency -v`: 40/40 passed.
- 2026-09-13 third post-merge bookkeeping (this session, planning-only, committed
  directly to `main` -- no branch, per the same "in-progress -> complete is bookkeeping,
  not a feature branch" rule the first two post-merge passes established): `PLAN.md`'s
  status table row for Concept 01.2b updated `In progress | #63` -> `Complete | #63`
  (PR #63 merged to `main` as `d4734dba`); Concept 01.3a's row updated
  `Blocked by 01.2b` -> `Planned`. `CONTEXT.md`'s Active concept, Completed concepts,
  and Handoff sections updated to match -- Concept 01.2b moved to Completed, Concept
  01.3a is now Active/next.
- 2026-09-13 Concept 01.3a implementation (this session, branch
  `docs/01.3a-public-vocabulary-and-identity-semantics`, decision-only -- no
  protocol schema, fixture, Host, SDK, or app source file touched):
  `01.3a-public-vocabulary-and-identity-semantics.md` gets a `**Decision:**` block
  under each of Sections A-F. Grounded against `ai/context/common.md`'s Versioning
  section (single repo `VERSION` file, no independent Host-only version literal
  exists today), `host/DovahLink.Host/Client/Protocol/HelloAckPayload.cs` and
  `Constants.cs` (current `BridgeVersion`/`PublicProtocolTransitionalBridgeVersion`
  naming), `host/DovahLink.Host/Identity/AdapterInstanceId.cs` and
  `ARCHITECTURE.md`'s "Runtime and identity model" (the four fixed private identity
  lifetimes, and the Host OS process's explicit lack of public identity), and
  `ARCHITECTURE.md`'s "Authoritative state and revisions" (the Host owns one
  authoritative state store per play context). Key reasoning: Section C's new
  `stateAuthorityId` is deliberately not a rename of `adapterInstanceId`, because a
  Host-process restart can invalidate the Host's own in-memory state store even when
  the underlying Adapter/Skyrim process never restarted -- an `adapterInstanceId`-only
  comparison would miss that case, which is exactly what
  `ai/context/protocol/compatibility.md`'s pre-existing "do not substitute
  `adapterInstanceId` ... for it" line already ruled out without stating why.
  `ai/context/protocol/compatibility.md`'s "Decided, not yet activated" and "Deferred:
  public instance identifier" sections are rewritten to record the decision as
  "decided, pending implementation in 01.3b/01.3c," cross-referencing this concept
  file's Sections A and C. Real changed-file counts for the two implementation
  concepts were re-measured (`git grep -il` across `protocol/fixtures/**`,
  `sdk/dart/**`, `host/**`, `app/**`, `integration/**`): `bridgeVersion` -> 39 files
  (`01.3b`), `bridgeInstanceId` -> 74 files (`01.3c`) -- both comfortably under the
  80-file re-plan threshold, no split proposal needed (supersedes this concept's own
  pre-decision ~10-15/~75-90 estimate). A fresh-eyes decision-gap review (Explore
  subagent, read-only) found zero remaining "TBD" rows, no contradiction between this
  file and `compatibility.md`, and no unflagged contradiction with `ARCHITECTURE.md`
  or `protocol/schema/README.md`.

## Handoff

Concept 01.2b merged to `main` via PR #63 (merge commit `d4734dba`, 2026-09-13). Both
scope-boundary reversals, the restored Host wording, and two full category-B/C
inventory re-runs are recorded above with zero-unresolved evidence -- the
`websocket_transport_test.dart` findings were fixed rather than left flagged; the only
remaining out-of-scope item is the `ai/context/protocol/security.md`-quoting host/
comments' own source doc, which stays outside this concept's file scope by design.
Handoff to Concept 01.3a is now active, per `PLAN.md` section 6 (one branch/PR per
concept, dependent concept waits for merge, not just open/approved) -- 01.3a's own
prerequisite (01.2b merged) is satisfied.

Concept 01.3a's six mandatory decisions (A-F) are now resolved on branch
`docs/01.3a-public-vocabulary-and-identity-semantics`, not yet opened as a PR --
see the Verification entry above for the full rationale and the concept file itself
for the decision text. Handoff to Concept 01.3b follows once this PR merges to
`main`, per the same one-branch/PR-per-concept rule.
