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
