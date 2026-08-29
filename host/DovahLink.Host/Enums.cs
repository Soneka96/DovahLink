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
