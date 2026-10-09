namespace DovahLink.Host.PairingCeremony;

/// <summary>Small constant values of the dormant pairing-ceremony integration, grouped by area.</summary>
internal static class Constants
{
    // ---- Ceremony host ----

    /// <summary>
    /// The pause after a drive that produced nothing, so a persistently ready socket can never turn
    /// the owner loop into a busy spin. A drive that is waiting for readiness already blocks for its
    /// own native bound, so this only matters when a drive returns at once with no events.
    /// </summary>
    public static readonly TimeSpan IdlePause = TimeSpan.FromMilliseconds(10);

    /// <summary>How long <see cref="PairingCeremonyHost.Start"/> waits for the owner thread to open and attach the native host.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long stopping waits for the owner thread, which only has to finish its current bounded
    /// drive (about 250 ms) and release the native host.
    /// </summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The bound on queued, not yet applied local decisions; a full queue refuses new ones.</summary>
    public const int DecisionQueueCapacity = 16;

    /// <summary>The backlog of the loopback pairing listener.</summary>
    public const int ListenerBacklog = 4;

    // ---- Result evidence ----

    /// <summary>The ASCII identifier of the one sas-pairing protocol profile this integration accepts.</summary>
    public static ReadOnlySpan<byte> ExpectedProfileIdentifier => "sas-pairing-vodozemac-profile-draft-01"u8;

    /// <summary>The one sas-pairing protocol profile version this integration accepts.</summary>
    public const uint ExpectedProfileVersion = 1;
}
