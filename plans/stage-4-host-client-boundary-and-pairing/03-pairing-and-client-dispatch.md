# 03 — Pairing and client message dispatch

Status: complete

Covers: R2, R4, R5, R6

Depends on: `02-authentication-and-session-admission.md`; existing `PairingCoordinator`,
`TrustStore`, `TrustAdminService`, `TrustResetService`, and `SessionRegistry`.

## Owner and boundary

The host owns the authenticated application dispatcher for the currently canonical client message
set. It maps pairing, liveness, and rename requests to existing host services and turns their typed
outcomes into canonical responses. This is a stable concept because it is the sole boundary where
client intent becomes host pairing/trust/session behavior.

## Inputs and outputs

Inputs are admitted session contexts and validated typed messages. Outputs are canonical pairing,
rename, heartbeat, capability, error, and invalidation messages plus narrow adapter-notification
requests for Concept 04.

## Contracts

- Dispatch only messages allowed by the session's authentication/trust tier.
- Map `pairing_request` to the host pairing coordinator and report unavailable, available,
  in-progress, or other-device status without disclosing another client's code. `available` is
  permitted only when the adapter-facing display operation has been accepted; code generation alone
  is not availability.
- Provide a coherent host pairing-status snapshot for the requesting `clientId`: own active challenge
  means `in_progress` with rounded-up remaining code lifetime; own pending credential means
  `in_progress` with `expiresInSeconds: null`; another owner's challenge/pending credential means
  `other_device_pairing` with the expiry field omitted; no displayable challenge means
  `unavailable`. Do not infer pending-versus-active from a nullable challenge field alone.
- Make challenge creation and first display one coherent pairing decision. A caller-level
  availability check followed by an independent `BeginPairing` is prohibited because the adapter can
  disappear between the two operations. The coordinator or an injected pairing-notification port
  must either accept the display as part of the operation or roll back the newly created challenge
  before any client receives `available`.
- The initial-display decision must have a bounded private-channel acknowledgement or an equivalent
  adapter-owned readiness contract; enqueueing an unobserved request is not proof that Skyrim can
  present it. A timeout or negative acknowledgement returns `unavailable` and leaves no advertised
  challenge.
- Map `pairing_confirm` and `pairing_ack` to the recoverable credential handshake. Never treat a
  pending credential as ordinary trusted-session authentication.
- Map malformed pairing payloads to correlated `malformed_message` errors; invalid code, expiry,
  pacing, hard-limit, pending-not-found, and pairing-invalidated cases remain their exact
  `pairing_outcome` values. Persistence or secure-generation failures use redacted retry-safe
  `internal_error` behavior while preserving retryable pending state where the domain contract says
  it remains available.
- Preserve exact pairing outcomes, pacing/retry values, expiry, wrong-attempt limits, pending
  invalidation, idempotent confirmation, and reconnect-grace semantics.
- Preserve the adapter-facing side effects from the established pairing behavior: initial display,
  manual redisplay, wrong-code automatic redisplay when its independent cooldown permits, and the
  no-code attempts-exhausted notification when the hard wrong-attempt limit is reached.
- When `PairingConfirmationResult.ShouldAutoRenotify` is true, obtain the still-active code through
  an explicit host pairing contract and issue the adapter notification without putting that code in
  the public outcome. Automatic redisplay is best effort and does not change the code-confirmation
  result if the adapter is unavailable.
- Map `pairing_renotify` and `pairing_cancel` to the owning client's operation only. Redisplay must
  not consume a cooldown or report success unless the adapter notification is accepted.
- If an active challenge cannot be redisplayed because the adapter is unavailable, preserve the
  challenge and cooldown state and return a retryable canonical `internal_error`; do not claim
  `renotified`, invent a new pairing outcome, or silently discard the challenge.
- Map `rename_request` to `TrustAdminService` for a Full/trusted session, preserving display-name
  bounds and clear-name behavior.
- Map a successful rename to `renamed`, invalid presentation data to `invalid_display_name`, and
  persistence/internal failure to a safe correlated error; never expose trust-store exceptions.
- Map `ping` to `pong` and keep capabilities/bootstrap behavior presentation-independent.
- Map full-session `subscribe`/`snapshot_request` to canonical unsupported-state behavior without
  creating state-area, queue, snapshot, or recovery state; restricted sessions reject them before
  application handling.
- Ensure administrative trust changes preserve the authoritative mutation → best-effort
  `session_invalidated` → force-close ordering and identify the reason exactly: `revoked` for
  Revoke, `blocked` for Block, `trust_reset` for Reset Trust, and `factory_reset` for Factory Reset.
  Unblock, Forget, and rename do not invalidate sessions.
- Extend the session invalidation seam so trust services receive transport-owned invalidation
  targets without owning WebSocket objects. The invalidation operation must preserve the target's
  authentication source, exclude developer-token sessions from client-scoped Revoke/Block/Reset
  Trust, and still target every session for Factory Reset.
- Keep `TrustAdminService`, `TrustResetService`, `PairingCoordinator`, and `SessionRegistry` free of
  WebSocket handles and transport implementation types. A host-owned invalidation coordinator may
  receive immutable session target snapshots and invoke transport callbacks, but trust/domain code
  must remain usable with the existing test doubles.
- Never hold the pairing-operation lock while awaiting a private display acknowledgement or any
  persistence/transport task. Reserve coherent state first, await outside the lock, then commit or
  roll back through the coordinator's serialized operation path.
- Treat connection loss as a pairing disconnect notification exactly once, and a reconnect as a
  reconnect notification for the same `clientId` only when a fresh socket is admitted.

## Invariants

- A client can operate only on its own `clientId`-bound pairing state.
- Only one pairing challenge or pending credential is active globally.
- A successful `pairing_ack` upgrades the existing session once; it does not mint a new session.
- Trust mutations never expose credentials or codes and never let a stale session continue operating.
- Pairing challenge creation and adapter display form one host-owned availability decision: a failed
  or unavailable display leaves no falsely advertised active challenge.
- The same atomic display rule applies to `pairing_renotify`: cooldown state and success are not
  committed unless the adapter accepted the redisplay request.
- Once an initial display has been accepted, later adapter loss does not invalidate the already
  visible challenge; code confirmation remains host-owned, while future redisplay reports the
  controlled retryable failure above.
- Errors are canonical and do not leak persistence or adapter failures.
- No client request directly invokes Skyrim code; adapter forwarding happens through an injected,
  narrow host-owned seam.

## Allowed files/modules

- New files only under `host/DovahLink.Host/Client/Dispatch/`.
- `host/DovahLink.Host/Pairing/`, `Trust/`, and `Sessions/` only for boundary integration and
  lifecycle callbacks required by the existing service contracts.
- New tests only under `host/DovahLink.Host.Tests/Client/Dispatch/`, with focused regression tests
  in existing `Pairing/`, `Trust/`, and `Sessions/` directories.
- No live state publisher implementation or `bridge/` files.

## Proof obligations

Expected focused test files:

- `host/DovahLink.Host.Tests/Client/Dispatch/ClientMessageDispatcherTests.cs`
- `host/DovahLink.Host.Tests/Pairing/PairingCoordinatorTests.cs` for pairing-domain regression
  coverage
- `host/DovahLink.Host.Tests/Trust/TrustAdminServiceTests.cs` and
  `host/DovahLink.Host.Tests/Trust/TrustResetServiceTests.cs` for reason/order regressions

- Every canonical pairing message reaches the correct host service and returns the exact permitted
  outcome/payload shape.
- Restricted/full authorization is enforced for every dispatched message.
- Pairing code generation/display is not repeated for a second client or repeated request, and a
  notification failure cannot leave an active challenge that the client was told is available.
- Pairing-status and outcome payloads preserve the schema's required `null` versus omitted fields,
  correlation IDs, retry-after rounding, and credential/display-name presence rules exactly.
- Pairing reconnect, expiry, wrong-code pacing, hard limit, persistence failure, cancellation, and
  administrative invalidation remain deterministic.
- Revoke, Block, Reset Trust, and Factory Reset produce the correct session-targeting behavior,
  including developer-token exemption and unconditional Factory Reset invalidation.
- `subscribe`/`snapshot_request` cannot allocate Stage 5 state/publication structures.
- Trust-service invalidation tests prove the exact reason matrix and prove that client-scoped Revoke,
  Block, and Reset Trust target only trusted-device sessions, while Factory Reset targets every
  authentication source.
- Authorization is rechecked after a trust mutation and an invalidation target is marked closing
  before its best-effort event is sent, so a concurrent request cannot use the short notification/
  close interval to continue as trusted.
- Connection loss and rapid reconnect do not transfer or reuse a prior session.

## Non-goals

- Implementing the WebSocket transport or private IPC wire mechanics.
- Adding public trust-administration messages absent from the canonical schema.
- Publishing subscriptions, snapshots, events, or real Skyrim state.

## Completion criteria and evidence

- Focused dispatcher and pairing/trust integration tests pass.
- Existing domain tests remain green without weakening their atomicity or lifecycle guarantees.
- Concept 04 receives explicit typed notification intents and invalidation callbacks rather than
  transport or adapter implementation details.
