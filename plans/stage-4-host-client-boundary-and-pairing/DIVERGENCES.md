# Stage 4 divergences

## D1 — Defer public host-instance identity during the client-boundary transition

Original requirement: `host/PLAN.md` Stage 4 R5 — “The host preserves the required fresh session and
play-context identity semantics.” The canonical envelope in `protocol/schema/README.md` also defines
`bridgeInstanceId` as a Bridge-originated identity used by clients to reject state from an earlier
Bridge lifetime.

Observed conflict: The migration audit explicitly records that the replacement host has no public
host identity, that the old `bridgeInstanceId` belongs to the frozen single-process Bridge model,
and that exposing a replacement instance identifier remains deferred to a later protocol revision.
Stage 4 is not allowed to silently invent an identity by reusing `adapterInstanceId`, a process ID,
port, path, or owner lifetime. Stage 4 also excludes live state publication.

Proposed change: Preserve the current public envelope shape for the Stage 4 connection/pairing
boundary, represent the host-instance field as explicitly unavailable during this transition, and
do not publish live state. Treat public host-instance identity and any required schema/SDK/client
compatibility update as deferred work that must be resolved before live state publication and final
cutover. Fresh `ConnectionId`, `sessionId`, `clientId`, and `playContextId` semantics remain fully
enforced; this divergence affects only the deferred public host-instance identity.

Impact: Stage 4 remains focused on transport, authentication, pairing, trust, and teardown without
a cross-area protocol migration. Stage 5 cannot claim complete authoritative live-state compatibility
until the deferred identity decision is resolved. The limitation must remain visible in the phase
ledger and cannot be hidden by populating the field from an unrelated lifetime.

Status: approved

Decision source: Direct maintainer instruction in the current task on 2026-09-01 (“ok go with A, yes”),
consistent with `ai/context/host/migration-audit.md`'s deferred public identity decision.

## D2 — Reconcile the restricted-session pairing allowlist

Original requirement: R4 — “Public messages are bounded, validated, authorized, and rejected before
reaching adapter code.” The current `ai/context/protocol/security.md` wording says a restricted
session accepts only `ping`, `capabilities`, `pairing_request`, and `pairing_confirm`, while
`protocol/schema/README.md` explicitly includes `pairing_ack` in the unpaired flow and defines
`pairing_renotify` and `pairing_cancel` as Restricted-session messages.

Observed conflict: The restricted pairing flow cannot complete if `pairing_ack` is rejected, and the
canonical schema cannot support its documented redisplay/cancel operations if `pairing_renotify` and
`pairing_cancel` are rejected. The two repository documents therefore do not currently give a
future implementer one unambiguous allowlist.

Decision: Treat the canonical schema and the complete pairing message definitions as the behavioral
source for Stage 4: restricted sessions allow `ping`, `capabilities`, `pairing_request`,
`pairing_confirm`, `pairing_ack`, `pairing_renotify`, and `pairing_cancel`; full sessions reject
those restricted-only pairing operations. Reconcile the abbreviated allowlist wording in the
security, schema, and migration-audit documents without adding any new wire message or changing
the pairing protocol vocabulary.

Impact: This preserves a completable recoverable pairing handshake and prevents an AI from
implementing an allowlist that makes `pairing_ack`, redisplay, or cancellation unreachable. The
reconciled wording now gives Concept 02 one final allowlist to encode.

Status: approved

Decision source: Direct maintainer instruction in the current task on 2026-09-01 ("do it then"),
following the Stage 4 plan audit; the canonical schema and existing Bridge pairing-handler behavior
support the complete allowlist.
