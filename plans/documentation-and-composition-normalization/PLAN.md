# Phase: Documentation and composition normalization

## 1. Source

- **Source path:** `plans/documentation-and-composition-normalization/SOURCE.md`
- **Source fingerprint:** SHA-256
  `6b09702f2b038be5c5d1d1c81048f9c2cce5b7e079c45797bc77c49ed59fea5f`
  (computed over the final three-block file, 2026-09-13).
- **Baseline:** `main` @ `499bd4f44e93e870388ff54f4f91489e3c546064` (PR #58 merged),
  2026-09-13.
- **Match status:** Current as of this writing. Re-verify the hash at the start of every
  concept turn; if it no longer matches, stop and reconcile before continuing.
- **Package status:** Frozen 2026-09-13, after two maintainer review passes
  (`DIVERGENCES.md` D1 and the CONTEXT.md decision log record both). No further
  restructuring of the concepts listed in the status table (section 8) beyond an
  approved divergence such as D2's insertion of Concept 01.1; changes from here are
  tracked as new divergences, not silent edits to this package's design.

There is no pre-existing roadmap phase or repository `PLAN.md` behind this initiative --
it is cross-cutting engineering hygiene (documentation/changelog conventions plus
Host/Adapter composition), not a product-roadmap stage. `SOURCE.md` is therefore the
frozen source of record instead of a roadmap stage file.

## 2. Objective and boundaries

Normalize DovahLink's documentation and changelog conventions, then use the corrected
conventions to make Host and Adapter composition/lifetime explicit and to sweep both
subsystems' documentation -- without changing runtime, protocol, or security behavior
anywhere in the package, except the one narrow, explicitly decided exception
`DIVERGENCES.md` D4 approves (public compatibility/version and instance-identity
vocabulary, implemented only by Concepts `01.3b`/`01.3c` to exactly the extent
Concept `01.3a` decides).

**In scope:** `ai/context/common.md`, `ai/context/dotnet/csharp-style.md`,
`ai/context/skse/cpp-style.md` (normative-correctness slice), `CHANGELOG.md`,
`tooling/test_repository_consistency.py`; `host/DovahLink.Host/` composition and
documentation; `adapter/` composition and documentation; the corresponding test
projects. Per D4: `AGENTS.md`, `ARCHITECTURE.md`, `ROADMAP.md`/`roadmap/**`,
`ai/context/**`, `console-admin/**`, `protocol/**`, `sdk/**`, `app/**`,
`integration/**`, and `host/`/`adapter/` terminology -- each concept's own file names
the exact set it may touch; see `01.2a`-`01.3c`'s concept files.

**Non-goals (package-wide):** no protocol changes except D4's approved exception, no
new product features, no opportunistic redesign of anything not named by a concept's
own scope, no generic
`TODO.md`, no compatibility shims for the pre-release product (per
`ai/context/common.md`'s "Pre-release compatibility").

**Dependency:** PR #58 is merged (baseline commit above) -- the one external
prerequisite named in `SOURCE.md` is satisfied.

## 3. Inherited invariants

Every concept in this package inherits all of the following unless its own file
explicitly states a narrower exception:

- Preserve runtime behavior, protocol behavior, security semantics, and public
  behavior exactly as they exist at the concept's starting commit.
- No opportunistic cleanup: do not fix, rewrite, or restructure anything the concept's
  own scope does not name, even when it is found to be poorly designed while working
  nearby. Report such findings separately (a maintainer note, a spawned task, or a
  roadmap/issue reference) rather than silently folding them in.
- Documentation coverage is never reduced -- every handwritten declaration in touched
  files keeps its documentation; only verbosity, placement, and organization change.
- C++ data-member declaration order is never changed unless the change is proven safe
  for construction, destruction, aggregate/designated initialization, and layout/ABI
  assumptions.
- Fail-closed security/startup ordering (trust and security state validated before any
  externally reachable listener/admission path is exposed) is preserved everywhere it
  currently exists.
- `ai/context/common.md`'s existing behavior-bearing-boundary rules (explicit
  interfaces, constructor injection, no service locators) apply to any new type a
  concept introduces.

## 4. Requirement IDs

Each ID's original wording lives in `SOURCE.md`; only a short paraphrase and pointer
are recorded here to avoid a second verbatim copy.

### Concept 01 -- conventions and changelog (`SOURCE.md` Block A, Issue 1; Block C item 3)

| ID | Paraphrase | Source location |
| --- | --- | --- |
| R1.1 | Documentation coverage is not reduced. | Block A, Issue 1 Acceptance criteria |
| R1.2 | Concise documentation is explicitly preferred; size guidelines by declaration kind. | Block A, Issue 1 "Documentation size and readability" |
| R1.3 | Params/results/XML/Doxygen structure remain strongly encouraged. | Block A, Issue 1 "Documentation philosophy" |
| R1.4 | Long method-body documentation/narration is prohibited; short why-comments only. | Block A, Issue 1 "Method bodies" |
| R1.5 | Current contract, history, architecture, and future plans get separated homes (information-ownership table). | Block A, Issue 1 "Information ownership" |
| R1.6 | Semantic member ordering replaces append-only convention for normal classes; C++ data-member order exempted unless proven safe. | Block A, Issue 1 "Member ordering" |
| R1.7 | `[Unreleased]` section (with `Added/Changed/Fixed/Removed/Security`) exists above versioned releases; release workflow promotes it. | Block A, Issue 1 "`[Unreleased]` changelog", "Release workflow" |
| R1.8 | Feature vs. release changelog responsibilities are explicit. | Block A, Issue 1 "Feature PR behavior" |
| R1.9 | Changelog bullets: one outcome each, concise, no implementation walkthrough/PR history. | Block A, Issue 1 "Feature PR behavior" rules list |
| R1.10 | Convention tests/tooling updated to match. | Block A, Issue 1 Acceptance criteria |
| R1.11 | No runtime behavior changes. | Block A, Issue 1 Acceptance criteria |
| R1.12 | `CHANGELOG.md` describes DovahLink (not only the old Bridge); reconciled with roadmap/`common.md`. | Block A, Issue 1 "Existing changelog cleanup" |
| R1.13 | Ordered-section rules are semantic, not universal: reverse-chronological collections (changelog) keep newest-first; append-only survives only where semantics require it. | Block B, item 4 (first quoted addition) |
| R1.14 (= R5.6a) | Convention docs must describe the current repository; no deleted-directory/retired-type as a primary normative example -- the normative-correctness slice of `cpp-style.md`'s Bridge-reference fix, split forward from Issue 5's R5.6 per D1. | Block B, items 2 and 4 |
| R1.15 | Introducing `[Unreleased]` requires backfilling it with notable outcomes already merged to `main` since the `0.3.3` release baseline, not starting it empty. | Block C, item 3 |

### Concept 02 -- Host composition (`SOURCE.md` Block A, Issue 2)

| ID | Paraphrase |
| --- | --- |
| R2.1 | `Program.cs` reduced to bootstrap/composition responsibility. |
| R2.2 | Host registrations split into cohesive `Composition/*ServiceExtensions.cs`-style modules. |
| R2.3 | Every behavior-bearing Host dependency gets a deliberate, audited lifetime (host-singleton / client-scoped / session-scoped / connection-scoped / transient). |
| R2.4 | `clientId` / `sessionId` / connection identities stay distinct; no state keyed by `clientId` merely for convenience. |
| R2.5 | No accidental cross-session or cross-connection state sharing. |
| R2.6 | Fail-closed security/startup ordering preserved; no sync-over-async initialization. |
| R2.7 | One explicit Host lifecycle orchestrator (e.g. `DovahLinkHostRuntime`) controls the verified startup/shutdown order, not implicit `IHostedService` ordering. |
| R2.8 | No service locator, no static global service-provider access. |
| R2.9 | Composition/lifetime tests cover singleton identity, session/connection isolation, reconnect freshness, `clientId`-persistence-without-leak, fail-closed startup, no-premature-listener-exposure, idempotent/ordered shutdown. |
| R2.10 | Runtime behavior remains equivalent. |

### Concept 03 -- Adapter composition (`SOURCE.md` Block A, Issue 3)

| ID | Paraphrase |
| --- | --- |
| R3.1 | No C++ DI framework introduced. |
| R3.2 | `SKSEPluginLoad` becomes a clear entry/composition boundary rather than the graph itself. |
| R3.3 | One explicit `AdapterRuntime` (or equivalently named type) owns the process-lifetime graph. |
| R3.4 | No globally accessible service locator/singleton API (no `AdapterRuntime::Instance()`). |
| R3.5 | Constructor injection remains the normal dependency mechanism. |
| R3.6 | Loader-lock-safe process-lifetime behavior preserved; no "cleaning up" the intentional leak. |
| R3.7 | `DllMain` remains minimal and non-blocking. |
| R3.8 | Concise current-state lifecycle documentation exists (no full migration history). |
| R3.9 | Composition/lifecycle tests updated (wiring, single-runtime construction, callback routing, startup stage, idempotent shutdown signaling, no unsafe-detach destruction). |
| R3.10 | No feature behavior changes. |

### Concept 04 -- Host documentation sweep (`SOURCE.md` Block A, Issue 4)

| ID | Paraphrase |
| --- | --- |
| R4.1 | Every existing handwritten Host declaration remains documented. |
| R4.2 | Useful params/results/exceptions remain. |
| R4.3 | Oversized XML blocks reduced to current-contract information. |
| R4.4 | Method bodies no longer carry long explanatory documentation. |
| R4.5 | Concurrency/security/lifecycle-critical comments remain. |
| R4.6 | Historical/future-development narration removed from implementation documentation. |
| R4.7 | Test docs describe current invariants, not regression genealogy. |
| R4.8 | C# members use the semantic ordering established by Concept 01/02. |
| R4.9 | Interface methods stay grouped before private helpers. |
| R4.10 | Host tests remain green. |
| R4.11 | No runtime behavior changes. |

### Concept 05 -- Adapter documentation sweep (`SOURCE.md` Block A, Issue 5; `R5.6` split per D1 -- the `R5.6a` normative-correctness slice moves to Concept 01 as `R1.14`, the `R5.6b` non-normative slice stays here)

| ID | Paraphrase |
| --- | --- |
| R5.1 | Every handwritten Adapter declaration remains documented. |
| R5.2 | Params/results/Doxygen remain useful and concise. |
| R5.3 | Long method-body narration removed. |
| R5.4 | SKSE/Windows/concurrency/ownership safety rationale remains. |
| R5.5 | Legacy Bridge/migration genealogy removed from Adapter *implementation and test* documentation (convention-file genealogy is `R5.6a`/`R1.14`'s responsibility, per D1). |
| R5.6b | Remaining non-normative/historical verbosity cleanup in `ai/context/skse/cpp-style.md`, once its normative rules are already correct per `R5.6a`/`R1.14`. |
| R5.7 | Interface/public methods and private helpers organized consistently. |
| R5.8 | C++ data-member ordering not changed blindly. |
| R5.9 | Adapter tests remain green. |
| R5.10 | No runtime behavior changes. |

(`R5.6` is split per `DIVERGENCES.md` D1: `R5.6a`, the convention-document
normative-correctness slice, is reassigned to Concept 01 as `R1.14`; `R5.6b`, the
remaining non-normative/historical cleanup, stays with Concept 05 above.)

## 5. Concept graph

```text
01 conventions + changelog
        │
        ▼
01.1 Adapter enum/constants physical normalization
        │
        ▼
01.2a Active docs/instructions terminology
        │
        ▼
01.2b Internal code/test/tooling terminology
        │
        ▼
01.3a Public vocabulary + identity/version design
        │
        ▼
01.3b Compatibility/version vocabulary cutover
        │
        ▼
01.3c Public authoritative-instance identity cutover
        │
        ├──────────────────────────┐
        ▼                          ▼
02 Host composition           03 Adapter composition
        │                          │
        ▼                          ▼
04 Host docs               05 Adapter docs
```

Concept 01 must merge before 01.1 begins, so the corrected conventions are
authoritative before any composition, physical normalization, or vocabulary work reads
them. Concept 01.1 (see `DIVERGENCES.md` D2) makes `adapter/`'s actual enum/constants
layout match the convention Concept 01 wrote. Concepts 01.2a/01.2b/01.3a/01.3b/01.3c
(see `DIVERGENCES.md` D4) form a single linear vocabulary-normalization chain,
inserted because active instructions, internal names, and transitional public
protocol terms still contain retired Bridge vocabulary that would otherwise leak into
newly composed Host/Adapter code: 01.2a normalizes active docs/instructions, 01.2b
renames stale internal naming, 01.3a is a design-only gate deciding the public
compatibility/version and instance-identity vocabulary, and 01.3b/01.3c implement
exactly that decision -- the package's only concepts permitted to touch public wire
behavior. This chain is deliberately linear, not parallelized, to avoid cross-PR
conflicts and double-touching files under rename. 02 and 03 both now depend on 01.3c
merged, not on 01 directly -- there is little value composing Host/Adapter around
names and identity concepts about to be renamed. 03 additionally still depends on
01.1's physical normalization (named explicitly rather than left as a transitive
implication, since it documents Adapter composition's two independent prerequisites:
physical layout normalized, and vocabulary stable). 04 depends on 02 (not just 01.3c)
so it never documents/reorganizes Host code that composition is about to move; 05
depends on 03 for the same reason on the Adapter side.

## 6. Execution contract

- Each concept is implemented in its own feature branch and PR.
- A dependent concept must not begin implementation until its dependency's PR is
  merged to `main` -- not merely opened or approved.
- After Concept 01 merges: Concept 01.1 may start. The 01.2a -> 01.2b -> 01.3a ->
  01.3b -> 01.3c chain proceeds linearly, each waiting for the previous concept's PR
  to merge; do not parallelize it. Concepts 02 and 03 require 01.3c merged (03 also
  requires 01.1 merged). Concept 04 requires Concept 02 merged. Concept 05 requires
  Concept 03 merged.
- No PR in this package may change more than 100 files. Plan every concept for no
  more than 80 changed files to preserve review headroom. If an atomic behavior/
  protocol change cannot fit within 100 files, stop for maintainer review; do not
  invent a temporary compatibility shim, dual-field alias, or an arbitrary split
  boundary to force it under the limit.
- Stop for maintainer review after every concept/PR. Do not automatically continue to
  the next concept after completing one, even when its dependency is already merged.
- Re-check this `PLAN.md`'s source fingerprint and the traceability matrix at the start
  of each concept turn before writing code.

## 7. Traceability matrix

| Requirement | Concept | Status |
| --- | --- | --- |
| R1.1-R1.13, R1.15 | 01 | preserved |
| R1.14 (= R5.6a) | 01 | decomposed (split from Issue 5's R5.6, see D1) |
| R2.1-R2.10 | 02 | preserved |
| R3.1-R3.10 | 03 | preserved |
| R4.1-R4.11 | 04 | preserved |
| R5.1-R5.5, R5.7-R5.10 | 05 | preserved |
| R5.6b | 05 | decomposed (split from Issue 5's R5.6, see D1) |

Concept 01.1 covers no requirement ID -- it did not exist in the original decomposition.
See `DIVERGENCES.md` D2. Concepts 01.2a, 01.2b, 01.3a, 01.3b, and 01.3c likewise cover
no requirement ID -- see `DIVERGENCES.md` D4.

## 8. Status tracking

| Concept | Status | PR |
| --- | --- | --- |
| 01 -- Conventions and changelog | Complete | #60 |
| 01.1 -- Adapter enum/constants physical normalization | Complete | #61 |
| 01.2a -- Active docs/instructions terminology | In progress | #62 |
| 01.2b -- Internal code/test/tooling terminology | Blocked by 01.2a | -- |
| 01.3a -- Public vocabulary + identity/version design | Blocked by 01.2b | -- |
| 01.3b -- Compatibility/version vocabulary cutover | Blocked by 01.3a | -- |
| 01.3c -- Public authoritative-instance identity cutover | Blocked by 01.3b | -- |
| 02 -- Host composition and DI lifetimes | Blocked by 01.3c | -- |
| 03 -- Adapter runtime composition | Blocked by 01.1, 01.3c | -- |
| 04 -- Host documentation sweep | Blocked by 02 | -- |
| 05 -- Adapter documentation sweep | Blocked by 03 | -- |

Status values: `Planned` -> `In progress` -> `Complete` (or `Blocked by <n>` while its
dependency is unmerged). `Complete` is authoritative only once that concept's PR is
actually merged to `main` -- GitHub's own merge-commit record is the permanent
traceability for which SHA a PR merged as; this table does not duplicate it. Update
this table after every PR merges; never rewrite `SOURCE.md` to reflect execution
progress.

## 9. Phase completion gate

The phase is complete only when:

- Every concept in the status table above (section 8) is `Complete`, each merged via
  its recorded PR -- this wording tracks the table rather than a fixed count, so an
  approved divergence that adds or removes a concept never leaves this gate stale.
- Every requirement ID in the traceability matrix is `preserved`/`decomposed` into a
  completed concept, or has an approved `DIVERGENCES.md` entry explaining why it is
  deferred or changed.
- `tooling/test_repository_consistency.py` and the Host/Adapter test suites are green
  at the final merge.
- No concept introduced a runtime, protocol, or security behavior change, except the
  exact public compatibility/version and instance-identity vocabulary change `01.3a`
  decided and `01.3b`/`01.3c` implemented, per `DIVERGENCES.md` D4 -- the package's
  one deliberate, narrow exception to this invariant.
- No PR in the final merge history exceeded 100 changed files.

## 10. Divergence policy

See `DIVERGENCES.md`. Four entries are currently recorded and approved: D1 -- Issue
5's `R5.6` splits into the convention-document normative-correctness slice (`R5.6a`,
reassigned to Concept 01 as `R1.14`) and the remaining non-normative/historical
cleanup (`R5.6b`, staying with Concept 05); D2 -- Concept 01.1 is inserted between
Concept 01 and Concept 03 to physically normalize `adapter/`'s enum and constants
layout, a new concept outside the original five-issue decomposition; D3 -- the
status table in section 8 tracks `Status | PR` only, not the merge SHA `SOURCE.md`
Block C item 7 originally asked for, since a PR cannot record its own merge SHA
before merging and GitHub already owns that record permanently; and D4 -- five new
concepts (`01.2a`, `01.2b`, `01.3a`, `01.3b`, `01.3c`) normalize legacy Bridge
terminology and the public compatibility/version and instance-identity vocabulary
before Concept 02/03 composition begins, the package's one deliberate, narrow
exception to the "no protocol/public behavior change" invariant.
