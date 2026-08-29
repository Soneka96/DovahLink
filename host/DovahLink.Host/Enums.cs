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
