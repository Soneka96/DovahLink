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

    /// <summary>The number of hex characters in a generated factory-reset confirmation code.</summary>
    public const int FactoryResetChallengeCodeLength = 8;

    /// <summary>How long a factory-reset challenge remains confirmable after it is issued.</summary>
    public static readonly TimeSpan FactoryResetChallengeLifetime = TimeSpan.FromMinutes(5);

    // ---- Pairing ----

    /// <summary>The number of digits in a generated pairing challenge code.</summary>
    public const int PairingChallengeCodeDigits = 6;

    /// <summary>How long a pairing challenge remains confirmable after it is issued.</summary>
    public static readonly TimeSpan PairingChallengeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The number of hex characters in a newly issued pairing credential.</summary>
    public const int PairingCredentialLength = 32;

    /// <summary>The number of hex characters in a newly paired device's short id.</summary>
    public const int PairingShortIdLength = 4;

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
}
