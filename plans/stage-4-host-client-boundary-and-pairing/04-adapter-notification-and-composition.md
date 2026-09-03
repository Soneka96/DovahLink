# 04 — Adapter-facing notification and host composition

Status: pending

Covers: R1, R2, R3, R4, R5, R6

Depends on: `01-public-websocket-transport.md`, `02-authentication-and-session-admission.md`,
`03-pairing-and-client-dispatch.md`, and completed Stage 3 private IPC.

## Owner and boundary

The host owns the decision to request a narrow Skyrim-facing notification or administrative
operation; the adapter owns the Skyrim/Papyrus presentation or execution seam. This concept is
stable because it completes the cross-process boundary and composition without introducing live
state publication.

## Inputs and outputs

Inputs are host pairing/trust outcomes, adapter availability, private IPC connection state, and host
shutdown. Outputs are bounded typed adapter notifications/commands, controlled unavailable outcomes,
composed host services, and end-to-end client-boundary test evidence.

## Contracts

- Extend the private host/adapter contract only with the narrow typed notifications required by the
  approved Stage 4 flow: pairing-code display/redisplay, wrong-code redisplay,
  attempts-exhausted notification, and the exact trust-administration operations listed in the
  phase private-message matrix. Do not reuse the public envelope as IPC.
- Use a closed, typed private message vocabulary for each approved intent; do not introduce a generic
  command name plus arbitrary payload, client-controlled Skyrim key, or public-to-private message
  passthrough. Every new private message must have bounded codec validation and matching C#/C++ tests.
- Cover both directions explicitly: host→adapter initial/manual/incorrect/attempts-exhausted pairing
  notifications, and adapter→host Papyrus requests for `help`, `list`, `revoke`, `block`, `unblock`,
  `forget`, `reset-trust`, `reset`, and `confirm-reset`, with typed host results. Do not omit the
  adapter-originated administration path merely because public clients do not expose those commands.
- The host sends values the host has already decided; the adapter performs only Skyrim-facing glue.
- Initial display and manual redisplay have a bounded acknowledgement/readiness result so the host
  can truthfully produce `pairing_status.available` or `pairing_outcome.renotified`. Wrong-code
  automatic redisplay and attempts-exhausted notification are best-effort side effects of their
  already-determined pairing outcomes and never block the client response indefinitely.
- Pairing codes travel only over the mutually authenticated private channel to the adapter's display
  seam. The private notification vocabulary must cover initial display, manual redisplay, wrong-code
  automatic redisplay, and the no-code attempts-exhausted notification; none is logged, returned in
  pairing-status responses, or copied into public diagnostics.
- A missing, unavailable, or backpressured adapter produces an explicit controlled outcome and never
  blocks the WebSocket reader, writer, or host shutdown.
- Public malformed or unauthorized input is rejected before any notification reaches adapter code.
- Composition injects the public listener, adapter channel, trust store, pairing coordinator,
  authentication providers, session registry, dispatcher, and shutdown lifecycle once each.
- Administrative invalidation retains the required best-effort notification and force-close order,
  even when the adapter is absent or the client writer has failed.
- Host shutdown first prevents new public admissions, then cancels/tears down public sessions and
  private IPC through one shared cancellation path; adapter absence must not prevent trust persistence
  or clean public-session teardown.
- The replacement host's public listener is composed for isolated development/test execution only
  while `bridge/` remains production. Do not change the adapter's production launch path, old-bridge
  packaging, or cutover wiring in this phase.
- Stage 4 does not wire live state capture/publication; that remains Stage 5/6 work.

## Invariants

- The adapter never owns pairing, trust, authorization, public protocol, or retry policy.
- Private IPC remains bounded, authenticated, generation-scoped, and unreachable from clients.
- Adapter loss leaves pairing/trust/session behavior controlled and observable; it does not fabricate
  game state or block public transport.
- Host shutdown closes public sessions and private IPC through deterministic cancellation paths.
- Option A remains intact: no host-instance identity is invented and no live state is published.

## Allowed files/modules

- `host/DovahLink.Host/Adapter/Ipc/` — only the private message/connection additions required for
  approved Skyrim-facing notifications and their host-side tests.
- `adapter/ipc/`, `adapter/papyrus/`, and `adapter/plugin/` — only the matching typed notification
  handling required by this concept.
- `host/DovahLink.Host/Program.cs` and composition files — public/client service wiring and shared
  shutdown only.
- `host/DovahLink.Host.Tests/Client/`, `Adapter/Ipc/`, and narrowly scoped integration tests.
- `adapter/tests/ipc/`, `adapter/tests/papyrus/`, and `adapter/tests/plugin/` only for matching
  boundary proof.
- No live capture/state publication, broad admin API, `bridge/`, SDK, or app files.

## Proof obligations

Expected focused test files:

- `host/DovahLink.Host.Tests/Adapter/Ipc/AdapterIpcSessionTests.cs`
- `host/DovahLink.Host.Tests/Adapter/Ipc/AdapterIpcConnectionTests.cs`
- `host/DovahLink.Host.Tests/Adapter/Ipc/AdapterIpcChannelIntegrationTests.cs`
- `host/DovahLink.Host.Tests/Client/Integration/PublicClientBoundaryIntegrationTests.cs`
- `host/DovahLink.Host.Tests/Client/Integration/AdapterNotificationIntegrationTests.cs`
- Matching native tests under `adapter/tests/ipc/`, `adapter/tests/papyrus/`, and
  `adapter/tests/plugin/`, using the existing native test targets rather than ad-hoc runners.

- Pairing challenge display and redisplay reach the adapter only after host policy accepts them and
  never disclose the code through the public wire response beyond the canonical outcome rules.
- Adapter absence, disconnect, queue pressure, and shutdown yield bounded controlled behavior.
- A pairing request while the adapter is absent or unable to accept display is reported as unavailable
  and leaves no active challenge that a later client can accidentally resume as displayed.
- A private display acknowledgement is bounded and correlation-scoped to the current adapter
  connection generation; a late acknowledgement from an older adapter connection cannot make a new
  challenge appear available.
- Adapter-originated trust requests are authorized by the mutually authenticated adapter channel,
  validated against the closed operation set and bounded fields, dispatched only to host trust
  services, and answered without exposing raw credentials or persistence exceptions.
- Adapter Papyrus forwarding tests prove the exact operation/argument matrix from `PLAN.md`, bounded
  behavior when the host is absent, and no blocking wait on a game-thread callback.
- A failed initial display operation rolls back the host challenge atomically; a failed redisplay
  leaves an already-displayed challenge and its cooldown unchanged, returning only the controlled
  retryable public failure specified by Concept 03.
- The hard wrong-attempt path sends the attempts-exhausted notification without a code, and the
  wrong-code path sends the current code only when the coordinator's independent auto-renotify
  decision permits it.
- Public and private listeners cannot be confused or cross-accessed.
- A session invalidated by revoke, block, Reset Trust, or Factory Reset receives best-effort terminal
  notification and is then force-closed; failed notification does not preserve the session.
- Host restart, adapter restart, client reconnect, cancellation, peer failure, and timeout preserve
  fresh session and play-context rules.
- Real/private IPC tests and independent C# client tests prove the complete Stage 4 boundary without
  adding live state publication.
- Startup tests prove trust persistence is loaded before the composition root admits any client,
  missing persistence means an empty store, and malformed/undecryptable persistence fails closed
  rather than silently admitting a client. This proof belongs here rather than to Concept 02 because
  it is a property of the composition root's own startup ordering (`Program.cs`), which this concept
  builds; Concept 02's admission boundary only consumes an already-loaded `ITrustStore` and performs
  no persistence loading of its own. See `DIVERGENCES.md`'s D4.

## Non-goals

- Native Skyrim state mappings, cadence, capture delivery, subscriptions, snapshots, or events.
- Final protocol cutover, LAN exposure, packaging release work, or old-bridge removal.
- A generic adapter command bus or public trust-administration protocol.

## Completion criteria and evidence

- Focused host and private-boundary tests pass, including concurrent reads/writes, cancellation,
  peer failure, timeout, reconnect, and administrative invalidation.
- Host and adapter remain independently buildable and the public listener is not coupled to private
  IPC implementation types.
- The Stage 5 handoff identifies any approved identity limitation and begins with a clean,
  authenticated, session-owned client boundary.
- Startup tests prove trust persistence loads before the composition root admits any client, missing
  persistence means an empty store, and malformed/undecryptable persistence fails closed -- the proof
  obligation handed off from Concept 02 per D4.
