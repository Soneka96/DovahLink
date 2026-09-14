# Divergences

## D1 -- cpp-style.md normative-correctness fix moved from Concept 05 to Concept 01

**Original requirement:** R5.6 -- "C++ convention documents become more rule-focused
and less historical" (`SOURCE.md` Block A, Issue 5 "Convention-file cleanup" and
Acceptance criteria), originally scoped entirely to the Adapter documentation sweep
(Concept 05).

**Observed conflict:** `ai/context/skse/cpp-style.md` does not merely contain
*historical color* about the retired Bridge -- its normative rules are expressed in
terms of the deleted `bridge/` directory layout and specific deleted types
(`IBridgeCallbackRegistry`, `bridge/shared/enums.hpp`, `TokenStore::Reservation`,
`SessionManager::Lease`, `ConnectionSlot::Lease`). Leaving this until Concept 05 means
any Adapter composition work done in Concept 03 -- which happens first, per the
dependency graph -- would be guided by a convention document that is not just verbose
but factually describes a directory structure that no longer exists on disk.

**Proposed change:** Split R5.6 explicitly into two tracked requirements rather than
reassigning it wholesale:

- `R5.6a` -- normative correctness of `cpp-style.md`: its current rules must not rely
  on deleted Bridge paths/types as their load-bearing worked examples. Owned by
  Concept 01, tracked there as `R1.14`.
- `R5.6b` -- remaining non-normative/historical verbosity cleanup in the C++
  convention documents: residual example trimming left in `cpp-style.md` once its
  rules are already correct per `R5.6a`. Stays with Concept 05. This is distinct from
  `R5.5`, which already owns the broader historical-comment pass over actual Adapter
  production code and tests -- `R5.6b` touches only the convention document itself.

Both halves are named directly in `PLAN.md`'s Requirement IDs and Traceability
sections so neither loses its own ID.

**Impact:** No behavior change. Changes only which concept's file list touches
`ai/context/skse/cpp-style.md`, and moves that touch earlier in the sequence so
Concept 03 can rely on a corrected convention document.

**Status:** approved.

**Decision source:** User message, 2026-09-13 (`SOURCE.md` Block B item 2, confirmed
in Block C item 4).

## D2 -- New Concept 01.1 inserted between Concept 01 and Concept 03

**Original requirement:** None in `SOURCE.md`. This concept did not exist in the
original five-issue decomposition; it emerged from Concept 01's own review process.

**Observed conflict:** Concept 01's first implementation pass fixed
`ai/context/skse/cpp-style.md`'s enum-consolidation rule by pointing it at the real
`ipc/ipc_enums.hpp` file instead of an invented `adapter/shared/enums.hpp`. Review
found that fix itself flawed: it told future non-IPC enums (for example a hypothetical
`capture/`-owned enum) to live inside a file physically and namespace-scoped to
`ipc/`, making the IPC module the physical owner of concepts it has no domain
relationship to -- a new design problem, not a documentation-accuracy one. The
maintainer's resolution: the intended convention (mirroring `csharp-style.md`'s
per-project `Enums.cs`/`Constants.cs`) is one project-wide `adapter/enums.hpp` and one
project-wide `adapter/constants.hpp`, with domain namespaces preserved inside each
file. `ipc/ipc_enums.hpp` and the current per-module `constants.hpp` files are today's
not-yet-relocated location, not the intended one. Concept 01 documents that target
rule (docs-only, per its own scope boundary); actually moving the files is a real
source-code change across ~10 `adapter/` files plus CMake, which does not belong in a
documentation-only concept.

**Proposed change:** Insert Concept 01.1 (see
`01.1-adapter-enum-and-constants-physical-normalization.md`) between Concept 01 and
Concept 03: it performs the physical file move/consolidation only, no other Adapter
restructuring. It is not folded into Concept 03 (Adapter runtime composition) because
that concept's purpose -- process-lifetime object-graph ownership -- is a different
concern from where enum/constant declarations physically live; Concept 03 should start
from an already-normalized layout rather than absorb this cleanup as a side quest.
Concept 03's dependency changes from "Concept 01 merged" to "Concept 01.1 merged".
Concept 02 (Host composition) is unaffected and may still proceed independently once
Concept 01 merges.

*Superseded by D4 below:* this last sentence was true when D2 was written, but D4
inserts the 01.2a-01.3c vocabulary chain between Concept 01.1 and Concept 02, so
Concept 02 is no longer unaffected -- it now depends on Concept 01.3c, not Concept 01
directly. This historical entry is left unedited above (D2's own decision about
Concept 03's dependency on 01.1 remains correct and unchanged); only its now-false
claim about Concept 02 is annotated here rather than silently rewritten.

**Impact:** Adds one concept and one PR to the package; no requirement ID is added or
changed. No behavior change is introduced by Concept 01 or 01.1 individually --
01.1's file move is intended to be behavior-neutral and must be verified by the full
Adapter test suite before that concept is marked `Complete`.

**Status:** approved.

**Decision source:** User messages, 2026-09-13 (maintainer review of Concept 01's
first implementation and its correction pass; the module-owned-enum-header
alternative was proposed and then explicitly withdrawn in favor of this one-file-per-
project resolution, in the same review exchange).

## D3 -- PLAN.md's status table tracks PR only, not merge SHA

**Original requirement:** `SOURCE.md` Block C item 7: "Let `PLAN.md` track concept
status, PR number and merge SHA; do not mutate `SOURCE.md` as work progresses."

**Observed conflict:** A concept's own PR cannot record its own merge commit's SHA
inside itself -- that SHA does not exist until after the PR merges, so requiring it as
part of that PR's own completion evidence is circular. The `Status | PR` table
actually in `PLAN.md` section 8 does not include a Merge SHA column, which is a real,
intentional divergence from Block C item 7's literal text, not an oversight -- but it
had never been given its own `DIVERGENCES.md` entry, so a reader (or an automated
reviewer) checking the table against the frozen source alone would see an
unexplained gap rather than a recorded decision.

**Proposed change:** Track `Status | PR` only. `Complete` is authoritative only once
that PR is actually merged to `main`; GitHub's own merge-commit record is the
permanent, already-existing traceability for which SHA a PR merged as, so this table
does not duplicate it. This was already implemented in `PLAN.md` section 8 and
recorded in `CONTEXT.md`'s decision log; this entry is the missing `DIVERGENCES.md`
record making that override auditable against `SOURCE.md` Block C on its own terms.

**Impact:** No behavior change. `PLAN.md`'s status table has one fewer column than
Block C's original instruction described; no requirement ID is affected.

**Status:** approved.

**Decision source:** User message, 2026-09-13 (`SOURCE.md` Block C item 7's original
ask, superseded by the maintainer's own follow-up in the same review exchange: "Status
tracking: drop the merge-SHA column entirely. Track `Status | PR`... This avoids
creating a closeout PR just to record metadata.").

## D4 -- Legacy Bridge terminology and public vocabulary normalization inserted before composition

**Original requirement:** None in `SOURCE.md`. This did not exist in the original
five-issue decomposition. It is added because the repository's implementation has
moved to Host + Adapter while active instructions, internal names, and transitional
public protocol terms still contain retired Bridge vocabulary.

**Observed conflict:** A full-repository inventory (236 files, 1,342 case-insensitive
`bridge` hits, both content and path/filename search, planning baseline @
`9ef61c51699bfd78f3810d99f684025f4c6009ad` -- the pre-D4 commit on this branch, not
`main`, which is `499bd4f4`) found the remaining references are not one kind of debt. Some are stale current-architecture terminology (`AGENTS.md` still
names `Bridge/Core` as a current investigation boundary; `console-admin/dovahlink.yaml`
and `DovahLinkAdmin.psc` cite a deleted implementation path,
`bridge/game_state/commonlib_trust_admin_papyrus_adapter.cpp`, that no longer exists
post-3A.2; `roadmap/10-multi-bridge-and-local-discovery-foundation.md` is a *planned,
not-yet-built* stage still named and worded around "Bridge" rather than historical
record of what shipped). Some are stale internal naming with no external contract
(`app/lib/features/connection/**`'s `BridgeEntity`/`BridgeListScreen`/
`BridgeListViewModel` cluster models "a DovahLink instance to connect to" under the
retired name; `adapter/runtime/adapter_task_marshaller.hpp`'s doc comment still cites
`IBridgeCallbackRegistry`, a name Concept 01 already removed from `cpp-style.md`).
Some are public wire/protocol contract: `bridgeVersion` and `bridgeInstanceId` are
real JSON field names, appearing in 3 and 57 protocol fixtures respectively (the
latter because it lives in the standard envelope header, present on nearly every
message), where a rename is a protocol decision, not a cosmetic one -- both fields are
already documented as "legacy wire-field name" in `protocol/schema/README.md`, and
`ai/context/protocol/compatibility.md` already defers the public authoritative-process
instance identifier decision and explicitly prohibits substituting `adapterInstanceId`,
a PID, a port, or a connection/session ID for it. Blindly renaming any of this --
especially a global `Bridge` -> `Host` substitution -- would silently rewrite
historically accurate changelog/roadmap/frozen-reference text, or make a wire-protocol
decision (compatibility authority naming, public instance identity semantics) without
the design step that decision actually requires.

**Proposed change:** Insert five new concepts between Concept 01.1 and Concept 03,
before Host/Adapter composition begins, so 02/03 build against stable final vocabulary
rather than names scheduled for immediate replacement:

- `01.2a` -- normalize active, present-tense docs/instructions terminology
  (documentation-only, no wire/behavior change).
- `01.2b` -- rename stale internal (non-public, non-wire) implementation naming to its
  actual current owner -- Host, Adapter, or DovahLink -- with no other semantic
  refactor riding along (behavior-neutral).
- `01.3a` -- a design-only gate with no wire implementation: decide the compatibility
  authority and canonical version vocabulary (replacing `bridgeVersion`), decide
  whether and how the public protocol exposes a state-authority continuity
  identifier (replacing `bridgeInstanceId`'s deferred semantics), and produce an
  explicit old-to-new vocabulary table. No implementation concept below starts while
  any row of that table is undecided.
- `01.3b` -- implement exactly 01.3a's compatibility/version decision (small: ~3
  fixtures plus the Host/SDK code and docs that produce/consume that one field).
- `01.3c` -- implement exactly 01.3a's public state-authority continuity identity decision
  (large: ~57 fixtures plus Host/SDK envelope code, since this field is in the
  standard envelope header) -- proving reconnect/new-session/multi-client/restart
  identity invariants, not just a renamed string.

Every concept keeps the package's standing "no blind global rename," "no opportunistic
cleanup," and "preserve historical truth" rules: a released changelog entry, frozen
`SOURCE.md`/reference material, explicit retired-Bridge history, and a completed
roadmap phase's historical record are not rewritten merely because they say "Bridge."
`01.3b`/`01.3c` are the package's only concepts permitted to touch public wire
behavior, and only to the exact extent `01.3a` decides -- no other protocol redesign
is authorized. A hard 100-changed-file-per-PR limit applies package-wide (target
`<=80`); if an atomic migration cannot fit, implementation stops for maintainer review
rather than inventing a compatibility shim, dual-field alias, or arbitrary split.

Concepts 02 and 03 are blocked until this vocabulary programme completes (`01.3c`
merged), not just `01`/`01.1` -- there is little value composing Host/Adapter around
names and identity concepts about to be renamed. Concept 03 additionally still depends
on `01.1`'s physical enum/constants normalization; both are named explicitly in its
concept file as two independent prerequisites (physical layout normalized; vocabulary
stable) even though `01.1` is transitively upstream of `01.3c` in the graph.

**Impact:** Adds five concepts and up to five PRs to the package; no existing
requirement ID changes. `01.3b`/`01.3c` are an explicit, narrow exception to the
package-wide "no protocol/public behavior change" invariant (`PLAN.md` section 3),
approved only to the extent `01.3a` decides -- this is the one place in the package
where that invariant is deliberately relaxed, and only there.

**Status:** approved.

**Decision source:** User-supplied agent brief,
`DovahLink_Bridge_Terminology_Normalization_Agent_Brief.md`, reviewed and approved
2026-09-13, with two corrections applied before insertion: the brief's own proposed
identifier ("D3") collided with this package's actual D3 (the merge-SHA divergence
above, added in a review pass the brief predated) and is renumbered D4; and Concept 02
and Concept 03's dependency lines are updated explicitly rather than left implicit.
