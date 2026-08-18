namespace DovahLinkValidationClient.Tests;

/// <summary>
/// A per-test trust-store file path under the OS temp directory, deleted on <see cref="Dispose"/>
/// so a pairing, redaction, or restart scenario never touches the developer's real trust store and
/// never leaves a persisted trust record behind in the temp directory afterward.
/// </summary>
public sealed class IsolatedTrustStore : IDisposable
{
    /// <summary>The isolated trust-store file's path; not guaranteed to exist until the bridge writes it.</summary>
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dovahlink-trust-{Guid.NewGuid():N}.json");

    /// <summary>Builds the harness environment override isolating the trust store at <see cref="Path"/>.</summary>
    /// <returns>An environment-variable override for <see cref="HarnessProcess"/>.</returns>
    public Dictionary<string, string> Override() => BridgeScenario.TrustStoreOverride(Path);

    /// <summary>Deletes the trust-store file if the bridge created one; a locked file never fails the test.</summary>
    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (IOException)
        {
            // Cleanup is best effort; a locked file must never fail the test.
        }
    }
}
