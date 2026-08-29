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
