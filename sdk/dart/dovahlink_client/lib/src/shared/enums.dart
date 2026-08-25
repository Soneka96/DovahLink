import 'package:json_annotation/json_annotation.dart';

// ---- Connection lifecycle ----

/// The connection lifecycle phase of a `DovahLinkClient`.
enum DovahLinkConnectionState {
  /// No socket is connected.
  disconnected,

  /// Establishing the socket connection.
  connecting,

  /// A socket is connected.
  connected,

  /// An administrator deliberately invalidated the authenticated session (`revoked`, `blocked`,
  /// `trustReset`, or `factoryReset`); see `DovahLinkClient.invalidationReason`. Terminal for the
  /// current session -- recovery is only ever a fresh, explicit `DovahLinkClient.connect`, never
  /// automatic.
  administrativelyInvalidated,

  /// Bounded automatic recovery is in progress after ordinary, unexpected transport loss (not a
  /// deliberate disconnect, and not an administrative invalidation -- neither of those ever enters
  /// this state). Transitions to [reauthenticating] once the transport reconnects, or resolves
  /// directly to [disconnected] if the bounded recovery window is exhausted first while still
  /// trying to reconnect.
  reconnecting,

  /// The transport reconnected during bounded automatic recovery and is being re-authenticated
  /// (`hello`) before trust is confirmed -- distinct from [connected], which means a live session
  /// is actually trusted and usable. Resolves to [connected] once `hello` admits a session, back to
  /// [reconnecting] if `hello` fails and the recovery cycle still has attempts or time remaining, or
  /// to [disconnected] once recovery gives up.
  reauthenticating,
}

/// The client's own trust standing with the bridge, established once `DovahLinkClient.hello`
/// succeeds and possibly upgraded by `DovahLinkClient.acknowledgeTrustedCredential` or
/// `DovahLinkClient.recoverPendingPairing`.
enum DovahLinkTrustState {
  /// Admitted without a trust credential; restricted to the pairing message set.
  unpaired,

  /// Fully trusted, either by a `trusted_device_credential` hello or a completed pairing.
  trusted,
}

/// The wire value of `hello.auth.method` (`protocol/schema/README.md`'s `hello`).
enum AuthMethod {
  /// No credential presented yet; admits a trust-restricted session solely to run pairing.
  @JsonValue('unpaired')
  unpaired,

  /// Explicit developer authentication via a locally configured one-time token.
  @JsonValue('one_time_local_token')
  oneTimeLocalToken,

  /// The persisted credential a completed pairing issued.
  @JsonValue('trusted_device_credential')
  trustedDeviceCredential,
}

/// Why a `trusted_device_credential` hello was rejected and `DovahLinkClient.authenticate`
/// recovered by discarding the credential and retrying as `unpaired`.
enum CredentialRejectionReason {
  /// The presented credential belonged to a clientId the bridge explicitly revoked.
  revoked,

  /// The presented credential did not match any credential the bridge currently trusts.
  unrecognized,

  /// The presented credential's clientId is a currently blocked Known Device. Unlike [revoked],
  /// recovering from this must not imply a new pairing code is being requested -- a blocked device
  /// is denied pairing until an administrator unblocks it.
  blocked;

  /// Returns the recovery reason for a rejected credential [code], or `null` when the rejection
  /// must surface to the caller.
  static CredentialRejectionReason? fromProtocolErrorCode(
    ProtocolErrorCode code,
  ) => switch (code) {
    ProtocolErrorCode.revoked => CredentialRejectionReason.revoked,
    ProtocolErrorCode.unauthenticated => CredentialRejectionReason.unrecognized,
    ProtocolErrorCode.blocked => CredentialRejectionReason.blocked,
    _ => null,
  };
}

/// The raw wire value of `hello_ack.clientIdentityKind` (`protocol/schema/README.md`'s
/// `hello_ack`), decoded directly by `HelloAckPayload`. Distinct from [DovahLinkTrustState]: this
/// is the wire vocabulary, mapped to the domain concept by `AuthenticationService`.
enum ClientIdentityKind {
  /// Admitted without a trust credential.
  @JsonValue('unpaired')
  unpaired,

  /// Admitted with a trusted device credential.
  @JsonValue('paired')
  paired,
}

// ---- Pairing ----

/// The bridge's report of pairing availability, from `DovahLinkClient.requestPairing`.
enum PairingAvailability {
  /// No challenge is currently active, and none was just started.
  @JsonValue('unavailable')
  unavailable,

  /// A fresh six-digit code was just generated and shown in Skyrim.
  @JsonValue('available')
  available,

  /// A challenge is already active; no new code was generated.
  @JsonValue('in_progress')
  inProgress,

  /// A different clientId currently owns the active challenge or pending credential.
  @JsonValue('other_device_pairing')
  otherDevicePairing,
}

/// The outcome of `DovahLinkClient.requestPairingRenotify`.
enum PairingRenotifyStatus {
  /// The active challenge's code was redisplayed in Skyrim.
  renotified,

  /// Redisplay was rejected; the renotify cooldown has not elapsed yet.
  cooldown,

  /// No challenge or pending credential is owned by this client.
  alreadyIdle;

  /// Converts a shared [PairingOutcome] into the `pairing_renotify` vocabulary, or returns `null`
  /// when the outcome belongs to another exchange.
  static PairingRenotifyStatus? fromOutcome(PairingOutcome outcome) =>
      switch (outcome) {
        PairingOutcome.renotified => PairingRenotifyStatus.renotified,
        PairingOutcome.renotifyCooldown => PairingRenotifyStatus.cooldown,
        PairingOutcome.alreadyIdle => PairingRenotifyStatus.alreadyIdle,
        _ => null,
      };
}

/// The outcome of `DovahLinkClient.cancelPairing`.
enum PairingCancelStatus {
  /// An owned active challenge or pending credential was cleared.
  cancelled,

  /// Nothing was owned; cancellation was a no-op.
  alreadyIdle;

  /// Converts a shared [PairingOutcome] into the `pairing_cancel` vocabulary, or returns `null`
  /// when the outcome belongs to another exchange.
  static PairingCancelStatus? fromOutcome(PairingOutcome outcome) =>
      switch (outcome) {
        PairingOutcome.cancelled => PairingCancelStatus.cancelled,
        PairingOutcome.alreadyIdle => PairingCancelStatus.alreadyIdle,
        _ => null,
      };
}

/// The raw wire value of `pairing_outcome.outcome` (`protocol/schema/README.md`'s
/// `pairing_outcome`), decoded directly by `PairingOutcomePayload`. Spans every outcome value
/// across `pairing_confirm`, `pairing_ack`, `pairing_renotify`, and `pairing_cancel` replies, since
/// the wire schema defines one shared closed vocabulary for all four; which subset is valid for a
/// given exchange is validated by the class that sent that request, not by this enum.
enum PairingOutcome {
  /// From `pairing_confirm`: a credential was issued.
  @JsonValue('credential_issued')
  credentialIssued,

  /// From `pairing_ack`: the session was upgraded to full trust.
  @JsonValue('trusted')
  trusted,

  /// From `pairing_ack`: the session was already fully trusted.
  @JsonValue('already_trusted')
  alreadyTrusted,

  /// From `pairing_confirm`: the pairing code had expired.
  @JsonValue('expired')
  expired,

  /// From `pairing_confirm`: the submitted code did not match.
  @JsonValue('invalid')
  invalid,

  /// From `pairing_confirm`: an attempt was made too soon after the previous one.
  @JsonValue('pacing_limited')
  pacingLimited,

  /// From `pairing_confirm`: the terminal count of wrong attempts cancelled the challenge.
  @JsonValue('hard_limit_reached')
  hardLimitReached,

  /// From `pairing_ack`: the bridge has no matching pending confirmation.
  @JsonValue('pending_not_found')
  pendingNotFound,

  /// From `pairing_renotify`: the active challenge's code was redisplayed.
  @JsonValue('renotified')
  renotified,

  /// From `pairing_renotify`: redisplay was rejected by the renotify cooldown.
  @JsonValue('renotify_cooldown')
  renotifyCooldown,

  /// From `pairing_cancel`: an owned active challenge or pending credential was cleared.
  @JsonValue('cancelled')
  cancelled,

  /// From `pairing_renotify` or `pairing_cancel`: nothing was owned.
  @JsonValue('already_idle')
  alreadyIdle,

  /// From `pairing_ack`: the pending credential survived long enough to be
  /// rejected by an administrative mutation fence.
  @JsonValue('pairing_invalidated')
  pairingInvalidated,
}

// ---- Request policy ----

/// A centralized bounded timeout category a request is classified against, per
/// `ai/context/sdk/api-design.md`'s "Request retry safety, session requirement, and timeout
/// class". Durations live in `shared/constants.dart`'s `kTimeoutClassDurations` rather than here,
/// so a call site's classification and the SDK's tuned duration policy stay independently
/// editable.
enum TimeoutClass {
  /// A fast administrative round trip (a query, an acknowledgement).
  short,

  /// The common case: a request that may involve a little more bridge-side work than [short].
  normal,

  /// Reserved for a request expected to take meaningfully longer than [normal]. Unused until a
  /// real operation actually needs it.
  heavy,
}

// ---- Trust invalidation ----

/// The typed reason `DovahLinkClient.invalidationReason` reports once
/// `DovahLinkConnectionState.administrativelyInvalidated` is reached -- the parsed form of an
/// incoming `session_invalidated.reason` (`protocol/schema/README.md`'s `session_invalidated`).
/// Never durable authoritative trust state; the Bridge remains the sole authority.
enum AdministrativeInvalidationReason {
  /// The presented device credential's `clientId` was explicitly revoked.
  @JsonValue('revoked')
  revoked,

  /// The presented `clientId` is a currently blocked Known Device.
  @JsonValue('blocked')
  blocked,

  /// The Known Device this session belonged to became Revoked (a Trusted device's trust reset).
  @JsonValue('trust_reset')
  trustReset,

  /// The Bridge's Known Device trust store was cleared. May end a developer-token session even
  /// though the configured developer token remains valid.
  @JsonValue('factory_reset')
  factoryReset,
}

// ---- Protocol envelope ----

/// The canonical wire value of an envelope's `messageType` field.
enum ProtocolMessageType {
  /// Begins connection authentication.
  @JsonValue('hello')
  hello,

  /// Acknowledges a validated [hello].
  @JsonValue('hello_ack')
  helloAck,

  /// Starts or queries a pairing challenge.
  @JsonValue('pairing_request')
  pairingRequest,

  /// Reports pairing availability.
  @JsonValue('pairing_status')
  pairingStatus,

  /// Submits a pairing code.
  @JsonValue('pairing_confirm')
  pairingConfirm,

  /// Acknowledges a persisted pairing credential.
  @JsonValue('pairing_ack')
  pairingAck,

  /// Requests redisplay of an owned pairing code.
  @JsonValue('pairing_renotify')
  pairingRenotify,

  /// Cancels an owned pairing challenge or pending credential.
  @JsonValue('pairing_cancel')
  pairingCancel,

  /// Reports a pairing operation outcome.
  @JsonValue('pairing_outcome')
  pairingOutcome,

  /// Requests a trusted device rename.
  @JsonValue('rename_request')
  renameRequest,

  /// Reports a trusted device rename outcome.
  @JsonValue('rename_outcome')
  renameOutcome,

  /// Exchanges runtime capabilities.
  @JsonValue('capabilities')
  capabilities,

  /// Requests state-area subscription.
  @JsonValue('subscribe')
  subscribe,

  /// Confirms state-area subscription results.
  @JsonValue('subscription_ack')
  subscriptionAck,

  /// Requests a current state snapshot.
  @JsonValue('snapshot_request')
  snapshotRequest,

  /// Delivers a complete state snapshot.
  @JsonValue('state_snapshot')
  stateSnapshot,

  /// Delivers an incremental state event.
  @JsonValue('state_event')
  stateEvent,

  /// Reports a structured protocol failure.
  @JsonValue('error')
  error,

  /// Notifies an authenticated client that its session was invalidated.
  @JsonValue('session_invalidated')
  sessionInvalidated,

  /// Probes WebSocket session liveness.
  @JsonValue('ping')
  ping,

  /// Replies to a [ping].
  @JsonValue('pong')
  pong,
}

/// The canonical wire value of an error payload's `code` field.
enum ProtocolErrorCode {
  /// The peer sent structurally invalid protocol data.
  @JsonValue('malformed_message')
  malformedMessage,

  /// The peer sent a frame larger than the allowed limit.
  @JsonValue('frame_too_large')
  frameTooLarge,

  /// The requested capability is unavailable.
  @JsonValue('unsupported_capability')
  unsupportedCapability,

  /// Authentication material was invalid, expired, reused, or unrecognized.
  @JsonValue('unauthenticated')
  unauthenticated,

  /// The peer is not authorized for the requested operation.
  @JsonValue('unauthorized')
  unauthorized,

  /// The presented device credential belongs to a revoked client.
  @JsonValue('revoked')
  revoked,

  /// The presented client ID belongs to a blocked device.
  @JsonValue('blocked')
  blocked,

  /// The peer reused a message ID within a session.
  @JsonValue('replayed_message')
  replayedMessage,

  /// The peer presented a stale or foreign session ID.
  @JsonValue('stale_session')
  staleSession,

  /// The peer exceeded an approved message-rate limit.
  @JsonValue('rate_limited')
  rateLimited,

  /// The bridge could not complete an operation safely.
  @JsonValue('internal_error')
  internalError,
}

/// The canonical wire value of the `hello.endpoint` field.
enum ProtocolEndpoint {
  /// Identifies the connecting side as the DovahLink client.
  @JsonValue('client')
  client,
}

// ---- Persistence ----

/// The client's local recovery standing for an in-progress pairing confirmation, persisted so a
/// crash or relaunch between issuing a credential and confirming it can resume correctly. See
/// `ai/context/protocol/security.md`'s "Persistent local trust" recoverable confirmation handshake.
enum PairingRecoveryState {
  /// No pairing confirmation is outstanding.
  none,

  /// A credential was issued and durably saved, but final confirmation (`pairing_ack`) has not yet
  /// been acknowledged by the bridge as `trusted`/`already_trusted`.
  confirming,
}

// ---- Rename ----

/// The raw wire value of `rename_outcome.outcome` (`protocol/schema/README.md`'s `rename_outcome`),
/// decoded directly by `RenameOutcomePayload`.
enum RenameOutcome {
  /// The device's display name was updated.
  @JsonValue('renamed')
  renamed,

  /// The requested display name failed the trust store's length or control-character bound.
  @JsonValue('invalid_display_name')
  invalidDisplayName,

  /// The session's identity is unrecognized or not currently trusted.
  @JsonValue('not_trusted')
  notTrusted,
}
