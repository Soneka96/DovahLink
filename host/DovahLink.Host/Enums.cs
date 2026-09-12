namespace DovahLink.Host;

// ---- Trust ----

/// <summary>The persistent trust state of a device the host has issued or previously issued a pairing credential to.</summary>
public enum KnownDeviceState
{
    /// <summary>The device has never completed pairing.</summary>
    Unpaired,

    /// <summary>The device holds a valid, currently trusted credential.</summary>
    Trusted,

    /// <summary>The device's trust was explicitly revoked by an administrative action.</summary>
    Revoked,

    /// <summary>The device is blocked from pairing or reconnecting until explicitly unblocked.</summary>
    Blocked,
}

/// <summary>The result of a known-device trust mutation.</summary>
public enum TrustMutationOutcome
{
    /// <summary>The requested state transition was persisted.</summary>
    Changed,

    /// <summary>No known device matched the requested identity.</summary>
    NotFound,

    /// <summary>The device exists but is not eligible for this operation.</summary>
    NotEligible,

    /// <summary>The device already has the requested state.</summary>
    AlreadyInState,
}

/// <summary>The outcome of requesting a new Factory Reset confirmation challenge.</summary>
public enum FactoryResetBeginOutcome
{
    /// <summary>The returned challenge became the active, confirmable one.</summary>
    Started,

    /// <summary>
    /// An in-flight <see cref="Trust.ITrustResetService.ConfirmResetAsync"/> invocation already holds
    /// an exclusive claim on the currently active challenge, so no fresh challenge could become active.
    /// </summary>
    AlreadyInProgress,
}

// ---- Sessions ----

/// <summary>The lifecycle state of one client connection's session.</summary>
public enum SessionState
{
    /// <summary>The session is live and its identifier remains usable.</summary>
    Active,

    /// <summary>The session has ended; its identifier can never become valid again.</summary>
    Invalidated,
}

/// <summary>How a session's owning connection authenticated at <c>hello</c>.</summary>
public enum SessionAuthenticationSource
{
    /// <summary>Authenticated against the process-lifetime developer/local-connection token.</summary>
    OneTimeLocalToken,

    /// <summary>Admitted with no credential presented yet, to run the pairing flow.</summary>
    Unpaired,

    /// <summary>Authenticated with a persisted pairing credential.</summary>
    TrustedDeviceCredential,
}

/// <summary>A session's current message-authorization tier.</summary>
public enum SessionTrustTier
{
    /// <summary>Restricted to the pairing/liveness allowlist until pairing succeeds on this connection.</summary>
    Restricted,

    /// <summary>Full access to every non-restricted public message.</summary>
    Full,
}

/// <summary>
/// The authoritative reason an administrative trust mutation invalidates one or more sessions.
/// Carried through to the terminal <c>session_invalidated</c> notification a later concept sends
/// before forcing the affected connection closed.
/// </summary>
public enum SessionInvalidationReason
{
    /// <summary>A trusted device's credential was explicitly revoked.</summary>
    Revoked,

    /// <summary>A known device was blocked from pairing or reconnecting.</summary>
    Blocked,

    /// <summary>Reset Trust reset every trusted device back to unpaired.</summary>
    TrustReset,

    /// <summary>Factory Reset unconditionally cleared every known device.</summary>
    FactoryReset,
}

// ---- Pairing ----

/// <summary>The host's pairing state machine, per <c>ai/context/protocol/security.md</c>'s "Persistent local trust".</summary>
public enum PairingState
{
    /// <summary>A pairing challenge has been issued and is waiting for a matching code.</summary>
    ChallengeActive,

    /// <summary>A matching code was confirmed and a credential has been issued, pending final confirmation.</summary>
    PendingCredential,

    /// <summary>Pairing completed; the device is now a trusted, known device.</summary>
    Trusted,

    /// <summary>The presented code did not match the active challenge.</summary>
    Rejected,

    /// <summary>The active challenge's code was presented after it had already expired.</summary>
    Expired,
}

// ---- Pairing outcomes ----

/// <summary>The result of attempting to begin a pairing challenge.</summary>
public enum PairingStartOutcome
{
    /// <summary>A new challenge was created for the requesting client.</summary>
    Started,

    /// <summary>The requesting client already owns an active challenge or pending credential.</summary>
    Resumed,

    /// <summary>A different client currently owns the pairing operation.</summary>
    OtherDeviceActive,

    /// <summary>Secure code generation failed.</summary>
    GeneratorFailed,

    /// <summary>The requesting device is blocked from pairing until it is unblocked.</summary>
    Blocked,
}

/// <summary>The result of evaluating a pairing code.</summary>
public enum PairingConfirmOutcome
{
    /// <summary>The credential was issued and is waiting for final client confirmation.</summary>
    CredentialIssued,

    /// <summary>The code was invalid or did not belong to the requesting client.</summary>
    Invalid,

    /// <summary>The challenge expired before the code was evaluated.</summary>
    Expired,

    /// <summary>The request arrived before the pacing interval elapsed.</summary>
    PacingLimited,

    /// <summary>The wrong-attempt hard limit cancelled the challenge.</summary>
    HardLimitReached,

    /// <summary>Secure credential generation failed.</summary>
    GeneratorFailed,

    /// <summary>
    /// A genuinely correct code was presented, but an administrative trust mutation committed after
    /// this exact challenge was created, so it is no longer authoritative and issued no credential.
    /// </summary>
    PairingInvalidated,
}

/// <summary>The result of finalizing a pending pairing credential.</summary>
public enum PairingCommitOutcome
{
    /// <summary>The credential became trusted.</summary>
    Trusted,

    /// <summary>A previously completed pairing was safely retried.</summary>
    AlreadyTrusted,

    /// <summary>No matching pending credential exists.</summary>
    PendingNotFound,

    /// <summary>An administrative trust mutation invalidated the pending credential.</summary>
    PairingInvalidated,

    /// <summary>Persistence failed and the pending credential remains retryable.</summary>
    PersistenceFailed,

    /// <summary>Secure short-id generation failed or exhausted its collision budget.</summary>
    GeneratorFailed,
}

/// <summary>The result of asking to display an active pairing code again.</summary>
public enum PairingRenotifyOutcome
{
    /// <summary>The code may be displayed again.</summary>
    Renotified,

    /// <summary>The manual redisplay cooldown is still active.</summary>
    Cooldown,

    /// <summary>The requesting client owns no active challenge, or one whose initial display has not yet committed.</summary>
    AlreadyIdle,
}

/// <summary>The result of cancelling a client's pairing operation.</summary>
public enum PairingCancelOutcome
{
    /// <summary>An owned challenge or pending credential was cancelled.</summary>
    Cancelled,

    /// <summary>The client owned no pairing operation.</summary>
    AlreadyIdle,
}

/// <summary>
/// The structurally distinct pairing states <see cref="Pairing.PairingStatusSnapshot"/> distinguishes
/// for one client, replacing any inference from a nullable challenge alone.
/// </summary>
public enum PairingStatusKind
{
    /// <summary>The client owns neither an active challenge nor a pending credential.</summary>
    Idle,

    /// <summary>The client owns a challenge reservation whose initial display has not yet committed.</summary>
    UncommittedDisplayReservation,

    /// <summary>The client owns an active challenge whose initial display has committed.</summary>
    DisplayedChallenge,

    /// <summary>The client owns a pending credential awaiting final confirmation.</summary>
    PendingCredential,

    /// <summary>A different client currently owns the active challenge or pending credential.</summary>
    OtherDeviceActive,
}

// ---- Adapter ----

/// <summary>Whether the native adapter is currently connected to the host over the private IPC channel.</summary>
public enum AdapterAvailability
{
    /// <summary>No adapter is currently connected; adapter-sourced state must be treated as unavailable, not stale.</summary>
    Unavailable,

    /// <summary>An adapter is currently connected.</summary>
    Available,
}

// ---- Adapter IPC ----

/// <summary>The kind of message carried by one private host-to-adapter IPC frame.</summary>
public enum IpcMessageKind : byte
{
    /// <summary>Sent by the connecting adapter to negotiate the channel. See <see cref="Adapter.Ipc.IpcHelloMessage"/>.</summary>
    Hello = 1,

    /// <summary>Sent by the host to conclude negotiation. See <see cref="Adapter.Ipc.IpcHelloAckMessage"/>.</summary>
    HelloAck = 2,

    /// <summary>Sent by the host to request a fresh baseline. See <see cref="Adapter.Ipc.IpcResynchronizeRequestMessage"/>.</summary>
    ResynchronizeRequest = 3,

    /// <summary>Sent by the adapter in response to a resynchronization request. See <see cref="Adapter.Ipc.IpcResynchronizeResultMessage"/>.</summary>
    ResynchronizeResult = 4,

    /// <summary>Sent by either side to announce a deterministic close. See <see cref="Adapter.Ipc.IpcCloseMessage"/>.</summary>
    Close = 5,

    /// <summary>Sent by either side to reject a decodable-but-invalid message. See <see cref="Adapter.Ipc.IpcRejectMessage"/>.</summary>
    Reject = 6,

    /// <summary>Sent by either side to cancel a previously sent request. See <see cref="Adapter.Ipc.IpcCancelMessage"/>.</summary>
    Cancel = 7,

    /// <summary>Sent by the host to ask the adapter to register one opaque Skyrim event key.</summary>
    ListenEvent = 8,

    /// <summary>Sent by the host to ask the adapter to perform one opaque sample read token.</summary>
    ReadSample = 9,

    /// <summary>Sent by the host to ask the adapter to present a pairing code. See <see cref="Adapter.Ipc.IpcPairingDisplayMessage"/>.</summary>
    PairingDisplay = 10,

    /// <summary>Sent by the adapter in response to a pairing display request. See <see cref="Adapter.Ipc.IpcPairingDisplayAckMessage"/>.</summary>
    PairingDisplayAck = 11,

    /// <summary>Sent by the host to present a no-code attempts-exhausted notification. See <see cref="Adapter.Ipc.IpcPairingAttemptsExhaustedMessage"/>.</summary>
    PairingAttemptsExhausted = 12,

    /// <summary>Sent by the adapter to forward a trust-administration command. See <see cref="Adapter.Ipc.IpcTrustAdminRequestMessage"/>.</summary>
    TrustAdminRequest = 13,

    /// <summary>Sent by the host in response to a trust-administration command. See <see cref="Adapter.Ipc.IpcTrustAdminResultMessage"/>.</summary>
    TrustAdminResult = 14,
}

/// <summary>Why a private IPC channel is being closed.</summary>
public enum IpcCloseReason : byte
{
    /// <summary>An ordinary, non-error close.</summary>
    Normal = 0,

    /// <summary>The sending process is shutting down.</summary>
    Shutdown = 1,

    /// <summary>The sender is closing because of an unrecoverable error.</summary>
    Error = 2,
}

/// <summary>Why the private IPC codec fail-closed rejected a frame it could still safely decode.</summary>
public enum IpcRejectReason : byte
{
    /// <summary>The frame's declared length is impossible or exceeds the configured limit.</summary>
    MalformedFrameLength = 0,

    /// <summary>The frame's message kind is not a recognized value.</summary>
    UnknownMessageKind = 1,

    /// <summary>A peer-ownership proof in the payload is structurally invalid.</summary>
    InvalidIdentity = 2,

    /// <summary>The payload bytes do not match the fixed or declared layout for the frame's kind.</summary>
    MalformedPayload = 3,

    /// <summary>
    /// An <see cref="Adapter.Ipc.IpcTrustAdminRequestMessage"/>'s correlation id matches one this
    /// session already has admitted and still outstanding.
    /// </summary>
    DuplicateTrustAdminCorrelationId = 4,

    /// <summary>
    /// A cancellable request's (resynchronize, listen-event, read-sample, or pairing-display)
    /// correlation id matches one this session already has admitted and still outstanding on the
    /// current connection generation.
    /// </summary>
    DuplicateCancellableCorrelationId = 5,
}

/// <summary>Why the host rejected an <see cref="Adapter.Ipc.IpcHelloMessage"/> negotiation.</summary>
public enum IpcHelloRejectReason : byte
{
    /// <summary>Negotiation was not rejected; used only when the hello was accepted.</summary>
    None = 0,

    /// <summary>The peer-ownership proof did not match the expected value.</summary>
    InvalidProof = 1,

    /// <summary>The hello payload was structurally invalid.</summary>
    Malformed = 2,

    /// <summary>The Hello's owning-Skyrim-lifetime identity did not match the value this host process was launched with.</summary>
    LifetimeMismatch = 3,
}

/// <summary>Which Skyrim-facing pairing-code display intent an <see cref="Adapter.Ipc.IpcPairingDisplayMessage"/> carries.</summary>
public enum PairingDisplayMode : byte
{
    /// <summary>The first display of a freshly generated code.</summary>
    Initial = 0,

    /// <summary>A client-requested manual redisplay of the still-active code.</summary>
    ManualRedisplay = 1,

    /// <summary>A best-effort redisplay after a wrong evaluated code, shown with incorrect-attempt presentation.</summary>
    WrongCodeRedisplay = 2,
}

/// <summary>
/// The closed set of adapter-originated trust-administration commands an
/// <see cref="Adapter.Ipc.IpcTrustAdminRequestMessage"/> may carry, per
/// <c>ai/context/protocol/security.md</c>'s "Trust administration surface".
/// </summary>
public enum TrustAdminOperation : byte
{
    /// <summary>Returns the canonical trust-administration command help. No argument.</summary>
    Help = 0,

    /// <summary>Lists known devices in the requested scope.</summary>
    List = 1,

    /// <summary>Revokes a trusted device by short id.</summary>
    Revoke = 2,

    /// <summary>Blocks a known device by short id.</summary>
    Block = 3,

    /// <summary>Unblocks a blocked device by short id.</summary>
    Unblock = 4,

    /// <summary>Forgets an eligible device by short id.</summary>
    Forget = 5,

    /// <summary>Resets every trusted device back to unpaired. No argument.</summary>
    ResetTrust = 6,

    /// <summary>Starts a Factory Reset confirmation challenge. No argument.</summary>
    Reset = 7,

    /// <summary>Confirms a Factory Reset challenge with its six-digit code.</summary>
    ConfirmReset = 8,
}

/// <summary>The device scope an <see cref="Adapter.Ipc.IpcTrustAdminRequestMessage"/>'s <see cref="TrustAdminOperation.List"/> operation requests.</summary>
public enum TrustAdminListScope : byte
{
    /// <summary>Every known device.</summary>
    All = 0,

    /// <summary>Only currently trusted devices.</summary>
    Trust = 1,

    /// <summary>Only currently blocked devices.</summary>
    Block = 2,
}

// ---- Client transport ----

/// <summary>
/// The authoritative root-cause reason one public WebSocket connection ended abnormally, reported
/// through <see cref="Client.Transport.IPublicWebSocketTransportDiagnostics"/>. Host-local
/// observability only -- never sent to the client, never a public protocol error. Not used for
/// normal lifecycle events (peer close, host shutdown, external cancellation, or a caller-requested
/// orderly close), which are not security/abnormal events and are never reported through this enum.
/// </summary>
public enum PublicWebSocketConnectionEndReason
{
    /// <summary>The WebSocket upgrade handshake did not complete within the configured deadline.</summary>
    HandshakeTimeout,

    /// <summary>The handshake request was malformed, missing required headers, or never produced a parseable request within the configured byte bound.</summary>
    InvalidHandshake,

    /// <summary>An established connection received a structurally invalid WebSocket frame.</summary>
    InvalidFraming,

    /// <summary>An established connection sent a binary message, which this transport does not support.</summary>
    UnsupportedBinaryMessage,

    /// <summary>An inbound message exceeded the configured maximum message size.</summary>
    MessageTooLarge,

    /// <summary>The connection exceeded the configured completed-message inbound rate limit.</summary>
    InboundRateLimitExceeded,

    /// <summary>An incomplete fragmented message was not completed within the configured assembly deadline.</summary>
    FragmentAssemblyTimeout,

    /// <summary>The connection missed a WebSocket-level keep-alive pong reply and was treated as unresponsive.</summary>
    KeepAliveTimeout,

    /// <summary>An outbound message could not be admitted onto the bounded outbound queue.</summary>
    OutboundCapacityExceeded,

    /// <summary>Sending a queued outbound frame to the peer failed.</summary>
    WriteFailure,

    /// <summary>The handshake request carried a browser <c>Origin</c> header, which this endpoint intentionally does not accept.</summary>
    DisallowedOrigin,

    /// <summary>The handshake request carried a <c>Sec-WebSocket-Version</c> value this transport does not support.</summary>
    UnsupportedWebSocketVersion,
}

/// <summary>
/// A narrow, transport-independent classification of why an established public WebSocket connection
/// ended, reported to <see cref="Client.Transport.IPublicWebSocketMessageHandler.HandleConnectionEnded"/>
/// so a consumer (for example the pairing reconnect-grace decision) can distinguish ordinary
/// connectivity loss from a deliberate security/protocol-driven termination without ever seeing a raw
/// <see cref="PublicWebSocketConnectionEndReason"/>, a WebSocket close status, or any other transport
/// implementation detail.
/// </summary>
public enum PublicConnectionTerminationKind
{
    /// <summary>
    /// Ordinary connectivity loss or an orderly/expected close -- a normal peer close, network loss,
    /// idle/keep-alive timeout, or a send failure indicating the peer is simply gone. Pairing reconnect
    /// grace remains available.
    /// </summary>
    ConnectivityLoss,

    /// <summary>
    /// A deliberate security or protocol enforcement action -- invalid framing, an unsupported binary
    /// message, an oversized message, an inbound rate-limit or outbound-capacity violation, fragment
    /// assembly abuse, or an equivalent application-level protocol-violation or message-bound close.
    /// Pairing must end outright rather than preserve reconnect grace.
    /// </summary>
    SecurityEnforcement,
}

/// <summary>
/// Why <see cref="Client.Transport.PublicWebSocketHandshake.TryParseUpgradeRequest"/> rejected an
/// upgrade request, or that it did not.
/// </summary>
public enum HandshakeRejectReason
{
    /// <summary>The request was not rejected; used only when the handshake was accepted.</summary>
    None,

    /// <summary>The request was malformed, missing a required header, or otherwise not a well-formed WebSocket upgrade request.</summary>
    Malformed,

    /// <summary>
    /// The request carried an <c>Origin</c> header. The public endpoint serves native DovahLink
    /// clients only; a browser-originated request is rejected regardless of the header's value.
    /// </summary>
    DisallowedOrigin,

    /// <summary>The request carried a <c>Sec-WebSocket-Version</c> header whose value is not <c>13</c>, the only version this transport supports.</summary>
    UnsupportedVersion,
}

/// <summary>
/// The reserved-capacity lane one outbound message is admitted onto, per
/// <c>ai/context/protocol/security.md</c>'s bounded outbound queue policy: a small slice reserved for
/// connection-level control and recovery traffic, kept separate from bulk data-publication traffic so
/// a slow client under data-publication pressure cannot delay or crowd out timely control-message
/// delivery.
/// </summary>
public enum PublicOutboundLane
{
    /// <summary>
    /// Connection-level control and recovery traffic -- for example pairing, rename, error, and
    /// session-invalidation messages, and current-state resynchronization responses. Always drained
    /// ahead of <see cref="Data"/> traffic.
    /// </summary>
    ControlOrRecovery,

    /// <summary>
    /// Bulk state-publication traffic. Passed to <see cref="Client.Transport.IPublicWebSocketConnection.TrySend"/>
    /// only for an Event message; a Snapshot message instead goes through
    /// <see cref="Client.Transport.IPublicWebSocketConnection.TrySendSnapshot"/>, which admits it
    /// onto this same lane's reserved capacity but with keyed-replaceable rather than plain FIFO
    /// semantics.
    /// </summary>
    Data,
}

// ---- Client protocol ----

/// <summary>
/// The canonical closed vocabulary of public protocol <c>messageType</c> values, per
/// <c>protocol/schema/README.md</c>'s "Message types". An unrecognized wire value is malformed
/// protocol input, never interpreted as a forward-compatible value.
/// </summary>
public enum PublicMessageType
{
    /// <summary>Client → host, first frame only.</summary>
    Hello,

    /// <summary>Host → client only.</summary>
    HelloAck,

    /// <summary>Client → host, restricted-session only.</summary>
    PairingRequest,

    /// <summary>Host → client, reply to <see cref="PairingRequest"/>.</summary>
    PairingStatus,

    /// <summary>Client → host, restricted-session only.</summary>
    PairingConfirm,

    /// <summary>Client → host, restricted-session only.</summary>
    PairingAck,

    /// <summary>Client → host, restricted-session only.</summary>
    PairingRenotify,

    /// <summary>Client → host, restricted-session only.</summary>
    PairingCancel,

    /// <summary>Host → client, reply to <see cref="PairingConfirm"/> or <see cref="PairingAck"/>.</summary>
    PairingOutcome,

    /// <summary>Client → host, full-session only.</summary>
    RenameRequest,

    /// <summary>Host → client, reply to <see cref="RenameRequest"/>.</summary>
    RenameOutcome,

    /// <summary>Sent by both endpoints after <see cref="HelloAck"/>.</summary>
    Capabilities,

    /// <summary>Client → host, full-session only.</summary>
    Subscribe,

    /// <summary>Host → client, reply to <see cref="Subscribe"/>.</summary>
    SubscriptionAck,

    /// <summary>Client → host, full-session only.</summary>
    SnapshotRequest,

    /// <summary>Host → client only.</summary>
    StateSnapshot,

    /// <summary>Host → client only.</summary>
    StateEvent,

    /// <summary>Host → client only.</summary>
    Error,

    /// <summary>Host → client only, unsolicited terminal event.</summary>
    SessionInvalidated,

    /// <summary>Client liveness request.</summary>
    Ping,

    /// <summary>Host reply to <see cref="Ping"/>.</summary>
    Pong,
}

/// <summary>The authentication method a client presents in <c>hello.auth.method</c>.</summary>
public enum HelloAuthMethod
{
    /// <summary>Developer/loopback-proof authentication against the process-lifetime one-time token.</summary>
    OneTimeLocalToken,

    /// <summary>No credential presented yet; admits a session restricted to the pairing/liveness allowlist.</summary>
    Unpaired,

    /// <summary>A persisted pairing credential, for an ordinary reconnect.</summary>
    TrustedDeviceCredential,
}

/// <summary>The session-identity kind exposed in <c>hello_ack.clientIdentityKind</c>.</summary>
public enum ClientIdentityKind
{
    /// <summary>A developer-authenticated or bootstrap-unpaired session, trust-restricted until pairing succeeds.</summary>
    Unpaired,

    /// <summary>A session admitted via, or upgraded to, a trusted persisted credential.</summary>
    Paired,
}

/// <summary>The canonical machine-readable codes an <c>error</c> message's <c>code</c> field may carry.</summary>
public enum PublicProtocolErrorCode
{
    /// <summary>The message failed structural, bound, or allowlist validation before interpretation.</summary>
    MalformedMessage,

    /// <summary>An inbound frame exceeded the maximum frame size before it could be safely decoded.</summary>
    FrameTooLarge,

    /// <summary>A requested capability, state area, or feature is not currently supported.</summary>
    UnsupportedCapability,

    /// <summary>Authentication failed for a reason that does not disclose which secret check failed.</summary>
    Unauthenticated,

    /// <summary>The session is not authorized to send this message.</summary>
    Unauthorized,

    /// <summary>A <c>trusted_device_credential</c> hello was rejected because the presented <c>clientId</c> was explicitly revoked.</summary>
    Revoked,

    /// <summary>An <c>unpaired</c> or <c>trusted_device_credential</c> hello was rejected because the presented <c>clientId</c> is a currently blocked Known Device.</summary>
    Blocked,

    /// <summary>A message carried a <c>messageId</c> already seen on this session.</summary>
    ReplayedMessage,

    /// <summary>A message carried a stale or foreign <c>sessionId</c>.</summary>
    StaleSession,

    /// <summary>The sender exceeded a rate or attempt limit.</summary>
    RateLimited,

    /// <summary>An unexpected internal failure occurred; no further detail is disclosed.</summary>
    InternalError,
}

/// <summary>
/// The wire values of <c>pairing_status.state</c>, per <c>protocol/schema/README.md</c>'s
/// "<c>pairing_status</c>" section.
/// </summary>
public enum PairingStatusWireState
{
    /// <summary>No displayable challenge exists for the requesting client.</summary>
    Unavailable,

    /// <summary>A fresh code was just generated and its adapter display was accepted.</summary>
    Available,

    /// <summary>The requesting client already owns an active challenge or pending credential.</summary>
    InProgress,

    /// <summary>A different client currently owns the active challenge or pending credential.</summary>
    OtherDevicePairing,
}

/// <summary>
/// The wire values of <c>pairing_outcome.outcome</c>, per <c>protocol/schema/README.md</c>'s
/// "<c>pairing_outcome</c>" section.
/// </summary>
public enum PairingOutcomeWireValue
{
    /// <summary>Reply to <c>pairing_confirm</c>: the credential was issued and awaits final confirmation.</summary>
    CredentialIssued,

    /// <summary>Reply to <c>pairing_ack</c>: the credential became trusted.</summary>
    Trusted,

    /// <summary>Reply to <c>pairing_ack</c>: a previously completed pairing was safely retried.</summary>
    AlreadyTrusted,

    /// <summary>Reply to <c>pairing_confirm</c>: the active challenge expired before evaluation.</summary>
    Expired,

    /// <summary>Reply to <c>pairing_confirm</c>: the submitted code did not match.</summary>
    Invalid,

    /// <summary>Reply to <c>pairing_confirm</c>: the attempt arrived before the pacing interval elapsed.</summary>
    PacingLimited,

    /// <summary>Reply to <c>pairing_confirm</c>: the wrong-attempt hard limit cancelled the challenge.</summary>
    HardLimitReached,

    /// <summary>Reply to <c>pairing_ack</c>: no matching in-memory pending credential remained.</summary>
    PendingNotFound,

    /// <summary>Reply to <c>pairing_ack</c>: an administrative trust mutation invalidated the pending credential.</summary>
    PairingInvalidated,

    /// <summary>Reply to <c>pairing_renotify</c>: the code was redisplayed.</summary>
    Renotified,

    /// <summary>Reply to <c>pairing_renotify</c>: the manual redisplay cooldown is still active.</summary>
    RenotifyCooldown,

    /// <summary>Reply to <c>pairing_cancel</c>: an owned challenge or pending credential was cancelled.</summary>
    Cancelled,

    /// <summary>Reply to <c>pairing_renotify</c> or <c>pairing_cancel</c>: the requesting client owned no active pairing operation.</summary>
    AlreadyIdle,
}

/// <summary>
/// The wire values of <c>rename_outcome.outcome</c>, per <c>protocol/schema/README.md</c>'s
/// "<c>rename_outcome</c>" section.
/// </summary>
public enum RenameOutcomeWireValue
{
    /// <summary>The device's display name was updated.</summary>
    Renamed,

    /// <summary>The presented display name failed the trust store's length or control-character bound.</summary>
    InvalidDisplayName,

    /// <summary>The requesting identity is unrecognized or not currently trusted.</summary>
    NotTrusted,
}

// ---- Client subscription ----

/// <summary>
/// One connection's own per-area delivery phase within
/// <see cref="Client.Subscription.PublicStateSubscription"/>'s recovery barrier.
/// </summary>
public enum AreaDeliveryPhase
{
    /// <summary>No live baseline exists yet; every Event for this area is discarded.</summary>
    AwaitingBaseline,

    /// <summary>
    /// A baseline is being established: Events at or below the barrier revision are discarded as
    /// superseded, and Events above it are held until the baseline is admitted or the attempt is
    /// abandoned.
    /// </summary>
    Recovering,

    /// <summary>A live baseline is established; Events for this area under the same play-context generation are forwarded immediately.</summary>
    Live,
}
