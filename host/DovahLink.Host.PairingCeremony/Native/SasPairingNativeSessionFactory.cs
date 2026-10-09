namespace DovahLink.Host.PairingCeremony.Native;

/// <summary>Opens native sessions; the seam that lets the ceremony host be tested without the native library.</summary>
internal interface ISasPairingNativeSessionFactory
{
    /// <summary>Loads the native library, registers the authority scope, and creates a native host.</summary>
    /// <param name="nativeLibraryPath">The absolute native library path.</param>
    /// <param name="authorityScope">The pairing authority scope.</param>
    /// <returns>The opened session, owned by the caller.</returns>
    /// <exception cref="PairingCeremonyNativeException">Opening failed; nothing stays open.</exception>
    ISasPairingNativeSession Open(string nativeLibraryPath, ReadOnlySpan<byte> authorityScope);
}

/// <inheritdoc cref="ISasPairingNativeSessionFactory"/>
internal sealed class SasPairingNativeSessionFactory : ISasPairingNativeSessionFactory
{
    /// <inheritdoc/>
    public ISasPairingNativeSession Open(string nativeLibraryPath, ReadOnlySpan<byte> authorityScope) =>
        SasPairingNativeSession.Open(nativeLibraryPath, authorityScope);
}
