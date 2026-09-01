# Stage 4 — Host Client Boundary and Pairing

## Source

- Source path: `host/PLAN.md`
- Phase: Stage 4 — Host Client Boundary and Pairing
- Snapshot date: 2026-09-01
- Source fingerprint: `host/PLAN.md` — `7434ECE0A3ACDBF9A7D86460F080D1BC7310B4AF6C2A15BF8868C676DCB1CC0C`
- Current source matches this fingerprint: yes

The source plan is a durable repository planning source and remains authoritative; the maintainer
should persist the phase package and any required source-plan changes deliberately.

## Required context before implementation

The implementing agent must read the following before each concept begins, not rely on this package
as a replacement: `AGENTS.md`, `README.md`, `PRODUCT.md`, `ARCHITECTURE.md`, `ROADMAP.md`,
`CONTRIBUTING.md`, `ai/context/common.md`, `ai/context/dotnet/csharp-style.md`,
`ai/context/host/architecture.md`, `ai/context/host/migration-audit.md`,
`ai/context/protocol/conventions.md`, `ai/context/protocol/compatibility.md`,
`ai/context/protocol/security.md`, `ai/context/integration/testing.md`, the current
`protocol/schema/README.md`, `ai/context/adapter/architecture.md`, the relevant
`ai/context/skse/architecture.md`, `ai/context/skse/cpp-style.md`,
`ai/context/skse/runtime-quirks.md`, `ai/context/skse/testing.md`, and the completed Stage 3
package under `plans/stage-3-thin-native-adapter-private-ipc/`. The source implementation and
relevant tests must then be read directly; CodeGraph is discovery only.

## Objective and boundaries

This package preserves the Stage 4 source scope verbatim:

> **Scope:**
> Implement the host-facing client connection boundary. The C# host accepts
> loopback WebSocket clients, validates typed messages, owns pairing and trust,
> and forwards only the narrow Skyrim-facing notifications or commands required
> through the adapter channel.

> **Acceptance criteria:**
>
> - The host owns the WebSocket listener, handshake, read loop, write loop,
>   timeout, cancellation, and connection teardown.
> - Pairing, trusted-device credentials, developer authentication, revocation,
>   blocking, reset, and session invalidation are host-owned.
> - The host does not expose adapter IPC as a public client endpoint.
> - Public messages are bounded, validated, authorized, and rejected before
>   reaching adapter code.
> - The host preserves the required fresh session and play-context identity
>   semantics.
> - Independent C# tests cover concurrent reads/writes, cancellation, peer
>   failure, timeout, reconnect, and administrative invalidation.

> **Not in scope:** live state publication or final removal of the old bridge.

> **Depends on:** Stage 2 and Stage 3

The host remains the sole owner of public transport, protocol mapping, authentication, pairing,
trust, authorization, session lifecycle, and administrative invalidation. The adapter remains the
sole owner of Skyrim-facing presentation/glue and private IPC execution. The public protocol and
private IPC contracts remain separate, and `bridge/` remains frozen reference behavior.

## AI execution guardrails

- Before every implementation turn, read this package's `PLAN.md`, `CONTEXT.md`, the active concept
  file, the source `host/PLAN.md`, and the current source fingerprint. Stop if the fingerprint or
  active concept status does not match the ledger.
- Execute exactly one concept per turn, in graph order. Do not implement a later concept because a
  needed seam is inconvenient or absent; amend the plan and record a divergence first.
- Change only the exact files/directories listed by the active concept. A required file outside that
  allowlist is a plan gap, not permission to broaden the diff.
- Do not add a public protocol field, message, error code, authentication method, limit, dependency,
  folder, generic command bus, or compatibility shim unless this package and `DIVERGENCES.md` are
  updated first and the maintainer approves the material change.
- Do not modify completed Stage 3 plan files or unrelated `bridge/` implementation to make Stage 4
  easier. If a completed Stage 3 seam is insufficient, record the exact required boundary change in
  the active Stage 4 concept and stop for review if it changes Stage 3 semantics.
- Do not use `adapterInstanceId`, `ConnectionId`, `sessionId`, PID, port, path, or owner lifetime as
  a substitute for the deferred public host-instance identity in D1.
- Do not mark a concept complete on a build alone. Its focused tests, independent fresh-eyes review,
  convention audit, traceability check, source-fingerprint check, and `CONTEXT.md` handoff are all
  required.
- The fresh-eyes reviewer is read-only and receives the source path/fingerprint, this phase `PLAN.md`,
  the active concept, applicable traceability rows, `CONTEXT.md`, and the complete changed-file
  diff. Classify findings as blocking, required for the concept, optional debt, or out of scope.
- Tests must follow the local C# conventions: documented behavior-bearing contracts, constructor
  injection, one primary type per file, and controllable thread-safe fakes for timing/lifecycle
  behavior. Do not introduce a mocking framework or a second protocol fixture authority.
- At the end of each concept turn, stop and name the exact next concept. Do not auto-proceed.

## Inherited invariants

- The host is an out-of-process .NET process and never loads Skyrim/CommonLib types or calls game
  runtime APIs.
- Public client transport is loopback-only on `127.0.0.1` and `::1`; LAN or wildcard exposure is
  not introduced by configuration or command-line flags.
- Public input is bounded before application handling: 1 MiB frame, depth 32, 4 KiB strings, 128
  array items, 64 object members, 100 messages per second per client, and 10,000 messages per
  session. Limit changes require explicit approval.
- Public output is bounded by the approved 128-message/2 MiB per-session queue policy; Stage 4
  responses use the reserved control/recovery capacity because no live data lane exists yet.
- The public connection has one active client during the first proof, a five-second handshake
  deadline, and a 60-second idle/liveness timeout.
- Malformed, oversized, unauthenticated, unauthorized, expired, replayed, and unsupported input is
  rejected before adapter/game code. Invalid framing is closed without attempting an error reply.
- A fresh `ConnectionId` and `sessionId` are created for every accepted socket. A session is bound
  to its owning socket and is invalidated before transport release; reconnect never resumes it.
- `clientId` persists across reconnects independently of `sessionId`, `ConnectionId`,
  `adapterInstanceId`, and `playContextId`.
- Developer-token authentication, bootstrap unpaired authentication, and trusted-device
  authentication are distinct providers/tier sources. Developer-token sessions are not Known
  Devices and are not invalidated by Block or Revoke solely through a matching self-declared
  `clientId`.
- Failed developer-token and failed trusted-device credential attempts use independent bounded
  throttles; a successful one-time developer token is committed only after session admission.
- Restricted sessions accept only their approved pairing/liveness capabilities until pairing
  succeeds. A successful pairing acknowledgement upgrades the same session exactly once.
- Pairing remains a recoverable challenge → pending credential → trusted handshake. Pending
  credentials never authenticate ordinary sessions, and final confirmation remains idempotent.
- Trust mutations change authoritative trust first, then best-effort notify affected sessions, then
  force-close them. Notification delivery is not a security dependency.
- Pairing codes and credentials never appear in normal logs, fixtures, raw diagnostics, or protocol
  errors. Credential persistence remains behind the approved per-user DPAPI trust-store boundary.
- Pairing challenge display and administrative Skyrim glue are adapter-facing notifications only; the
  adapter makes no pairing, trust, authorization, or retry decision.
- The public envelope remains distinct from private IPC messages. Public clients cannot reach the
  private adapter listener or its message vocabulary.
- Host-originated messages use one coherent play-context snapshot: `playContextId` is the current
  adapter-reported context or genuine `null` before/after a context exists; it is never fabricated
  from adapter availability, process identity, or a default string.
- Stage 4 does not change the production path while `bridge/` remains the production
  implementation. Do not alter old-bridge packaging, launch wiring, or its ownership of port `58231`
  to activate the replacement beside it; replacement-host real-listener checks use an injected free
  test port or an explicitly isolated process setup.
- Trust persistence must load successfully before the host admits clients. A missing store is an
  empty store as defined by the persistence contract; malformed or undecryptable persisted data
  fails closed rather than silently resetting trust.
- Option A is approved: Stage 4 does not invent a public host-instance identity. The existing
  `bridgeInstanceId` envelope field remains explicitly unavailable for this transition boundary,
  no live state is published in Stage 4, and the host-identity/protocol-revision work is deferred
  and recorded in `DIVERGENCES.md`.
- The current public schema still requires a non-empty `hello_ack.payload.bridgeVersion`. Until the
  replacement is the production implementation, use the existing project-owned transitional value
  from `bridge/vcpkg.json`'s `version-string`; do not bump versions or add release bookkeeping on
  this phase branch. Host-owned release-version authority is a later cutover/release decision.

## Requirement IDs

- **R1** — “The host owns the WebSocket listener, handshake, read loop, write loop, timeout, cancellation, and connection teardown.”
- **R2** — “Pairing, trusted-device credentials, developer authentication, revocation, blocking, reset, and session invalidation are host-owned.”
- **R3** — “The host does not expose adapter IPC as a public client endpoint.”
- **R4** — “Public messages are bounded, validated, authorized, and rejected before reaching adapter code.”
- **R5** — “The host preserves the required fresh session and play-context identity semantics.”
- **R6** — “Independent C# tests cover concurrent reads/writes, cancellation, peer failure, timeout, reconnect, and administrative invalidation.”

## Stage 4 public message coverage

The following matrix is part of the phase scope. It prevents a later implementation from silently
omitting a canonical message or accepting a server-originated message from a client:

| Message | Direction and Stage 4 behavior |
|---|---|
| `hello` | Client → host, first frame only; authenticate and admit a fresh session. |
| `hello_ack` | Host → client only; fresh session, transitional unavailable `bridgeInstanceId`, and non-empty `bridgeVersion`. |
| `capabilities` | Host → client after `hello_ack` with the currently empty capability list; accept a client's post-hello empty capability advertisement with no response, reject malformed payloads as `malformed_message`, and reject non-empty capabilities as `unsupported_capability`. |
| `ping` / `pong` | Client liveness request and host response; WebSocket control-frame liveness remains separate. |
| `pairing_request` / `pairing_status` | Restricted client request and host status; `available` is allowed only after adapter display acceptance. |
| `pairing_confirm` / `pairing_outcome` | Client code submission and host outcome; wrong-attempt automatic redisplay and exhausted-attempt notification remain adapter-facing side effects. |
| `pairing_ack` / `pairing_outcome` | Client durable-credential confirmation and host outcome; successful confirmation upgrades the same session. |
| `pairing_renotify` / `pairing_outcome` | Restricted client request and host outcome; code is displayed only through the adapter. |
| `pairing_cancel` / `pairing_outcome` | Restricted client request and host outcome. |
| `rename_request` / `rename_outcome` | Full-session client request and host response. |
| `subscribe` / `subscription_ack` | Full-session request is parsed and answered with all requested areas rejected because Stage 4 registers no state areas; restricted sessions reject it before application handling. |
| `snapshot_request` | Full-session request is parsed and rejected as unsupported because Stage 4 publishes no state; restricted sessions reject it before application handling. |
| `state_snapshot` / `state_event` | Host-originated Stage 5 messages only; never accepted as client input in Stage 4. |
| `error` | Host-originated only, with canonical codes and redacted diagnostics. |
| `session_invalidated` | Host-originated unsolicited terminal message only; best-effort before forced close. |

Every inbound message uses the canonical envelope and correlation rules. Every host-originated
message receives a fresh cryptographically random `messageId` unique within the socket session.

The client `hello` reserves its own `messageId` before admission and the server's `hello_ack`
correlates to it. After admission, client messages carry the socket-bound `sessionId` and their
declared `clientId`; host messages carry the server-issued session identity, omit `clientId` except
where the schema explicitly requires it (such as `hello_ack`), and use `null` correlation only for
the schema's unsolicited messages. `hello_ack` correlates to the client's `hello`. A play-context
change during response construction must be handled through the tracker snapshot contract rather
than by combining separate unsynchronized getters.

## Stage 4 private adapter message coverage

The new private messages are not public protocol messages and must remain a closed typed vocabulary:

| Direction | Intent | Required behavior |
|---|---|---|
| Host → adapter | Pairing code available | Initial display or manual redisplay of the host-owned active code; bounded acknowledgement for operations that gate public success. |
| Host → adapter | Pairing code incorrect | Best-effort redisplay with incorrect-attempt presentation after the host's independent cooldown decision. |
| Host → adapter | Pairing attempts exhausted | No-code terminal notification after the hard wrong-attempt limit. |
| Adapter → host | Trust administration request | Closed operations with exact shapes: `help` (no argument), `list` (scope `all`, `trust`, or `block`), `revoke`/`block`/`unblock`/`forget` (five-digit `shortId`), `reset-trust` (no argument), `reset` (no argument), and `confirm-reset` (six-digit confirmation code). |
| Host → adapter | Trust administration result | Typed bounded result for the originating adapter request; host remains the mutation authority. |

Every private request/response is correlation- and adapter-connection-generation-scoped, bounded,
authenticated by the completed Stage 3 channel, and tested in both C# and C++. No arbitrary command
name, client-provided Skyrim key, or public-message passthrough is permitted.

Papyrus-facing administration must not perform an unbounded synchronous wait on a Skyrim/game
thread for a host response. The adapter must use the approved bounded/non-blocking forwarding seam
and return an explicit host-not-ready or controlled failure result when a response cannot be obtained
within that seam. If the current Papyrus callback/thread model cannot satisfy this, implementation
must stop and advertise the missing lifecycle decision rather than blocking Skyrim.

## Concept graph

1. **Public WebSocket transport and connection lifecycle** (`01-public-websocket-transport.md`)
   establishes the loopback listener, WebSocket upgrade, per-connection I/O ownership, deadlines,
   cancellation, and deterministic teardown. It is the transport foundation and does not interpret
   application messages.
2. **Public protocol validation, authentication, and session admission** (`02-authentication-and-session-admission.md`)
   consumes the transport and establishes typed envelope validation, hello authentication, replay
   protection, trust-tier admission, fresh session identity, and authorization context.
3. **Pairing and client message dispatch** (`03-pairing-and-client-dispatch.md`) consumes an
   authenticated session and maps canonical pairing/liveness/rename messages to the existing host
   services, including restricted-session upgrade and administrative invalidation behavior.
4. **Adapter-facing notification and host composition** (`04-adapter-notification-and-composition.md`)
   connects approved pairing/admin notifications to the private adapter channel, completes the
   host composition root, and proves the live client/host/adapter boundary without publishing live
   state. It depends on the preceding client concepts and the completed Stage 3 IPC seam.

The ordering moves from transport ownership to admission/security, then application dispatch, then
cross-process notification and composition. No concept introduces live state publication; that
belongs to Stage 5.

## Traceability matrix

| Requirement | Concept(s) | Status | Mapping |
|---|---|---|---|
| R1 | 01, 04 | decomposed | Transport lifecycle is isolated from composition and shutdown wiring |
| R2 | 02, 03, 04 | decomposed | Authentication/session context, pairing/trust dispatch, and adapter-facing invalidation |
| R3 | 01, 04 | decomposed | Separate public listener and private adapter channel composition |
| R4 | 02, 03, 04 | decomposed | Bounds/validation, authorization, and proof that rejected public input cannot reach adapter code |
| R5 | 02, 03, 04 | decomposed | Fresh socket/session admission, play-context envelope context, and reconnect/invalidation tests |
| R6 | 01, 02, 03, 04 | decomposed | Focused C# concurrency, failure, timeout, reconnect, and administrative tests |

## Phase completion gate

- All four concepts are complete and their focused checks pass.
- The host accepts only loopback WebSocket clients and never exposes the private adapter listener.
- Public framing, typed decoding, input limits, message-ID uniqueness, hello ordering, authentication,
  trust tiers, authorization, and canonical error behavior are tested independently of the adapter.
- Pairing completes through challenge, credential issuance, durable confirmation, and same-session
  trust upgrade, including expiry, pacing, cancellation, reconnect grace, and administrative fences.
- Developer-token sessions remain separate from Known Device trust and survive Block/Revoke where
  the security contract requires it; Factory Reset still invalidates every session.
- Administrative invalidation sends best-effort `session_invalidated` before force-closing the owning
  socket, while all teardown paths invalidate the session before releasing the connection slot.
- Pairing display/redisplay and approved administrative notifications cross the private IPC boundary
  as narrow typed messages without moving policy into the adapter.
- Initial/manual display acknowledgement, wrong-code automatic redisplay, and attempts-exhausted
  notification are all covered; an implementation that only forwards the first pairing code is
  incomplete.
- No live state publication, old-bridge deletion, LAN exposure, or speculative public admin protocol
  has been introduced.
- Host, independent validation, and native/private-IPC checks pass; CodeGraph is rerun against the
  final changed area; and the diff contains no unrelated changes.
- The final source-plan checkbox/status is updated only after all requirements and approved
  divergences are complete; the package fingerprint and `CONTEXT.md` are then reconciled with that
  source-plan change.
- The source plan's Stage 4 completion mark does not authorize Stage 7 cutover, old-bridge deletion,
  release packaging, or production activation.
- Documentation changes are limited to the phase package, required host/adapter private-contract
  documentation, and source-plan completion bookkeeping. Do not change `protocol/`, SDK, app,
  release-version, or changelog files unless a separately approved divergence requires it.

## Divergence policy

See `DIVERGENCES.md` for the approved Option A decision to defer public host-instance identity and
keep `bridgeInstanceId` explicitly unavailable during this transition boundary, plus D2's approved
reconciliation of the restricted-session allowlist across the source documentation.
