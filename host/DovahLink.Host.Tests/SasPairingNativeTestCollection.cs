namespace DovahLink.Host.Tests;

/// <summary>
/// Groups every test class that loads the real sas-pairing native library through the public
/// SasPairing package. The native library allows one live runtime per process, so these classes
/// never run in parallel with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SasPairingNativeTestCollection
{
    /// <summary>The collection name every member test class references via <see cref="Xunit.CollectionAttribute"/>.</summary>
    public const string Name = "SasPairingNativeTests";
}
