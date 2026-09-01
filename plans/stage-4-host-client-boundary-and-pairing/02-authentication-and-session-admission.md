# 02 — Public protocol validation, authentication, and session admission

Status: pending

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
  session slot must not consume a retryable one-time token.

## Invariants

- A public validation or authorization failure cannot invoke adapter code.
- Developer-token sessions remain separate from Known Device trust and are not targeted by Block or
  Revoke solely through a matching `clientId`.
- A reconnect creates a new socket, `ConnectionId`, and `sessionId`; it never resumes the old one.
- A session cannot be transferred across sockets or reused after teardown.
- Public errors contain canonical codes without credentials, filesystem paths, or raw exceptions.
- Authentication and session decisions remain host-owned and do not depend on adapter availability.

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
- Startup tests prove trust persistence is loaded before admission, missing persistence means an
  empty store, and malformed/undecryptable persistence prevents silent client admission.
