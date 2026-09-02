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
