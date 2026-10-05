namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// Everything a <see cref="PairingCeremonyHost"/> needs, as detached values from the caller's local
/// installation state: the native library's explicit absolute path, the pairing authority scope, and
/// this Host's own Bootstrap. Nothing here comes from a peer or the network.
/// </summary>
public sealed class PairingCeremonyHostOptions
{
    /// <summary>The authority scope bytes, owned by this instance.</summary>
    private readonly byte[] authorityScope;

    /// <summary>Validates and copies the options.</summary>
    /// <param name="nativeLibraryPath">The absolute path of the sas-pairing native library; nothing is ever searched for.</param>
    /// <param name="authorityScope">The non-empty pairing authority scope from stable local state.</param>
    /// <param name="localBootstrap">This Host's own Bootstrap fields.</param>
    /// <exception cref="ArgumentException">The path is not fully qualified or the scope is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="localBootstrap"/> is <see langword="null"/>.</exception>
    public PairingCeremonyHostOptions(string nativeLibraryPath, ReadOnlySpan<byte> authorityScope, CeremonyBootstrapFields localBootstrap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeLibraryPath);
        ArgumentNullException.ThrowIfNull(localBootstrap);
        if (!Path.IsPathFullyQualified(nativeLibraryPath))
        {
            throw new ArgumentException("The native library path must be absolute.", nameof(nativeLibraryPath));
        }

        if (authorityScope.IsEmpty)
        {
            throw new ArgumentException("The pairing authority scope must not be empty.", nameof(authorityScope));
        }

        NativeLibraryPath = nativeLibraryPath;
        this.authorityScope = authorityScope.ToArray();
        LocalBootstrap = localBootstrap;
    }

    /// <summary>The absolute path of the sas-pairing native library.</summary>
    public string NativeLibraryPath { get; }

    /// <summary>The pairing authority scope bytes.</summary>
    public ReadOnlySpan<byte> AuthorityScope => authorityScope;

    /// <summary>This Host's own Bootstrap fields.</summary>
    public CeremonyBootstrapFields LocalBootstrap { get; }
}
