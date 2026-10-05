namespace DovahLink.Host.PairingCeremony;

// ---- Ceremony host ----

/// <summary>The lifecycle state of a <see cref="PairingCeremonyHost"/>.</summary>
public enum PairingCeremonyHostState
{
    /// <summary>Created; <see cref="IPairingCeremonyHost.Start"/> has not been called.</summary>
    NotStarted,

    /// <summary>The owner thread holds an attached native host and is driving it.</summary>
    Running,

    /// <summary>The host failed closed; see <see cref="IPairingCeremonyHost.Failure"/>. It never restarts.</summary>
    Failed,

    /// <summary>The host was stopped and every native resource it owned was released.</summary>
    Stopped,
}

/// <summary>Why a <see cref="PairingCeremonyHost"/> failed closed.</summary>
public enum PairingCeremonyFailure
{
    /// <summary>Opening the native runtime, authority, host, or loopback listener failed.</summary>
    StartFailed,

    /// <summary>The pairing authority scope is already owned by another live owner of this Windows user.</summary>
    AuthorityUnavailable,

    /// <summary>The native owner loop failed closed; this host stops pairing work without reattaching.</summary>
    OwnerLoopFailed,

    /// <summary>The native library reported FATAL; no new pairing work is possible until the OS process restarts.</summary>
    ProcessFatal,

    /// <summary>The native library reported output its ABI makes impossible; pairing work stops for this process.</summary>
    ContractViolation,
}

/// <summary>The explicit result of one human comparison of the displayed SAS values.</summary>
public enum SasComparisonDecision
{
    /// <summary>The two displays were reported equal.</summary>
    Match,

    /// <summary>The two displays were reported different.</summary>
    Mismatch,

    /// <summary>The comparison was abandoned.</summary>
    Cancel,
}

/// <summary>The PEER's role in a completed ceremony, as the result reports it.</summary>
public enum CeremonyPeerRole
{
    /// <summary>The peer started the ceremony (the DovahLink client, when this Host is the Responder).</summary>
    Initiator = 1,

    /// <summary>The peer answered the ceremony.</summary>
    Responder = 2,
}

/// <summary>Where one Responder attempt stands; the owner thread advances it only on native events and explicit local decisions.</summary>
internal enum CeremonyAttemptStage
{
    /// <summary>The run exists; the Initiator's key has not arrived.</summary>
    AwaitingInitiatorKey,

    /// <summary>The Initiator's key arrived; nothing is exposed until explicit local authorization names this attempt.</summary>
    AwaitingExposureAuthorization,

    /// <summary>Authorized; exposure is being applied (retried while a frame is still waiting to be written).</summary>
    Exposing,

    /// <summary>Exposed; waiting for the native SAS presentation.</summary>
    AwaitingPresentation,

    /// <summary>The SAS was presented; nothing is approved until an explicit decision names its exact ceremony identity.</summary>
    AwaitingSasDecision,

    /// <summary>MATCH was recorded; the Bootstrap MAC is being emitted (retried while a frame is still waiting to be written).</summary>
    EmittingBootstrapMac,

    /// <summary>Everything local was done; waiting for the native local result.</summary>
    AwaitingResult,
}

// ---- Native port ----

/// <summary>The kind of one translated native drive event.</summary>
internal enum NativeCeremonyEventKind
{
    /// <summary>A peer connection was accepted.</summary>
    ConnectionAccepted,

    /// <summary>A peer connection was refused before it was accepted.</summary>
    AcceptRefused,

    /// <summary>The listening socket stopped accepting.</summary>
    ListenerDisabled,

    /// <summary>A protocol step happened on a connection.</summary>
    ConnectionStep,

    /// <summary>A connection closed.</summary>
    ConnectionClosed,
}

/// <summary>The protocol message a translated step event concerns; only the ones the Responder acts on are distinguished.</summary>
internal enum NativeProtocolEvent
{
    /// <summary>Any other protocol event, or none.</summary>
    Other,

    /// <summary>A peer's START was accepted, creating this endpoint's Responder run.</summary>
    StartAccepted,

    /// <summary>The Initiator's key arrived, so this Responder may now be authorized to expose its own.</summary>
    InitiatorKey,

    /// <summary>The peer's Bootstrap MAC was authenticated.</summary>
    BootstrapMacAuthenticated,

    /// <summary>The peer cancelled the ceremony.</summary>
    PeerCancel,
}

/// <summary>The fail-closed classification of a native operation failure.</summary>
internal enum NativeFailureKind
{
    /// <summary>The operation failed with an ordinary status; only that operation is affected.</summary>
    OperationFailed,

    /// <summary>The run the operation named has ended.</summary>
    RunEnded,

    /// <summary>A retained outbound frame must be written by a later drive before this action can run.</summary>
    WritePending,

    /// <summary>The authority scope is owned elsewhere.</summary>
    OwnershipUnavailable,

    /// <summary>The native host's owner loop failed closed.</summary>
    OwnerLoopFailed,

    /// <summary>The native library is permanently FATAL for this process.</summary>
    ProcessFatal,

    /// <summary>The native library broke its ABI contract.</summary>
    ContractViolation,
}
