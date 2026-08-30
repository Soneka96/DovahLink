namespace DovahLink.Host;

// ---- Trust ----

/// <summary>Small, cross-cutting constant values shared across the host, grouped by area.</summary>
public static class Constants
{
    /// <summary>The default per-Windows-user file the trust store is persisted to.</summary>
    public static string TrustStoreFilePath
    {
        get
        {
            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException("The current Windows user has no local application-data directory.");
            }

            return Path.Combine(localApplicationData, "DovahLink", "host", "trust-store.dat");
        }
    }

    /// <summary>The number of decimal digits in a generated factory-reset confirmation code.</summary>
    public const int FactoryResetChallengeCodeDigits = 6;

    /// <summary>How long a factory-reset challenge remains confirmable after it is issued.</summary>
    public static readonly TimeSpan FactoryResetChallengeLifetime = TimeSpan.FromSeconds(60);

    // ---- Pairing ----

    /// <summary>The number of digits in a generated pairing challenge code.</summary>
    public const int PairingChallengeCodeDigits = 6;

    /// <summary>How long a pairing challenge remains confirmable after it is issued.</summary>
    public static readonly TimeSpan PairingChallengeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The number of hex characters in a newly issued pairing credential.</summary>
    public const int PairingCredentialLength = 32;

    /// <summary>The number of decimal digits in a newly paired device's short id.</summary>
    public const int PairingShortIdDigits = 5;

    /// <summary>The maximum number of short-id candidates tried before pairing fails closed.</summary>
    public const int PairingMaxShortIdGenerationAttempts = 20;

    /// <summary>How long an owner may be disconnected before its pairing challenge expires.</summary>
    public static readonly TimeSpan PairingReconnectGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>The minimum interval between manual or automatic pairing-code redisplays.</summary>
    public static readonly TimeSpan PairingRenotifyCooldown = TimeSpan.FromSeconds(5);

    /// <summary>The minimum interval between evaluated pairing-code attempts.</summary>
    public static readonly TimeSpan PairingConfirmPacingInterval = TimeSpan.FromSeconds(1);

    /// <summary>The number of evaluated wrong codes allowed before the challenge is cancelled.</summary>
    public const int PairingMaxWrongAttempts = 5;

    /// <summary>How long an issued credential may remain pending final confirmation.</summary>
    public static readonly TimeSpan PairingPendingCredentialLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The maximum UTF-8 byte length of a trusted device display name.</summary>
    public const int MaxDisplayNameLengthBytes = 64;

    // ---- Authentication ----

    /// <summary>The number of hex characters in a generated one-time local connection token.</summary>
    public const int LocalConnectionTokenLength = 32;

    /// <summary>
    /// How long a one-time local connection token remains consumable after it is issued, per
    /// <c>ai/context/protocol/security.md</c>'s "Phase 1 exposure".
    /// </summary>
    public static readonly TimeSpan LocalConnectionTokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The maximum number of failed token attempts allowed within
    /// <see cref="LocalConnectionTokenFailureWindow"/>, per
    /// <c>ai/context/protocol/security.md</c>'s "Phase 1 exposure".
    /// </summary>
    public const int LocalConnectionTokenMaxFailuresPerWindow = 5;

    /// <summary>The rolling window over which failed token attempts are counted.</summary>
    public static readonly TimeSpan LocalConnectionTokenFailureWindow = TimeSpan.FromSeconds(60);

    // ---- Sessions ----

    /// <summary>The maximum number of active client sessions admitted by the first host proof.</summary>
    public const int MaxActiveSessions = 1;

    // ---- Adapter IPC ----

    /// <summary>The fixed byte length of an IPC frame header (kind and correlation id).</summary>
    public const int IpcFrameHeaderBytes = 9;

    /// <summary>
    /// The maximum total byte length (header plus payload) of one private IPC frame. Approved as a
    /// provisional value for this concept's small control-only messages; a later concept that needs
    /// to carry a larger payload over this channel may revise it with the same documented approval
    /// <c>ai/context/protocol/security.md</c>'s own limits require.
    /// </summary>
    public const int MaxIpcFrameBytes = 65536;

    /// <summary>The maximum byte length of an <see cref="Adapter.Ipc.IpcHelloMessage"/> peer-ownership proof token.</summary>
    public const int MaxIpcPeerProofTokenBytes = 64;

    /// <summary>
    /// The bounded capacity later concepts must enforce for a private IPC send/receive queue. Not
    /// itself enforced by this contract's codec.
    /// </summary>
    public const int MaxIpcQueuedMessages = 256;

    /// <summary>
    /// The maximum inbound private IPC message rate later concepts must enforce, per connected peer.
    /// Not itself enforced by this contract's codec.
    /// </summary>
    public const int MaxIpcMessagesPerSecond = 200;
}
