# Stage 28 — Safe Companion Authorization Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./27-optional-ui-mod-adapters.md) · [Next stage](./29-runtime-profiling-and-advanced-bridge-hardening.md)

## 28. Safe Companion Authorization Foundation

**Status:** Planned after read-only product validation

### Outcome

DovahLink can safely add individually approved actions later without exposing a generic command API
or granting control through read access.

### Scope and behavior

- Define per-client permissions independently from authentication and sessions.
- Keep read and control capabilities separate.
- Define command identity, requesting `clientId` and `sessionId`, result, failure, timeout, and
  idempotency.
- Reject replayed, stale-session, stale-play-context, unauthorized, and unsupported commands before
  game code.
- Define authorization, revocation, audit-safe diagnostics, and capability negotiation.
- Define deterministic multi-client conflict handling.
- Validate the machinery without adding gameplay mutation.

### Dependencies and boundaries

This phase follows read-only validation and depends on identity, multi-client isolation, and security.
It adds no equipment, favorites, hotkey, map-marker, fast-travel, console, Papyrus, or other action.
Each action needs its own product decision, security review, validation plan, roadmap phase, and
implementation approval.

### Acceptance criteria

The contract can deny control independently from reads; unauthorized, replayed, stale, conflicting,
and unknown test commands are rejected deterministically; decisions are attributable; and no Skyrim
mutation is exposed.
