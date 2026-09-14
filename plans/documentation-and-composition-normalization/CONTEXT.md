# Current phase context

Source: `plans/documentation-and-composition-normalization/SOURCE.md`
Source fingerprint: SHA-256
`6b09702f2b038be5c5d1d1c81048f9c2cce5b7e079c45797bc77c49ed59fea5f`
Phase: Documentation and composition normalization
Package: `plans/documentation-and-composition-normalization/`
Status: active (package frozen 2026-09-13)

## Active concept

- File: `02-host-composition-and-di-lifetimes.md`
- Status: Complete on branch `feature/02-host-composition-and-di-lifetimes`, not yet opened as a
  PR. Prerequisite (Concept 01.3c merged to `main`) confirmed via `git log` (merge commit
  `3768c1e0`, PR #66), not inferred from a prior label.
- First implementation pass (manual composition, six reviewable step-build steps) introduced
  `Composition/CoreServiceExtensions.cs`, `TrustServiceExtensions.cs`,
  `AdapterIpcServiceExtensions.cs`, `PublicClientServiceExtensions.cs` as static `Compose*` methods
  returning `*Services` records, plus `Process/DovahLinkHostRuntime.cs` as the explicit lifecycle
  orchestrator R2.7 requires. That pass recorded one known, deliberate gap: R2.9's public-side
  connection/reconnect-isolation bullet was structurally guaranteed (each connection-owned
  collaborator constructed fresh by an inline connection-factory lambda) but not independently
  wire-observable, since no state area was registered yet.
- Second pass (this session, maintainer-directed scope expansion, see D8): closed that gap and
  went further, per the maintainer's explicit instruction that stable Host dependencies use real
  dependency injection throughout, not just the R2.9 proof. Eight reviewable step-build steps: (1)
  extracted `Client/Transport/PublicConnectionFactory.cs` (`IPublicConnectionFactory`) from the
  public-client inline connection lambda; (2) extracted `Adapter/Ipc/AdapterConnectionFactory.cs`
  (`IAdapterConnectionFactory`) from the adapter-IPC inline connection lambda; (3) one atomic
  cutover of all four composition modules from manual `Compose*`/`*Services`-record methods to
  `Microsoft.Extensions.DependencyInjection` `IServiceCollection` registration extensions
  (`AddCoreServices`/`AddTrustServices`/`AddAdapterIpcServices`/`AddPublicClientServices`), plus new
  `HostRuntimeServiceExtensions.AddHostRuntime`, plus `Program.ComposeAndRunAsync` rewritten to
  build one `ServiceCollection`/`ServiceProvider` -- done as one step because the four modules'
  shared `CoreServices`/`TrustServices`/`AdapterIpcServices`/`PublicClientServices` records were
  exactly what DI replaced, so no independently compiling intermediate state existed between
  converting one module and converting all four; (4)-(5) the R2.9 proof tests
  (`PublicClientConnectionLifetimeTests.cs`: outbound-isolation, reconnect/replay-state isolation);
  (6) fixed two stale test doc comments claiming production `Main` defaults to no public listener
  (`Main`'s own `ResolvePublicListenerPort` always resolves a real port; only test code calling
  `ComposeAndRunAsync`/`DovahLinkHostRuntime` directly can omit one); (7) this close-out, including
  removing the first pass's now-closed R2.9 gap note above and adding Concept 02's own R2.1-R2.10
  traceability table.
- `ProgramCompositionTests.cs` and `DovahLinkHostRuntimeTests.cs` required **zero** edits across the
  entire DI migration (confirmed via `git diff --stat` immediately after the migration step) --
  `Program.ComposeAndRunAsync`'s public signature and every observable behavior (fail-closed
  ordering, exception types, rendezvous line ordering) are unchanged; only the internal composition
  *mechanism* changed.
- `ValidateOnBuild` is deliberately not set when building the `ServiceProvider`: it eagerly
  constructs every registered service at build time and wraps any resulting exception in
  `AggregateException`, which would have turned `ProgramCompositionTests`' expected direct
  `SocketException`/`InvalidDataException` into a wrapped one. Resolving `DovahLinkHostRuntime`
  once, immediately after building the provider, achieves the same fail-fast-at-startup goal
  through ordinary lazy singleton resolution, which propagates exceptions directly -- documented
  inline in `Program.cs` and in `HostRuntimeServiceExtensions.cs`.
- Final acceptance gate (this session's second pass): `dotnet build ... -p:GenerateDocumentationFile=true
  -p:TreatWarningsAsErrors=true` clean; `dotnet test host/DovahLink.Host.Tests` 1771/1771 passed
  (1763 first-pass baseline + 8 new); `python -m unittest discover -s tooling -p "test_*.py"`
  170/170 passed, unaffected. Whole-branch changed-file count vs. `main` (`git merge-base HEAD main`
  = `3768c1e0`, then `git diff --name-only base...HEAD`): **27 files** -- comfortably under both the
  80 re-plan threshold and the 100 hard stop (up from the first pass's 22, net of four `*Services.cs`
  records created and later deleted within this same branch, which cancel out against `main`).
  While verifying this pass, the suite twice hit an unrelated, pre-existing flake in tests that
  read the real per-Windows-user DPAPI trust-store file with no cross-class serialization
  protecting them from other tests/processes doing the same (confirmed absent from a clean
  `git worktree` checkout of this branch's own base commit, and traced on this run to an orphaned
  `vstest.console` process from an earlier invocation still holding the file open); not a defect in
  this concept's own composition/DI work, and a follow-up task was filed separately rather than
  fixed here (out of this concept's file scope).
- R2.9's previously recorded gap is closed: `PublicClientConnectionLifetimeTests.cs` proves both
  outbound-state isolation between two simultaneously accepted connections and that a reconnect
  under the same persistent `clientId` and the identical `messageId` gets a fresh session rather
  than being rejected as replayed -- proving fresh connection state, fresh replay state, a new
  session, and that persistent `clientId` retains no connection-scoped state, all in one test. No
  known Concept 02 gap remains. See the concept file's own R2.1-R2.10 traceability table for the
  full per-requirement mapping.
- Next action: maintainer review, then open the PR. Once merged, Concept 04 (Host documentation
  sweep) becomes eligible to start, per `PLAN.md` section 6's merge-not-just-complete rule; Concept
  03 (Adapter composition) remains independently eligible regardless, per its own dependency on
  `01.1`/`01.3c` only.

## Completed concepts

- `01-conventions-and-changelog.md` -- merged to `main` via PR #60 (merge commit
  `8847fcdc`, 2026-09-13).
- `01.1-adapter-enum-and-constants-physical-normalization.md` -- merged to `main` via
  PR #61 (merge commit `77f31fa0`, 2026-09-13).
- `01.2a-active-documentation-and-instruction-terminology.md` -- merged to `main` via
  PR #62 (merge commit `bc86f4cc`, 2026-09-13).
- `01.2b-internal-code-test-and-tooling-terminology.md` -- merged to `main` via PR #63
  (merge commit `d4734dba`, 2026-09-13).
- `01.3a-public-vocabulary-and-identity-semantics.md` -- merged to `main` via PR #64
  (merge commit `805d1641`, 2026-09-14). Design-only gate; all six decisions (compatibility
  authority, canonical version vocabulary, `stateAuthorityId` semantics, cache/revision
  scope, migration policy, old->new vocabulary table) final -- see this file's prior
  Verification entries for the full rationale.
- `01.3b-compatibility-version-vocabulary-cutover.md` -- merged to `main` via PR #65
  (merge commit `5f8ca28d`, 2026-09-14). Implemented `01.3a`'s Sections A/B decision
  (`bridgeVersion` -> `hostVersion`) across Host, canonical schema, Dart SDK, and the
  Flutter app; two correction passes (a 5-file documentation gap, and D5/the
  VERSION-invariant/bookkeeping pass) are recorded in this file's earlier Verification
  entries. Did not touch `bridgeInstanceId`/`stateAuthorityId` -- that was `01.3c`'s
  field, deliberately left alone.
- `01.3c-public-authoritative-instance-identity-cutover.md` -- merged to `main` via PR #66
  (merge commit `3768c1e0`, 2026-09-14). Implements exactly `01.3a`'s Section C decision across seven
  reviewable steps: (1) package bookkeeping fixing PR #65's merge lag in `PLAN.md`/
  `CONTEXT.md`; (2) a new internal `StateAuthorityLifecycle` rotation state machine
  (mint at Host startup fail-closed, rotate exactly once per detected continuity
  break, repeated-loss lock-down until a fresh baseline, fatal on runtime mint
  failure) plus a new `Resynchronized` event on `IAdapterAvailabilityTracker` it
  observes, wired into `Program.cs` with no wire change yet; (3) the Host wire cutover
  itself -- `PublicEnvelope`/`PublicEnvelopeCodec` stamp `stateAuthorityId` on
  `hello_ack`/`state_snapshot`/`state_event` only (encode fails closed with no
  lifecycle configured) and omit it from every other message per the closed
  wire-presence table, all 57 `protocol/fixtures/**/*.json` fixtures renamed or
  stripped accordingly, `tooling/validate_protocol_fixtures.py` updated to match; (4)
  the Dart SDK mirror (`Envelope`/`EnvelopeValidator`, `@JsonKey(includeIfNull:
  false)` so outgoing client envelopes never carry the key at all); (5) the Adapter's
  one real-process E2E test file's hardcoded wire literals; (6) documentation cutover
  across `ARCHITECTURE.md`, the canonical schema, and five `ai/context`/roadmap docs,
  plus `tooling/test_repository_consistency.py`'s pinned strings -- recording D6 for
  one roadmap bullet (`roadmap/10`, Status: Planned) that could not be mechanically
  renamed because `01.3a` Section C rules `stateAuthorityId` out for instance-identity
  purposes; (7) this close-out. Every fresh-eyes test-gap pass (Host, Dart) found and
  fixed real gaps (a missing subscriber-exception-isolation fix, missing per-gated-type
  empty/missing-field tests) before this concept's own acceptance gate ran.
- Independent maintainer review (this session, after the above seven steps) found two
  real defects the concept's own mandatory invariants require proving but the
  implementation did not: `PublicStateSubscription` never invalidated a connection's
  live baseline on a `stateAuthorityId` rotation (only a play-context transition did),
  leaving the post-rotation baseline rule unenforced; and the Dart SDK could not
  distinguish an absent `stateAuthorityId` key from one present with an explicit JSON
  `null`, disagreeing with the Host's own wire-presence check. Both are now fixed:
  `StateAuthorityLifecycle` raises a new `Rotated` event that `PublicStateSubscription`
  listens for to reset every area to `AwaitingBaseline`, sharing the same invalidation
  path a play-context transition already used; `Envelope.fromJson` now records
  `json.containsKey('stateAuthorityId')` and `EnvelopeValidator.validate` rejects a
  present-but-null key on every message type that must omit it entirely. See the
  Verification entry below for the fresh-eyes gaps this fix pass itself found and
  closed, and `DIVERGENCES.md` D7 for the file-count consequence.
- Re-run PR-size inventory: 86 predicted files (81-100 "stop for maintainer review"
  band); the maintainer explicitly approved one atomic PR rather than a split, since
  no safe split point exists for one shared envelope field. Final actual count: **102
  files**, over the concept's own 100-file hard stop -- the maintainer explicitly
  approved this as a one-time exception per `DIVERGENCES.md` D7, since the two
  additional files (`PublicStateSubscription.cs`, a new `FakeStateAuthorityLifecycle.cs`
  test double) were required to fix the mandatory-invariant defects above, not new
  scope. See the concept file's own PR-size gate section for the full accounting.
- Prerequisites: Concept 01.3b merged (`main` @ `5f8ca28d`, PR #65) -- confirmed via
  `git log`, satisfied. No release was cut between `01.3b` merging and this concept's
  completion (`VERSION` still `0.3.3`, `CHANGELOG.md`'s newest section still
  `[0.3.3]`, no newer git tag).
- PR #66 merged to `main` as `3768c1e0` (confirmed via `git log`, not inferred from the
  branch's own prior `Complete` label). This was the last concept in the D4 chain --
  Concepts 02 (Host composition) and 03 (Adapter composition, also requiring Concept
  01.1 merged, already satisfied) are now both eligible to start, per `PLAN.md`
  section 6's merge-not-just-complete rule. Concept 02 is the one now active, above.

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
- D6 (2026-09-14, this session, during `01.3c`'s documentation-cutover step):
  `roadmap/10-multi-bridge-and-local-discovery-foundation.md`'s "Validate records
  against authenticated `bridgeInstanceId`" bullet (Status: Planned) is reworded
  rather than mechanically renamed to `stateAuthorityId` -- `01.3a` Section C rules
  that field out for instance-identity purposes, so a blind rename would bake an
  undecided design choice into a phase nobody has designed yet. Stage 10's own
  identity mechanism remains an open question for whoever implements it.
- D7 (2026-09-14, this session, independent maintainer review of `01.3c`'s already-
  `Complete` implementation): the review found `PublicStateSubscription` never
  invalidated a live baseline on `stateAuthorityId` rotation and the Dart SDK could not
  tell an absent `stateAuthorityId` key apart from an explicit JSON `null` -- both real
  defects in invariants `01.3c` itself requires proving, not new scope. Fixing them
  touched two files the branch had not touched before (`PublicStateSubscription.cs`, a
  new `FakeStateAuthorityLifecycle.cs` test double), raising the final changed-file
  count from an already-stale "99" to 102 -- over `01.3c`'s own 100-file hard stop. The
  maintainer explicitly approved 102 as a one-time exception for this PR rather than
  splitting the fix, removing tests, or reopening `01.3a`'s design. The package-wide
  `>100` rule itself is unchanged for every other concept.
- D8 (2026-09-14, this session, Concept 02, maintainer-directed scope expansion): the
  maintainer, after being shown the conflict, explicitly chose to introduce
  `Microsoft.Extensions.DependencyInjection` as the Host's real composition mechanism
  and to promote the public/adapter connection-construction lambdas into production
  `IPublicConnectionFactory`/`IAdapterConnectionFactory` classes -- overriding
  `02-host-composition-and-di-lifetimes.md`'s own Design-section text ("do not
  introduce a factory/aggregate abstraction solely to satisfy this example when the
  audit finds nothing that needs it") and its narrow "Files this concept may change"
  list. This is recorded as an approved divergence, not a silent scope change. Two
  things ground the decision beyond the maintainer's direct instruction: (1)
  `ai/context/dotnet/csharp-style.md`'s pre-existing, concept-independent rule --
  "every collaborator is supplied through constructor injection; do not construct or
  resolve a behavior-bearing collaborator inside another class" -- which the current
  inline `stream => new PublicWebSocketConnection(..., new PublicHelloAdmissionHandler(...))`
  lambdas already violate, regardless of this concept's DI question; (2) both
  `PublicWebSocketListener` and `AdapterIpcListener` already take a
  `Func<Stream, TConnection>` factory delegate, so wrapping that in a named factory
  class is a small, low-risk seam rather than a structural redesign. The concept's own
  non-negotiable invariants (fail-closed async trust/security bootstrap strictly before
  either listener is exposed, no sync-over-async, no service locator, one root
  provider, `DovahLinkHostRuntime` remains the explicit lifecycle owner rather than
  `IHostedService`) are preserved exactly; only the composition *mechanism* changes,
  from manual `Compose*` static methods to `IServiceCollection` registration modules.
  `02-host-composition-and-di-lifetimes.md`'s `Status` line is set to `In progress` for
  the duration of this work (see that file), not flipped straight to `Complete`, per
  this plan's own rule that `Complete` requires every R2.x bullet traced to a specific
  file/test first.

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
- 2026-09-14 Concept 01.3b implementation (this session, branch
  `feature/01.3b-compatibility-version-vocabulary-cutover`): implements `01.3a`'s
  Sections A/B exactly, across six reviewable commits (Host model; canonical
  schema/fixtures/tooling guard; Dart SDK model/codegen; Flutter app pairing
  consumers; documentation/tooling-guard cutover; this close-out) -- see the Active
  concept entry above for the full file-by-file breakdown. Acceptance-gate re-run of
  all four suites plus the tooling guard: `dotnet test
  host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj` 1717/1717 passed; `dart
  analyze`/`dart test` (`sdk/dart/dovahlink_client`) clean, 623/623 passed; `flutter
  analyze`/`flutter test` (`app/`) clean, 349/349 passed; `python -m unittest discover
  -s tooling -p "test_*.py"` 166/166 passed (includes the fixture validator confirming
  57 fixtures still satisfy the envelope contract). Whole-branch changed-file count vs.
  `main` (`git merge-base HEAD main` = `805d1641`, then `git diff --name-only
  base...HEAD`): **48 files** -- comfortably under both the 80 re-plan threshold and
  the 100 hard stop, and below `01.3a`'s own 39-plus-doc-tail prediction only because
  that prediction excluded the doc/tooling tail it flagged separately, all of which
  landed within these 48. Repo-wide sweep (`git grep -lI "bridgeVersion\|BridgeVersion"`)
  confirms every remaining hit is genuine history (`CHANGELOG.md`, this package's own
  `plans/*.md` decision records, `roadmap/*.md` -- out of this concept's file scope
  per `PLAN.md`'s package boundary) or this concept's own prose explaining the rename
  in `tooling/test_repository_consistency.py`'s comments -- zero live identifier or
  requirement-bearing prose reference remains. `bridgeInstanceId` is untouched
  everywhere, confirmed still present and unchanged, reserved for `01.3c`.
- 2026-09-14 Concept 01.3b correction pass (this session, same branch): an independent
  review correctly flagged that this concept's earlier evidence entry above missed 5
  more files carrying the same active "Bridge-version compatibility" wording --
  `sdk/README.md` (2 occurrences), `roadmap/05-dart-client-sdk-foundation.md` (10
  occurrences), `app/README.md`, and `protocol/README.md` -- the last of which was a
  live factual error (present-tense), not just terminology, the same bug class as the
  `ARCHITECTURE.md` sentence the prior pass already caught. The same review also
  correctly rejected its own initial claim that 01.3b was "blocked" on missing SDK-side
  Host-version range enforcement: `sdk/README.md` already documents that enforcement as
  Stage 5's own undone scope (`git grep -n "incompatib" -- sdk/dart/dovahlink_client/lib/
  app/lib/` returns zero hits, on `main` too, confirming no such mechanism was ever
  built, not something this rename regressed), and `01.3a` never decided a concrete
  range/exception-type/enforcement-point design for 01.3b to implement -- building one
  here would be new protocol implementation outside this concept's scope, which
  `AGENTS.md`'s non-negotiable rules and this concept's own Non-goals both forbid. Fixed
  the 5 files plus their two dependent test guards in
  `tooling/test_repository_consistency.py`
  (`test_sdk_readme_documents_the_phase_5_pull_forward_and_the_real_package`,
  `test_app_and_protocol_docs_reconcile_the_sdk_boundary`); closed out
  `01.3b-compatibility-version-vocabulary-cutover.md` itself in place (`Status: pending`
  -> `Complete`, filled the "Required proof before editing" template, rewrote the two
  proof-obligation bullets that had claimed SDK range validation and too-old/too-new
  behavior were "tested" -- factually false today -- to instead state plainly that this
  concept preserves current pass-through behavior and defers enforcement to Stage 5,
  and updated "Files this concept may change" to the real final list). Final re-run
  acceptance gate: `dotnet test` 1717/1717, `dart analyze`/`test` clean/623/623,
  `flutter analyze`/`test` clean/349/349, `python -m unittest discover -s tooling -p
  "test_*.py"` 166/166, all re-passed after the fix; whole-branch changed-file count vs.
  `main` (`805d1641`): **52 files**, still comfortably under both gates. Fresh
  branch-wide sweep (`git grep` for "Bridge version"/"Bridge compatibility"/etc. across
  `.md`/`.dart`/`.cs`/`.py`) confirms every remaining hit is genuinely historical, an
  intentional `assertNotIn` regression guard, or this concept's own before/after
  documentation of the rename -- zero live stale reference remains.
- 2026-09-14 Concept 01.3b governance/bookkeeping correction pass (this session, same
  branch, three steps): a second independent review raised two further points and
  confirmed PR #65 is genuinely open (draft). (1) Recorded `DIVERGENCES.md` D5: `01.3a`
  Section A's "mechanism unchanged from today" claim is factually imprecise (no such
  SDK-side enforcement mechanism exists anywhere in this codebase, `main` included);
  D5 documents the correction and states `01.3b` implements the authority/vocabulary
  rename only, deferring enforcement to Stage 5, rather than `01.3b`'s own file
  asserting that correction on its own authority as the prior pass had done. `PLAN.md`
  section 10 updated to name D5; `01.3b`'s proof-obligation bullet now cites D5. (2)
  Hardened `test_version_literals_match_the_published_release` to assert the exact
  `public const string PublicProtocolHostVersion = "{version}";` declaration in
  `Constants.cs` (previously unchecked -- the test only verified `adapter/vcpkg.json`
  and 2 of 3 hello-ack fixtures) and added the third fixture,
  `hello-ack-paired.json`, to the fixture loop. (3) Bookkeeping: `PLAN.md`'s 01.3b row
  PR column `--` -> `#65`; `01.3b`'s own Status line and `CONTEXT.md`'s Active-concept
  section updated from "not yet opened as a PR" to "PR #65, open (draft)"; the
  concept file's own "final count vs. `main`" completion-criteria line corrected from
  the prior pass's 52 to the true current **54 files** (D5 itself added
  `DIVERGENCES.md` as a new file to the diff, which the prior pass's count predated --
  the dated Verification entry above recording 52 is left untouched, since it
  correctly describes that earlier pass's own state, not this one's). Final
  acceptance gate, this pass touching only `DIVERGENCES.md`, `PLAN.md`, the `01.3b`
  concept file, `CONTEXT.md`, and `tooling/test_repository_consistency.py` (confirmed
  via `git status` before commit -- zero runtime Host/SDK/App source file changed):
  `dotnet test` 1717/1717, `dart analyze`/`test` clean/623/623, `flutter
  analyze`/`test` clean/349/349, `python -m unittest discover -s tooling -p
  "test_*.py"` 166/166, all green. Fresh sweep for "52 files"/"not yet opened as a
  PR" confirms only the intentionally-preserved historical entry remains.
- 2026-09-14 Concept 01.3c implementation, seven steps (this session, branch
  `feature/01.3c-public-authoritative-instance-identity-cutover`): implemented
  `01.3a` Section C's `stateAuthorityId` continuity-epoch identity exactly, per the
  Active-concept entry above's full step breakdown. Two fresh-eyes test-gap passes
  (Host, Dart) each found and fixed real gaps before this concept's own tests were
  considered complete -- a missing `FatalFailureOccurred` subscriber-exception
  isolation fix (mirroring `AdapterAvailabilityTracker.PublishTransition`'s own
  documented discipline) plus two missing lock-down/concurrency tests on the Host
  side; three missing per-gated-type empty/missing-`stateAuthorityId` tests on the
  Dart side. One maintainer decision point mid-implementation: the re-run PR-size
  inventory came in at 86 predicted files (81-100 "stop for maintainer review" band,
  not "comfortably under 80"); the maintainer explicitly approved one atomic PR
  rather than any split. One documentation gap required its own maintainer decision:
  `roadmap/10-multi-bridge-and-local-discovery-foundation.md`'s discovery-record
  identity bullet could not be mechanically renamed (`01.3a` Section C rules
  `stateAuthorityId` out for instance-identity purposes) -- reworded and recorded as
  D6 rather than silently deciding a new, undesigned mechanism. Final acceptance
  gate, full branch-wide: `dotnet test` 1740/1740, `dart analyze`/`test`
  clean/628/628, `flutter analyze`/`test` clean/349/349, `python -m unittest
  discover -s tooling -p "test_*.py"` 166/166 plus `test_validate_protocol_fixtures`
  bringing the total to 170/170 (57/57 fixtures satisfy the envelope contract), and
  the Adapter's native `ctest --preset windows-x64-debug` 477/477 (2 skips,
  pre-existing/unrelated to this concept -- they require a Release-configuration
  adapter build). Final changed-file count vs. `main`: 99, under the 100 hard stop.
  Fresh repo-wide sweep (`git grep -n "bridgeInstanceId"`) confirms every remaining
  hit is genuinely historical (frozen-reference `ARCHITECTURE.md` prose,
  `CHANGELOG.md`, this package's own decision records) or this concept's own
  before/after documentation of the rename -- zero live identifier or
  requirement-bearing prose reference remains anywhere in the repository.
- 2026-09-14 `01.3c` review-fix pass (this session, same branch, after the concept had
  already recorded itself `Complete` above): an independent maintainer review found two
  real defects in invariants `01.3c` itself lists as mandatory to prove. (1)
  `PublicStateSubscription` reacted to `feed.EventOccurred`/`SnapshotChanged` and
  `playContextTracker.Transitioned` but never to a `stateAuthorityId` rotation, so an
  already-`Live` area's baseline stayed live across a rotation with no invalidation --
  the concept's own "a rotation invalidates incremental continuity from the previous
  value until a fresh `state_snapshot` establishes the new baseline" invariant was
  unenforced. Fixed in two steps: `IStateAuthorityLifecycle` gained a `Rotated` event,
  raised exactly at the rotation point (mirroring `FatalFailureOccurred`'s per-
  subscriber exception isolation); `PublicStateSubscription` now takes it as a
  constructor dependency and resets every area to `AwaitingBaseline` on `Rotated`,
  sharing the invalidation logic already extracted from `OnPlayContextTransitioned`
  into a common `InvalidateAllAreasUnderGate()` helper. (2) The Dart SDK's
  `Envelope.fromJson` decoded `stateAuthorityId` as a nullable field without ever
  recording whether the JSON key itself was present, so `EnvelopeValidator`'s
  forbidden-on-non-gated-types check (`stateAuthorityId != null`) could not
  distinguish a genuinely absent key from one present with an explicit `null` --
  silently accepting a shape the Host's own `PublicEnvelopeCodec.TryGetStateAuthorityId`
  already rejects. Fixed by adding a `stateAuthorityIdPresent` parameter, derived from
  `json.containsKey('stateAuthorityId')` at the one real call site, checked separately
  from the decoded value's nullness. Fixing the Dart bug surfaced 8 pre-existing test
  literals (5 in `dovahlink_client_test.dart`, 3 in `envelope_test.dart`) that only
  passed because of it; all 8 fixed to the now-correct shape. Two fresh-eyes test-gap
  passes (Host, Dart) each found and fixed real gaps before this fix pass's own tests
  were considered complete -- three missing `Rotated`-event assertions on already-
  existing lock-down/repeated-loss/concurrency tests, and one missing symmetric
  `Unsubscribe` assertion extending `Unsubscribe_AfterBind_RemovesSubscriptions` to
  cover the new collaborator; no gaps found in the Dart fix beyond what was already
  covered. A third, narrower finding -- a theoretical race between authority rotation
  and event delivery -- was investigated and found to have no executable path today:
  `IStatePublicationFeed` has no real producer yet (still `NullStatePublicationFeed`),
  `StatePublisher.ApplyCore` gates every event on adapter availability checked before
  disconnection even triggers rotation, and `AdapterIpcConnection.ReadLoopAsync`
  processes one adapter connection's frames as one strictly sequential async chain.
  Recorded as a documented constraint on `IStatePublicationFeed`'s own contract for
  whoever implements the real domain-feed producer in a later concept, not a defect in
  `01.3c`. Final acceptance gate for this fix pass: `dotnet test` 1746/1746 (Host,
  full suite, including the exact CI command --
  `--configuration Release -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true`
  -- clean), `dart analyze`/`test` clean/630/630 (Dart SDK, full suite, including a
  `dart run build_runner build` confirming zero generated-source drift); App/tooling/
  Adapter suites unchanged from the entry above since no file in those areas was
  touched. Final changed-file count vs. `main`: 102, over the concept's own 100-file
  hard stop -- see `DIVERGENCES.md` D7 for the maintainer's explicit one-time exception.

## Handoff

Concept 01.2b merged to `main` via PR #63 (merge commit `d4734dba`, 2026-09-13). Both
scope-boundary reversals, the restored Host wording, and two full category-B/C
inventory re-runs are recorded above with zero-unresolved evidence -- the
`websocket_transport_test.dart` findings were fixed rather than left flagged; the only
remaining out-of-scope item is the `ai/context/protocol/security.md`-quoting host/
comments' own source doc, which stays outside this concept's file scope by design.

Concept 01.3a merged to `main` via PR #64 (merge commit `805d1641`, 2026-09-14). Its
six mandatory decisions (A-F) are final -- see the Verification entry above for the
full rationale and the concept file itself for the decision text. Handoff to Concept
01.3b is now active, per `PLAN.md` section 6 (one branch/PR per concept, dependent
concept waits for merge, not just open/approved) -- 01.3b's own prerequisite (01.3a
merged) is confirmed satisfied by the merge commit above, not inferred from the prior
branch-level `Complete` label.

Concept 01.3b's implementation is complete on branch
`feature/01.3b-compatibility-version-vocabulary-cutover`, including two correction
passes -- the 5-file documentation gap an independent review found, and the
D5/VERSION-invariant/bookkeeping pass recorded in the Verification entry below -- see
the Verification entries above and below for the full acceptance-gate evidence. PR
#65 merged to `main` (merge commit `5f8ca28d`, 2026-09-14). Handoff to Concept 01.3c
is now active on branch `feature/01.3c-public-authoritative-instance-identity-cutover`,
per the same one-branch/PR-per-concept rule -- `01.3c`'s own prerequisite (01.3b
merged) is confirmed satisfied by the merge commit above. Its re-run file-count
inventory came in at 86 files (73 in the core implementation dirs, plus 7
mechanically-required doc/tooling files, plus 6 live-prose-consistency files --
`roadmap/02` and `roadmap/03` among them, since `tooling/test_repository_consistency.py`
pins exact wording from both against the old field name), landing in `01.3c`'s own
81-100 "stop for maintainer review" band rather than "comfortably under 80." The
maintainer explicitly approved proceeding as one atomic PR rather than any split, per
the gate's own escape hatch and `01.3a` Section E's ban on splitting one wire-contract
change across PRs.

Concept 01.3c's implementation merged to `main` via PR #66 (merge commit `3768c1e0`) --
see the Verification entry above for the full acceptance-gate evidence and the
Completed-concepts entry for the step-by-step breakdown. This was the last concept in
the D4 vocabulary-normalization chain. Handoff to Concept 02 (Host composition and DI
lifetimes) is now active on branch `feature/02-host-composition-and-di-lifetimes`, per
the same one-branch/PR-per-concept rule -- `02`'s own prerequisite (01.3c merged) is
confirmed satisfied by the merge commit above, not inferred from the branch's own prior
`Complete` label. Concept 03 (Adapter composition, also requiring Concept 01.1 merged,
already satisfied) is independently eligible to start as well, per `PLAN.md` section 6,
but is not the one this session is picking up.
