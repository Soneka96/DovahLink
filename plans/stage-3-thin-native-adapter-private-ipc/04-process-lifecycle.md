# 04 — Host process lifecycle and composition

Status: pending

Covers: R1, R4, R5, R8

Depends on: `01-private-ipc-contract.md`, `02-host-ipc-channel.md`,
`03-native-adapter-core.md`.

## Owner and boundary

The adapter owns lifecycle plumbing for the packaged host process because it is
the Skyrim lifetime’s native entry point. The host owns its own deterministic
teardown once asked to stop. This concept is stable because process ownership,
adoption proof, retry, and cleanup are independent of any particular Skyrim
domain.

## Inputs and outputs

Inputs are adapter startup/shutdown callbacks, Papyrus connection requests,
packaged host executable metadata, private-channel readiness, and OS process
state. Outputs are a hidden host process tied to the current Skyrim lifetime,
bounded connection attempts, explicit host-readiness status, graceful shutdown
requests, and forced cleanup when the owner disappears.

## Contracts

- Launch uses structured process arguments and a hidden window; no untrusted
  shell command text is built or executed.
- The host creates the private listener from the configured loopback port. A
  configured port of `0` means the operating system assigns an available
  loopback port; the host then passes that actual bound port to the current
  adapter through a bounded, authenticated startup rendezvous. The adapter
  connects to the reported endpoint and never guesses or performs a
  find-then-bind port race.
- Adoption is allowed only after private-channel ownership/lifetime proof; a
  matching executable or PID alone is insufficient.
- Startup retry runs outside game-thread callbacks and has explicit bounds and
  cancellation.
- A Papyrus or native connection request only triggers the same bounded,
  non-blocking lifecycle path; it does not add a second retry or policy layer.
- Orderly Skyrim close asks the host to perform deterministic teardown first.
  Parent-lifetime supervision remains the fallback for crash or forced exit.
- Repeated startup/shutdown and host-already-gone paths are safe and idempotent.

## Invariants

- No orphaned host may remain after forced Skyrim termination within the
  operating-system supervision guarantee.
- Host failure never blocks or crashes the adapter.
- Host and adapter retain independent process and identity lifetimes.
- Process IDs are diagnostics only and never become DovahLink identity.

## Allowed files/modules

- `adapter/include/` and `adapter/src/` — process launch, adoption proof,
  supervision, startup retry, and shutdown modules only.
- `host/DovahLink.Host/Process/` and `host/DovahLink.Host/Program.cs` — host
  startup/composition and deterministic shutdown integration only.
- `adapter/tests/`, `host/DovahLink.Host.Tests/Process/`, and a narrowly scoped
  cross-process harness module only if required to prove the contract.
- No client WebSocket, pairing, public protocol cutover, or `bridge/` files.

## Proof obligations

- Hidden launch and safe adoption are tested with controllable process doubles.
- Dynamic-port startup is tested with configuration `0`, including successful
  host binding, exact adapter discovery of the assigned port, peer-proof
  validation during rendezvous, and bounded failure when endpoint handoff is
  unavailable. A configured nonzero port remains supported and fails clearly
  when already occupied.
- Startup retry is bounded, cancellable, and never runs on a game-thread path.
- Graceful close requests host teardown and handles an already-dead host.
- Forced owner termination is covered by supervision/cleanup tests.
- The host remains valid without an adapter and reports controlled unavailability.
- Papyrus-facing connection/status behavior reports host readiness or “host not
  ready” without blocking Skyrim or inventing a local fallback state.

## Non-goals

- Packaging the final Vortex release, deleting `bridge/`, or final runtime
  conformance; those belong to later stages.
- Adding application behavior to the adapter.

## Completion evidence

- Focused lifecycle/composition checks pass.
- The phase completion gate can point to an independently buildable adapter,
  host, and private-channel proof without unresolved divergence.
