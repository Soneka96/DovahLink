# 01 — Private IPC contract and message model

Status: pending

Covers: R2, R4

Depends on: Stage 1 and Stage 2; no sibling concept dependency.

## Owner and boundary

This is the host/adapter contract boundary. It is a small, private,
language-neutral message model implemented independently by the C# host and C++
adapter. It is stable because both process lifetimes and every later IPC
operation depend on the same negotiated framing, identity, and limit rules.

## Inputs and outputs

Inputs are the Stage 1 private-IPC decisions and the existing host identity and
availability seams. Outputs are the explicit frame layout, version negotiation,
message kinds, size/rate limits, peer-ownership proof, cancellation semantics,
and deterministic close behavior consumed by concepts 02–04.

## Contracts

- The private contract belongs to the host/adapter boundary, not `protocol/`
  and not the public SDK envelope.
- Messages are owned values. No frame or queued item may retain a Skyrim/CommonLib
  pointer, borrowed buffer, or public-protocol object.
- Host and adapter reject incompatible versions, malformed frames, unknown
  message kinds, invalid identities, and over-limit payloads.
- The contract has explicit request/response correlation and a resynchronization
  request path without embedding application policy in the wire model.

## Invariants

- Per-message bytes, frame length, queue capacity, and rate limits are explicit.
- Cancellation and close are observable and idempotent on both sides.
- The adapter can stop offering data without blocking a game-thread callback.
- CommonLib and Skyrim headers appear only in adapter compilation units, never in
  host sources or private contract representations.

## Allowed files/modules

- `host/DovahLink.Host/Adapter/Ipc/` — host-side private frame/message
  contract and codec module.
- `host/DovahLink.Host.Tests/Adapter/Ipc/` — host-side contract tests.
- `adapter/ipc/` — native private frame/message contract and codec module.
- `adapter/tests/ipc/` — native contract tests.
- No `bridge/`, `protocol/`, SDK, app, or public WebSocket files.

## Proof obligations

- Valid frames round-trip across the declared representation.
- Invalid length, version, kind, identity, and payload cases fail closed.
- Limit checks happen before allocation or queue admission.
- Cancellation and repeated close cannot deadlock or throw uncontrolled errors.
- Contract tests use owned plain values and do not require Skyrim.

## Non-goals

- Public protocol changes or WebSocket behavior.
- Pairing, trust, subscriptions, cadence policy, state publication, or client
  session behavior.
- Skyrim reads, event registration, process launch, or Papyrus behavior.

## Completion evidence

- Focused contract tests and host/adapter compilation pass.
- The final frame/message rules are recorded in source documentation and match
  the Stage 1 private-IPC ownership decisions without a material divergence.
