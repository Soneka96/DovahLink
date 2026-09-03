# 02 — Public protocol validation, authentication, and session admission

Status: complete

Covers: R2, R3, R4, R5, R6

Depends on: `01-public-websocket-transport.md`; Stage 2 authentication, trust, identity, and
session services; Stage 3 host composition.

## Owner and boundary

The host owns the public protocol adapter and admission policy. This is a stable concept because
typed envelope validation, hello authentication, replay protection, trust-tier admission, fresh
session identity, and message authorization form one security boundary before application dispatch.

## Inputs and outputs

Inputs are complete bounded WebSocket text messages and host-owned authentication/trust/session
services. Outputs are typed client messages, an admitted session context, canonical protocol errors,
and rejection/close decisions.

## Contracts

- Consume the canonical public schema from `protocol/schema/README.md`; do not copy private IPC
  message types or define a second protocol authority in the host.
- Require `hello` as the first client message and validate all required fields, field types, closed
  enum values, payload shape, message-ID uniqueness, and authentication-method/token rules.
- Support the exact three hello methods: `unpaired` has no token; `trusted_device_credential` has a
  hex credential checked against the trusted record; `one_time_local_token` is available only through
  the explicit developer-token configuration/provider and is never silently enabled by a default.
  The existing `ILocalConnectionTokenAuthenticator` atomic one-time/rate-limit guarantees must be
  preserved or its replacement must be separately documented and tested.
- Use the existing approved `DOVAHLINK_BRIDGE_TOKEN` environment variable for the current
  transition, which is the repository's documented equivalent configuration for the security
  document's `DOVAHLINK_DEV_TOKEN` wording. Do not add a second alias, silently rename the variable,
  or add a fallback on this phase branch; an eventual host-release naming change belongs to an
  explicit later compatibility decision.
- Maintain a separate failed trusted-credential throttle from the developer-token throttle. A
  malformed or non-matching trusted credential consumes only the credential failure budget; an
  `unpaired` hello has no credential budget to consume.
- Reject malformed `clientId`, endpoint, auth method, token presence, and envelope identity fields
  before calling authentication services. Authentication failures return the correct canonical
  pre-session error (`unauthenticated`, `revoked`, or `blocked`) without revealing which secret
  check failed.
- Validate JSON with the approved maximum depth 32, string length 4 KiB, array length 128, and
  object-member count 64 before materializing typed DTOs. Ignore only unknown fields the current
  schema permits as optional forward-compatible data; reject invalid required fields and unknown
  message types as malformed protocol input.
- Create a fresh `ConnectionId` and `sessionId` only after successful admission. Bind them to the
  exact socket and retain one coherent session record containing client identity, authentication
  source, trust tier, and invalidation state; do not reconstruct those facts from independent getters.
- Enforce these exact inbound allowlists: before hello only `hello`; restricted sessions allow
  `ping`, `capabilities`, `pairing_request`, `pairing_confirm`, `pairing_ack`, `pairing_renotify`,
  and `pairing_cancel`; full sessions allow liveness, capabilities, rename, and Stage 4's explicit
  unsupported-state request handling. Server-originated message types are never accepted from a
  client.
- Retain all seen client `messageId` values for the session up to the 10,000-message bound and
  reject duplicates as replay before dispatch.
- Enforce the three-protocol-violations-in-30-seconds close policy with bounded security-failure
  diagnostics. Do not retry malformed input indefinitely and do not send protocol errors for frames
  that were too large or otherwise unsafe to decode.
- Reject stale/foreign session IDs, unsupported messages, unauthorized messages, and limit violations
  before adapter dispatch. Do not send an error for a frame that cannot be safely decoded.
- Emit `hello_ack` with a non-empty transitional `bridgeVersion` from the existing project-owned
  `bridge/vcpkg.json` release literal, a fresh session envelope, and the correct `clientIdentityKind`.
  Keep `bridgeInstanceId` explicitly unavailable under D1 and do not publish state.
- Preserve the admission ordering: decode/validate hello; perform the applicable auth/throttle
  reservation; generate fresh session and response IDs; reserve the bounded session slot; recheck
  trust for races after admission; commit a successful one-time developer token only after session
  admission succeeds; then mark the socket authenticated and send `hello_ack`/capabilities. A full
  session slot must not consume a retryable one-time token. `SessionRegistry.TryCreate` reserves
  active-session capacity immediately on success, before the trust recheck runs; when that recheck
  rejects the connection, call `SessionRegistry.Invalidate(sessionId, connectionId)` to release the
  reservation before returning the rejection result, so a losing race against a concurrent
  revocation/block never permanently consumes active-session capacity. The rejection result itself
  is unaffected by that rollback.
- Enforce the approved 10-second pre-authentication hello deadline from
  `ai/context/protocol/security.md`'s "Input limits" and "Connection liveness": once a connection
  completes the WebSocket upgrade, this concept starts that deadline and owns its semantics -- the
  transport layer only supplies the generic mechanism (`IPublicConnectionContext.RequestClose()`)
  to close the exact connection, and must not itself know what `hello` means or gain a
  transport-level `HelloTimeout`/`WaitForHelloAsync` API of its own. A valid `hello` accepted before
  the deadline atomically cancels it; a deadline that fires first closes that exact connection and
  releases its slot without admitting a late `hello`. The deadline is scoped to one connection's
  exact lifetime and is never reused, restarted, or capable of affecting a later reconnect's own
  connection/deadline.
- Recheck `SessionRegistry.IsActive(sessionId, connectionId)` immediately before finalizing admission
  on every hello authentication path, not only the trust-backed recheck against Block/Revoke: an
  unconditional `SessionRegistry.InvalidateAll()` (Factory Reset) can land in the same window and is
  not itself a trust-store condition the earlier recheck would catch. A losing race here rolls back
  any reserved one-time token and rejects the connection without ever sending `hello_ack` for a
  session the registry no longer knows about.
- Classify a structurally valid client message the current session's trust tier does not authorize as
  `unauthorized`, distinct from a protocol shape/direction violation -- a server-originated message
  type, or `hello` received after a session already exists -- which remains `malformed_message` since
  no trust tier could ever authorize it.

## Invariants

- A public validation or authorization failure cannot invoke adapter code.
- Developer-token sessions remain separate from Known Device trust and are not targeted by Block or
  Revoke solely through a matching `clientId`.
- A reconnect creates a new socket, `ConnectionId`, and `sessionId`; it never resumes the old one.
- A session cannot be transferred across sockets or reused after teardown.
- Public errors contain canonical codes without credentials, filesystem paths, or raw exceptions.
- Authentication and session decisions remain host-owned and do not depend on adapter availability.
- A transport-live, not-yet-admitted connection can never hold the single public admission slot past
  the approved 10-second pre-authentication hello deadline merely by continuing to answer WebSocket
  Ping/Pong; that liveness signal does not extend or substitute for this deadline. A pre-auth
  deadline belongs to exactly one connection's exact lifetime: a deadline that outlives its own
  connection's teardown can never fire against, close, or otherwise affect a different (including a
  same-client reconnect's) connection.
- A connection's local `admitted` state is never treated as authorization independent of the
  authoritative `SessionRegistry`: once the registry no longer considers `(sessionId, connectionId)`
  active for any reason, the very next post-admission message on that connection is rejected as
  `stale_session`, even though the connection's own local state has not itself changed.

## Allowed files/modules

- New files only under `host/DovahLink.Host/Client/Protocol/` and
  `host/DovahLink.Host/Client/Authentication/`.
- `host/DovahLink.Host/Authentication/LocalConnectionTokenAuthenticator.cs` only for narrowly
  required interface or behavior integration; do not weaken its existing guarantees.
- `host/DovahLink.Host/Sessions/` and `host/DovahLink.Host/Trust/` only for session metadata,
  admission, and invalidation seams required by the boundary.
- New tests only under `host/DovahLink.Host.Tests/Client/Protocol/` and
  `host/DovahLink.Host.Tests/Client/Authentication/`, with focused regression tests in existing
  `Authentication/`, `Sessions/`, and `Trust/` directories.
- No public schema or SDK change unless a new material divergence is approved first.

## Proof obligations

Expected focused test files:

- `host/DovahLink.Host.Tests/Client/Protocol/PublicEnvelopeCodecTests.cs`
- `host/DovahLink.Host.Tests/Client/Authentication/PublicHelloAdmissionTests.cs`
- `host/DovahLink.Host.Tests/Sessions/SessionRegistryTests.cs` for session metadata/invalidation
  regressions
- `host/DovahLink.Host.Tests/Authentication/LocalConnectionTokenAuthenticatorTests.cs` for token
  atomicity/regression coverage

- Malformed, oversized, duplicate, replayed, unauthenticated, unsupported, and unauthorized input
  is rejected before application or adapter handling.
- Concurrent hello attempts respect the one-client bound and token atomicity.
- A rejected full-slot attempt does not consume a valid one-time token, while failed token or
  credential attempts consume only their applicable failure/throttle budget.
- A valid but currently revoked trusted credential returns `revoked`; an unpaired session for a
  revoked identity remains eligible to re-pair; a blocked identity is rejected for both unpaired and
  trusted-device admission, while developer-token admission is not classified by Known Device state.
- Each successful authentication method yields the correct trust tier and fresh session identity.
- Restricted sessions cannot access non-pairing messages; successful pairing upgrades exactly once.
- Developer-token sessions remain unaffected by client-scoped Block/Revoke while Factory Reset still
  invalidates them.
- The transitional `bridgeVersion` is present and non-empty, while no implementation treats
  `bridgeInstanceId` as a usable identity under D1.
- The capability exchange is explicit: the host sends an unsolicited empty `capabilities` message
  after `hello_ack`; a client may send an empty `capabilities` advertisement with no response,
  malformed capability payloads return `malformed_message`, and non-empty capabilities return
  `unsupported_capability` without allocating state-area structures.
- Socket loss, timeout, cancellation, and host shutdown invalidate sessions exactly once.
- Canonical error/session envelope behavior is tested with independent C# clients and existing
  fixtures where the current contract permits it.
- A connection that completes the WebSocket upgrade but never sends `hello`, while remaining
  WebSocket-liveness-compliant (continuing to answer Ping/Pong), is closed once the approved
  10-second pre-authentication hello deadline elapses, and its listener admission slot becomes
  reusable.
- A valid `hello` accepted before the 10-second deadline cancels the pending eviction; the
  connection is not later closed by that former deadline.
- A `hello` accepted concurrently with the deadline firing produces exactly one authoritative
  outcome: either the session is admitted and never subsequently closed by the deadline, or the
  deadline closes the connection and no `hello` received after that point is admitted. No interleaving
  admits a session that a racing deadline then still closes.
- A pre-auth deadline belongs to its own connection's exact lifetime: a stale deadline left over from
  a connection that has already ended must never close, or otherwise affect, a different connection,
  including a same-client reconnect that begins after the first connection's deadline was already
  running.
- After a connection occupying the single public admission slot is evicted by this deadline for
  never completing `hello`, a subsequent connection can successfully occupy that slot and proceed
  through ordinary admission.
- A message reaching exactly the 10,000-message session bound is itself accepted; a later message --
  including one already in flight when the bound was reached, before the requested close takes
  effect -- is neither recorded nor dispatched, regardless of whether it is a fresh or a previously
  seen `messageId`.
- An unconditional `SessionRegistry.InvalidateAll()` landing between a connection's session
  reservation and its final admission recheck results in no `hello_ack`, no consumed one-time token,
  and no active session, for every hello authentication method.

## Non-goals

- Pairing challenge orchestration and adapter display notifications; those belong to Concept 03/04.
- Live state publication, subscriptions, revisions, queues, or state recovery; those belong to Stage 5.
- Public list/revoke/block/reset commands not present in the canonical schema.
- LAN exposure or a new host-instance identity protocol.

## Completion criteria and evidence

- Focused authentication, validation, identity, and session tests pass.
- The host produces a typed, authenticated session context that Concepts 03 and 04 can consume.
- Any required session-registry extension preserves existing Stage 2 invariants and is covered by
  focused regression tests.
- This concept's admission boundary is composition-ready: it depends on an already-loaded
  authoritative `ITrustStore` passed in through constructor injection and performs no persistence
  loading, decryption, or startup-ordering decision of its own. Proving that the production
  composition root actually loads trust persistence before admitting any client, that missing
  persistence means an empty store, and that malformed/undecryptable persistence fails closed rather
  than silently admitting a client, is a startup-ordering property of the composition root itself --
  which `Program.cs` does not yet build (Concept 04 completes the host composition root) -- and so is
  Concept 04's completion criterion, not this one's. See `DIVERGENCES.md`'s D4.
