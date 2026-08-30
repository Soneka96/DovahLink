# 02 — Host IPC channel and recovery

Status: pending

Covers: R2, R4, R6, R7

Depends on: `01-private-ipc-contract.md`; Stage 2 host availability and identity
services.

## Owner and boundary

The C# host owns the private listener and connection lifecycle. This is a stable
host-side service boundary: it translates private messages into host-owned
availability and resynchronization operations without exposing adapter IPC to
public clients.

## Inputs and outputs

Inputs are negotiated adapter connections and bounded private messages. Outputs
are adapter availability transitions, a fresh baseline/resynchronization result,
host-directed event/sample intent messages, and controlled diagnostics for loss,
rejection, cancellation, or shutdown.

## Contracts

- Consume only the contract from concept 01.
- Inject the existing `IAdapterAvailabilityTracker` and host-owned collaborators;
  do not construct behavior-bearing services internally.
- On connect, create a connection generation and require a fresh baseline.
- On loss, mark adapter-sourced state unavailable and invalidate pending work.
- Forward host-owned event registration keys and sample-read tokens without
  interpreting Skyrim semantics in the host IPC channel or adapter protocol
  boundary.
- A resynchronization response is accepted only for the current connection and
  becomes a new baseline, never an incremental patch over stale state.

## Invariants

- No public client endpoint can reach the private listener or its message model.
- Peer rejection, malformed input, and over-limit input fail
  closed and leave the host in a controlled unavailable state.
- Connection teardown is deterministic, bounded, and idempotent.
- Late messages from an old connection generation cannot alter current state.

## Allowed files/modules

- `host/DovahLink.Host/Adapter/Ipc/` — C# private listener/channel and
  connection-generation handling.
- `host/DovahLink.Host/Adapter/` — only the existing adapter availability seam
  when a narrowly required integration change is proven.
- `host/DovahLink.Host.Tests/Adapter/Ipc/` — focused host channel tests.
- `host/DovahLink.Host/Program.cs` only if required for composition.
- No adapter implementation, public WebSocket, pairing, or `bridge/` files.

## Proof obligations

- Connect/disconnect/reconnect transitions update availability correctly.
- Adapter loss produces unavailable/resynchronization-required state.
- Current-generation resynchronization succeeds; stale-generation responses are
  ignored safely.
- Host-directed event keys and sample tokens reach the adapter as bounded intent
  messages; the host remains the owner of their policy and
  cadence.
- Cancellation, peer failure, malformed input, and shutdown complete without
  hanging the host.

## Non-goals

- Public protocol mapping, WebSocket hosting, pairing, trust, or state fan-out.
- Choosing which Skyrim event or sampled value a host feature ultimately needs.

## Completion evidence

- Focused C# tests pass and the host remains buildable without native headers or
  the old bridge.
