namespace DovahLink.Host;

// ---- Trust ----

/// <summary>Small, cross-cutting constant values shared across the host, grouped by area.</summary>
public static class Constants
{
    /// <summary>The default per-Windows-user file the trust store is persisted to.</summary>
    public static readonly string TrustStoreFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DovahLink",
        "host",
        "trust-store.dat");

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
}
