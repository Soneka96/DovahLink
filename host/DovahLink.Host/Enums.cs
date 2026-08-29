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

// ---- Adapter ----

/// <summary>Whether the native adapter is currently connected to the host over the private IPC channel.</summary>
public enum AdapterAvailability
{
    /// <summary>No adapter is currently connected; adapter-sourced state must be treated as unavailable, not stale.</summary>
    Unavailable,

    /// <summary>An adapter is currently connected.</summary>
    Available,
}
