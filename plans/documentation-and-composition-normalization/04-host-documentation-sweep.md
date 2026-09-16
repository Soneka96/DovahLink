# Concept 04 -- Host documentation and member organization sweep

**Status:** Complete on branch `feature/04-host-documentation-sweep`, open as PR #69
and under review. See this file's own R4.1-R4.11 traceability table below for the
per-requirement evidence.

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

### R4.1-R4.11 traceability

Base `main` @ `e76b76400a76ba801e103f90e1a75253a848f0b7` (PR #68 merged), head
`e26cd723c39bb80c94d441c14114dc5aa0864a09`. `git diff --name-only main...HEAD` = **67
files**, `git diff --stat` = **+808/-918 lines** (net reduction, consistent with trimming
narration rather than adding behavior). Every one of this branch's 46 commits is typed
`docs(host)` or `test(host)` -- none `fix(host)`/`feat(host)`/`refactor(host)`, itself
evidence no commit on this branch claims a behavior change.

| Req | Requirement | Evidence |
| --- | --- | --- |
| R4.1 | Every existing handwritten declaration remains documented | No commit on this branch removes a doc comment without replacing it; several commits add docs that were missing rather than removing coverage: `702fc278` (`PlayContextSnapshot` param docs), `f3c5d3f0` (`StoredValue` param docs), `bed8cbf0` (`PublicConnectionFactory` field docs), `d23f8860`/`7d449a08` (`FakeSessionRegistry`/`FakeAdapterAvailabilityTracker` missing field docs). Confirmed by the zero-warning `-p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true` build below -- a missing doc on a public member fails that build. |
| R4.2 | Useful params/results/exceptions remain | Same missing-param-doc commits as R4.1; no commit strips a `<param>`/`<returns>`/`<exception>` block, only prose size within blocks that remain. |
| R4.3 | Oversized XML blocks reduced to current-contract information | `b78db63b` (trim oversized comments and cross-file doc duplication in Process/Time/Security), `fd4f7d14` (drop planning-doc/requirement-id citations and duplicated docs from Composition sweep), `4cb9ba27` (drop oversized comments and stale `security.md` quotes in Authentication). |
| R4.4 | Method bodies no longer carry long explanatory documentation | Same `b78db63b`/`4cb9ba27` commits; no method-body comment block remains that narrates rather than states a short why. |
| R4.5 | Concurrency/security/lifecycle-critical comments remain | No commit in this branch's log removes synchronization-ordering, lock-avoidance, security-ordering, or cancellation/lifetime why-comments -- the trims above (`b78db63b`, `4cb9ba27`) removed duplication and stale citations, not the underlying safety rationale, which is unchanged in the diff. |
| R4.6 | Historical/future-development narration removed from implementation documentation | `fd4f7d14`, `b78db63b`, `f3c5d3f0`, `3bd8a2b9`, `89633673`, `4cb9ba27`, `855a65c5`, `e36e5ad9`, `e9398969`, `9044a971`, `960e9f56` -- each drops a plan/requirement-ID/roadmap/Concept citation, stale future-implementation narration, or an internal-migration reference from production doc comments, replacing it with current-contract wording. |
| R4.7 | Test docs describe current invariants, not regression genealogy | 21 commits drop genealogy framing ("X fix", "the fix", "race fix", "confirmed defect/race", "regression proof", "mapping fix", "Rename-resurrection", bare-flag/"had to be fixed for") from test documentation: `74f7afc9`, `a18dbfad`, `f98f4ad0`, `0bc2e590`, `9b38ee79`, `0451b9a4`, `e7780770`, `481a07e0`, `a8e98a68`, `d7cd393b`, `3b9e6f05`, `ecb81f03`, `f3eff330`, `96548db5`, `aeab86e9`, `70e02ad4`, `bff55041`, `20a07d69`, `dda7d0aa`, `55dd57f5`, `46aa5a7d`, `337b02fe`, `e26cd723`. |
| R4.8 | C# members use the semantic ordering Concept 01/02 established | `2625914e`, `89633673`, `764ba611`, `b42bbd33`, `e9df9ebe`, `e7381a2f`, `7ee53969`, `1bd4c002`, `5dd1c63f`, `6e6acb0a`, `7981773b`, `7d449a08`, `d23f8860` -- reorder declarations into constants/static state, dependencies, instance state, constructors, properties/events, interface/override methods, other methods, private helpers, nested types, with no logic change per commit. |
| R4.9 | Interface methods stay grouped before private helpers | `4386c491` (`AdapterIpcSession`), `0a13ad4f` (`PublicWebSocketConnection`) explicitly regroup interleaved interface methods and private helpers into two contiguous blocks. |
| R4.10 | Host tests remain green | `dotnet test host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --configuration Release` at head `e26cd723`: first run 1789/1791 passed, with 2 failures (`PublicWebSocketListenerTests.RunAsync_ConnectionThrows_StillAcceptsSubsequentConnection`, `ProgramCompositionTests.ComposeAndRunAsync_ShutdownRacingPublicHelloAdmission_NeverDeadlocksAndClientNeverHangs` -- the latter is the pre-existing DPAPI trust-store file-lock flake `CONTEXT.md`'s Concept 02 Verification entry already documents as unrelated to composition/doc work). Re-running the first test in isolation (`--filter FullyQualifiedName~PublicWebSocketListenerTests.RunAsync_ConnectionThrows_StillAcceptsSubsequentConnection`) passed, and a full clean re-run passed **1791/1791** -- both failures are parallel-execution flakes, not regressions from this branch's doc/reorder-only changes. |
| R4.11 | No runtime behavior changes | `dotnet build host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --configuration Release --no-incremental -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true` at head `e26cd723`: 0 warnings, 0 errors. Combined with R4.10's clean 1791/1791 test run (same pass count, no reclassified/removed test): every non-comment, non-blank line touched in `host/DovahLink.Host/**/*.cs`'s diff against `main` was extracted, stripped of its `+`/`-` marker and surrounding whitespace, and sorted -- the resulting multiset of added lines is byte-for-byte identical to the multiset of removed lines (107 added = 107 removed, zero set difference), proving every apparent code change in production files is a pure relocation with no line's content edited. The same check across `host/DovahLink.Host.Tests/**/*.cs` (117 added = 117 removed, zero set difference) proves the same for test files. |
