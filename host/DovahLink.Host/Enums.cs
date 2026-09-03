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

/// <summary>The host's pairing state machine, per <c>ai/context/host/migration-audit.md</c>'s "Pairing state machine".</summary>
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

    /// <summary>The requesting client owns no active challenge.</summary>
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
