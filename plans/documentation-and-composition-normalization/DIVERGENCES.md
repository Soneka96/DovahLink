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

**Proposed change:** Split R5.6. The correctness-critical slice -- making
`cpp-style.md`'s normative rules accurate and free of deleted-path/deleted-type
examples where those examples are load-bearing for applying the rule -- becomes R1.14,
owned by Concept 01. Concept 05 keeps the broader verbosity/historical-comment pass
over actual Adapter production code and tests, plus any residual non-normative
trimming left in `cpp-style.md` once its rules are already correct.

**Impact:** No behavior change. Changes only which concept's file list touches
`ai/context/skse/cpp-style.md`, and moves that touch earlier in the sequence so
Concept 03 can rely on a corrected convention document.

**Status:** approved.

**Decision source:** User message, 2026-09-13 (`SOURCE.md` Block B item 2, confirmed
in Block C item 4).
