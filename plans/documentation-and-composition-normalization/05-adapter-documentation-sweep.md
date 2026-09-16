# Concept 05 -- Adapter documentation and member organization sweep

**Status:** Complete on branch `feature/05-adapter-documentation-sweep`, not yet
opened as a PR. See this file's own R5.1-R5.10/R5.6b traceability table below for the
per-requirement evidence.

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

### R5.1-R5.10, R5.6b traceability

**Content sweep** (base `main` @ `f6e63530de7af14059a7490bc3d60478b68af091`, PR #69
merged, through head `d427c6c1`, 8 commits, every one typed `docs(adapter)`):
`git diff --name-only main...d427c6c1 -- adapter/` = **21 files**. Real content change
(formatting-reflow noise from the staged-code pre-commit hook excluded via `git diff
-b`) = **199 insertions / 243 deletions** across those 21 files. Executed via
`/step-build`: an 11-step plan (module-by-module survey) with a fresh-eyes review and
a full rebuild+`ctest` run after every step, plus 3 repeated runs of the IPC test group
after the highest-risk step (session/connection/socket) to rule out concurrency
flakiness.

**Close-out and correction bookkeeping** (`aa44db1f`, `a57e3421`, `33619155`, and this
commit): `aa44db1f` synced Concept 04/05 status after PR #69 merged. `a57e3421`
recorded this concept's first close-out pass, which described the two Bridge-specific
structural regression guards below as an intentional, permanent exception. `33619155`
(`test(adapter)`, not `docs(adapter)`) removed those two tests once the maintainer
identified them as obsolete rather than a durable invariant -- see `DIVERGENCES.md` D10
for the full rationale. This commit reconciles R5.5/R5.9/R5.10 and the statistics below
with that removal.

**Final branch verification** (after all commits above): head `33619155afcca9151b1249926e07c91f862fca41`.
`git diff --name-only main...HEAD -- adapter/` = **21 files** (unchanged from the
content sweep -- `33619155` only re-touched an already-modified file), **199
insertions / 289 deletions** (`git diff -b`; the 46 additional deletions are
`33619155`'s test-only removal, no new insertions). Whole-branch `git diff --name-only
main...HEAD` = **25 files** (the 21 above, plus this package's own
`05-adapter-documentation-sweep.md`, `CONTEXT.md`, `PLAN.md`, and `DIVERGENCES.md`),
comfortably under the 80-file re-plan threshold. This branch's 12 commits are typed
`docs(adapter)` (8), `docs(plan)` (3), and `test(adapter)` (1) -- the one
`test(adapter)` commit is a test-only deletion with no production behavior change, per
R5.10 below.

| Req | Requirement | Evidence |
| --- | --- | --- |
| R5.1 | Every handwritten Adapter declaration remains documented | Every file in `adapter/{capture,dispatch,identity,ipc,papyrus,plugin,process,runtime}/` and their tests was read in full and surveyed; no commit removes a doc comment without replacing it -- every edit either trims/corrects prose inside an existing block or leaves it untouched. Most of the module (`identity/`, `dispatch/`'s test, `ipc/`'s 14 message DTOs and `ipc_frame_codec.*`, `papyrus/`'s status adapter, `plugin/`'s `adapter_runtime.*`/`adapter_startup_context.hpp`, all of `process/`'s 12 prod files, most of `runtime/`) was already fully compliant and untouched. |
| R5.2 | Params/results/Doxygen remain useful and concise | No `@param`/`@return`/`@throws` was removed anywhere in the sweep; confirmed per-file during the module survey (e.g. `ipc_frame_codec.hpp`'s full `@param`/`@return`/`@throws` set on every `Encode*`/`Decode*` helper is untouched). |
| R5.3 | Long method-body narration removed | None found needing removal -- every method-body `//` comment surveyed across all 8 modules was already a short why-comment (lock ordering, ownership, platform quirk), not narration. |
| R5.4 | SKSE/Windows/concurrency/ownership safety rationale remains | Explicitly verified by a dedicated fresh-eyes pass after Step 3 (the highest-risk step, `AdapterIpcSession`/`AdapterIpcConnection`/`WinsockAdapterIpcSocket`) confirming no concurrency/lifecycle/security invariant was dropped; the 54-line `SendTrustAdminRequest` contract and the destructor's generation-close reasoning were deliberately left untouched as justified length per `ai/context/common.md`'s own exception for real concurrency/lifecycle contracts. |
| R5.5 | Legacy Bridge/migration genealogy removed from Adapter implementation and test documentation | `65aaec38` (capture/dispatch), `b5407da5` (ipc peer-proof, plus 2 stale-claim corrections found by grep-verifying actual usage), `2ece8c7c` (ipc session: roadmap-stage refs + 4 test-comment genealogy rewrites), `e23b58d3` (papyrus trust-admin), `a41ac42a` (plugin: the flagship `SOURCE.md` `SetupLogging` example, plus 2 more dangling `bridge/` test references), `21d5b2ee` (4 roadmap-narration test comments in the real-process E2E suite), `e4e43195` (runtime: version guard + achievement-compatibility attribution, kept the real external MIT attribution), `d427c6c1` (CMakeLists.txt's 3 dangling `bridge/` references, plus 3 stale "future work"/roadmap-stage constants docs found to already be implemented). That content sweep left two structural regression-guard tests in `dovahlink_adapter_plugin_test.cpp` asserting current source contains no `bridge/` reference, at the time described as an intentional, permanent exception. The maintainer subsequently identified those two tests themselves as obsolete: `/bridge` is permanently deleted, so a test that only guards against its reintroduction carries the retired architecture's genealogy forward rather than testing anything about the current one, and does not require negative regression protection. `33619155` removed both tests (see `DIVERGENCES.md` D10). Repo-wide `git grep -niI bridge -- adapter/` now returns **zero matches**, with no remaining exception. |
| R5.6b | Residual non-normative verbosity trim in `cpp-style.md` | Investigated (full read + a genealogy/roadmap-signal-word grep across the whole file): zero hits. Concept 01's `R1.14` fix and Concept 01.1's physical enum/constants move already left this file fully rule-focused; no residual cleanup remained. No file changed. |
| R5.7 | Interface/public methods and private helpers organized consistently | Verified per-class during the survey against `ai/context/skse/cpp-style.md`'s member-ordering rule (constants/static state, injected dependencies, mutable instance state, constructors/destructor, interface-implementation/override methods grouped, other public methods, private helpers, nested types); every class already grouped interface/override methods together with no interleaved private helper -- no reordering was needed anywhere in this sweep. |
| R5.8 | C++ data-member ordering not changed blindly | Zero data-member reorders performed anywhere in this sweep -- confirmed by the diff review below (R5.10), which found no data-member declaration touched. |
| R5.9 | Adapter tests remain green | Content-sweep baseline: `ctest --preset windows-x64-debug` re-run after every one of the 8 content-changing steps: **488/490 passed, 2 skipped** (the Release-only real-package-layout tests, expected and unchanged) at every single checkpoint, identical to the pre-sweep baseline captured before Step 1. The IPC test group (`AdapterIpcSession`/`AdapterIpcConnection`/`WinsockAdapterIpcSocket`, 172 tests) was additionally re-run 3 times after Step 3 to rule out concurrency flakiness from that step's edits -- 172/172 all three times. Post-`33619155` (this correction, deleting the two obsolete Bridge tests): full Debug rebuild + `ctest --preset windows-x64-debug` re-run, actual result **486/488 passed, 2 skipped** -- the same 2 Release-only tests skipped as the baseline; the discovered total dropped from 490 to 488 solely because the two removed `TEST_CASE`s are gone, which is the intended, documented consequence of `DIVERGENCES.md` D10, not a new failure. Zero test failures at any point. |
| R5.10 | No runtime behavior changes | 11 of this branch's 12 commits are typed `docs(adapter)`/`docs(plan)`; one, `33619155`, is typed `test(adapter)` because it deletes two `TEST_CASE` blocks -- recorded honestly as a test-count change rather than folded into a `docs` commit, per `DIVERGENCES.md` D10. A repo-wide diff review (`git diff -b main...HEAD`, formatting-reflow noise from the pre-commit hook separated out) confirms no production control flow, function signature, ownership/lifetime, IPC/protocol behavior, security behavior, startup/shutdown sequencing, or C++ data-member ordering changed anywhere on this branch: every content hunk in the 20 other `adapter/` files is either the R5.5 comment rewrite or pure `clang-format` reflow, and `33619155`'s only hunk is the two deleted `TEST_CASE` bodies plus the now-unused `#include <array>` -- no production file (only the test file itself) is touched by that commit. |
