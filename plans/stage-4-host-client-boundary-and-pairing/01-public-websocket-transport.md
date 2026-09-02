# 01 — Public WebSocket transport and connection lifecycle

Status: complete

Covers: R1, R3, R6

Depends on: Stage 2 and Stage 3; no sibling concept dependency.

## Owner and boundary

The C# host owns the public loopback WebSocket listener and one transport object per accepted
client. This is a stable concept because socket ownership, WebSocket upgrade, I/O concurrency,
deadlines, cancellation, and physical teardown are transport concerns independent of pairing or
message-specific policy.

## Inputs and outputs

Inputs are loopback TCP connections, the host shutdown token, and bounded transport configuration.
Outputs are an owned WebSocket connection context for the next concept, transport failures,
timeout/cancellation outcomes, and deterministic connection-close notifications.

## Contracts

- Bind only `127.0.0.1` and `::1`; do not bind wildcard or LAN addresses.
- Reserve the approved public loopback port `58231` for the eventual production composition. Stage 4
  must not activate the replacement listener beside the frozen `bridge/` production path; its isolated
  development/test composition must inject a different explicit loopback port. Public production
  binding must not silently use the private IPC port or an ephemeral public port.
- Perform the WebSocket upgrade and establish the handshake deadline before application dispatch.
- Own exactly one reader and one serialized writer per socket. Application code never writes to the
  WebSocket directly.
- The message handler receives a narrow transport capability scoped to the exact connection that
  delivered each message -- a bounded/serialized send and an orderly-close request -- rather than
  only inbound bytes. The capability exposes no raw `WebSocket`/`Stream`/`Socket` type and can never
  resolve or address a different connection, including after its own connection has ended.
- A caller-requested orderly close is distinct from the forced close an unadmittable outbound
  message already triggers: an orderly close gives whatever is already queued a bounded opportunity
  to reach the wire before the read side is interrupted, rather than racing it out of existence.
- Apply the five-second establishment deadline, 60-second idle/liveness deadline, 1 MiB complete
  message bound, and 100 inbound messages-per-second bound at the transport boundary. Fragmented
  messages must be accumulated only up to the bound; a larger message is closed without an error
  response.
- Bound outbound storage to the approved 128-message/2 MiB per-session policy. Since Stage 4 has no
  live data lane, all responses and terminal-control messages use the reserved control capacity; a
  response that cannot be admitted follows the controlled close path rather than growing memory.
- Preserve WebSocket control frames and use WebSocket-level Ping/Pong for established liveness;
  do not invent a second transport heartbeat or use application `ping` as a substitute.
- Cancellation, peer failure, timeout, normal close, and host shutdown converge on one idempotent
  teardown path. The path releases the public connection slot only after session invalidation is
  notified by the authenticated layer.
- Do not import or expose private adapter IPC message types through the public transport.

## Invariants

- No public transport path can reach `AdapterIpcListener` or private IPC codecs.
- Oversized or invalidly framed input is rejected before attempting application decoding.
- Concurrent application responses are serialized; no concurrent WebSocket writes occur for one peer.
- A stalled peer cannot hold the listener slot indefinitely.
- Teardown never blocks adapter capture or private IPC recovery.
- The transport does not decide authentication, pairing, authorization, trust, or state semantics.

## Allowed files/modules

- New files only under `host/DovahLink.Host/Client/Transport/`.
- `host/DovahLink.Host/Constants.cs` only for approved public-client bounds.
- `host/DovahLink.Host/Program.cs` only for public listener composition and shared shutdown wiring.
- New files only under `host/DovahLink.Host.Tests/Client/Transport/` plus existing test doubles required
  to control timing and transport failure.
- No `protocol/`, `sdk/`, `app/`, `bridge/`, or native adapter production files.

## Proof obligations

Expected focused test files:

- `host/DovahLink.Host.Tests/Client/Transport/PublicWebSocketListenerTests.cs`
- `host/DovahLink.Host.Tests/Client/Transport/PublicWebSocketConnectionTests.cs`

Existing test doubles may be extended only when they remain reusable transport fakes; do not place
public-boundary behavior in integration-client fixtures.

- Loopback-only binding is proven for both supported loopback addresses and wildcard binding is not
  silently accepted.
- Handshake timeout, idle timeout, cancellation, normal close, peer failure, and host shutdown all
  reach bounded deterministic teardown.
- Concurrent response attempts produce ordered serialized writes.
- Partial reads and bounded frames are handled without unbounded allocation.
- A failed public connection does not stop the listener from accepting a later connection.
- A fragmented message that crosses the 1 MiB bound is rejected before JSON parsing and does not
  allocate an unbounded buffer.
- The transport handles text-only application messages, binary input, invalid WebSocket framing,
  close frames, control frames, and a peer that never completes the upgrade distinctly and safely.
- A connection from a non-loopback address is rejected even if it reaches the listener, and a second
  public connection is rejected while the single-client admission slot is occupied; it never replaces
  the existing session.
- A graceful close has a bounded drain and an abort/dispose fallback; a peer that withholds close
  completion cannot hang host shutdown or connection-slot release.
- The private adapter listener remains independently composed and unreachable from public clients.

## Non-goals

- Hello authentication, message meaning, pairing, trust, state publication, or adapter commands.
- LAN/Wi-Fi exposure, TLS, or a new public transport contract.
- Reusing the private IPC framing or public envelope as a transport abstraction.

## Completion criteria and evidence

- Focused transport tests pass.
- The host remains buildable without native headers or the old bridge.
- The next concept receives a transport context with explicit reader/writer ownership and teardown
  hooks, without exposing private adapter types.

A pre-merge fresh-eyes audit found this last criterion was not actually met: the original
`HandleMessageAsync(payload, cancellationToken)` shape gave the handler no way to respond on, or
close, the exact connection that delivered a message -- the only reachable path was
`PublicWebSocketListener.CurrentConnection`, a global/listener-owned lookup unsafe across reconnects.
A corrective pass added `IPublicConnectionContext`/`PublicConnectionContext`
(`host/DovahLink.Host/Client/Transport/PublicConnectionContext.cs`), now passed as
`HandleMessageAsync`'s first parameter, and `IPublicWebSocketConnection.RequestClose()` alongside the
existing `TrySend`, all within this concept's existing file allowlist. See `CONTEXT.md` for the full
corrective-pass record and updated verification counts.
