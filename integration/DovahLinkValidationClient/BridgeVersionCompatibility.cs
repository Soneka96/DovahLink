namespace DovahLinkValidationClient;

/// <summary>
/// The outcome of comparing a Bridge's reported release version against this client's declared
/// supported range, per <c>ai/context/protocol/compatibility.md</c>'s compatibility bootstrap.
/// </summary>
public enum BridgeVersionCompatibilityResult
{
    /// <summary>The Bridge version falls within the supported range.</summary>
    Supported,

    /// <summary>The Bridge version is older than the minimum supported version.</summary>
    BridgeOlderThanSupported,

    /// <summary>The Bridge version is newer than the maximum supported version.</summary>
    BridgeNewerThanSupported,

    /// <summary>The reported version is missing or not a valid version string.</summary>
    Unparseable,
}

/// <summary>
/// Declares this validation client's supported Bridge-release range and evaluates a connected
/// Bridge's reported version against it, per <c>ai/context/protocol/compatibility.md</c>: "every
/// SDK release declares an explicit supported Bridge-version range... a client that finds the
/// exposed version outside its supported range fails explicitly on its own side."
/// </summary>
public static class BridgeVersionCompatibility
{
    /// <summary>
    /// The oldest Bridge release this client understands. Currently equal to
    /// <see cref="MaximumSupportedVersion"/>: this project has not yet built multi-release
    /// compatibility, so the declared range is a single supported release rather than an inferred
    /// or speculative span (see <c>bridge/README.md</c>: "Bridge version supports exactly
    /// one runtime").
    /// </summary>
    public static readonly Version MinimumSupportedVersion = new(0, 3, 3);

    /// <summary>The newest Bridge release this client understands.</summary>
    public static readonly Version MaximumSupportedVersion = new(0, 3, 3);

    /// <summary>
    /// Evaluates a Bridge-reported version string against the declared supported range.
    /// </summary>
    /// <param name="bridgeVersion">The value of <c>hello_ack.bridgeVersion</c>.</param>
    /// <returns>The comparison outcome.</returns>
    public static BridgeVersionCompatibilityResult Evaluate(string? bridgeVersion)
    {
        if (string.IsNullOrEmpty(bridgeVersion) || !Version.TryParse(bridgeVersion, out Version? parsed))
        {
            return BridgeVersionCompatibilityResult.Unparseable;
        }
        if (parsed < MinimumSupportedVersion)
        {
            return BridgeVersionCompatibilityResult.BridgeOlderThanSupported;
        }
        if (parsed > MaximumSupportedVersion)
        {
            return BridgeVersionCompatibilityResult.BridgeNewerThanSupported;
        }
        return BridgeVersionCompatibilityResult.Supported;
    }
}
