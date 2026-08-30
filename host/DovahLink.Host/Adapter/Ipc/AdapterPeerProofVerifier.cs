using System.Security.Cryptography;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Verifies the peer-ownership proof carried by <see cref="IpcHelloMessage"/>. Host and adapter are
/// shipped as one atomic package with no negotiated protocol version, so this shared value -- not a
/// wire version -- is what proves a connecting peer belongs to the matching packaged pair, per
/// <c>ai/context/host/architecture.md</c>'s "Framing and package ownership".
/// </summary>
public interface IAdapterPeerProofVerifier
{
    /// <summary>
    /// The proof value for this host process's lifetime. A later concept's process-launch code reads
    /// this to pass the same value to the adapter it starts.
    /// </summary>
    byte[] ExpectedToken { get; }

    /// <summary>Checks a presented token against <see cref="ExpectedToken"/> in constant time.</summary>
    /// <param name="presentedToken">The peer-proof token presented in a connecting adapter's Hello.</param>
    /// <returns><see langword="true"/> when the presented token exactly matches the expected value.</returns>
    bool Matches(ReadOnlySpan<byte> presentedToken);
}

/// <inheritdoc cref="IAdapterPeerProofVerifier"/>
public sealed class AdapterPeerProofVerifier : IAdapterPeerProofVerifier
{
    /// <summary>The owned proof value generated for this host process's lifetime.</summary>
    private readonly byte[] expectedToken;

    /// <summary>Creates a verifier and generates a fresh random proof value for this host process's lifetime.</summary>
    public AdapterPeerProofVerifier()
    {
        expectedToken = RandomNumberGenerator.GetBytes(Constants.MaxIpcPeerProofTokenBytes);
    }

    /// <inheritdoc/>
    public byte[] ExpectedToken => expectedToken.ToArray();

    /// <inheritdoc/>
    public bool Matches(ReadOnlySpan<byte> presentedToken) =>
        CryptographicOperations.FixedTimeEquals(expectedToken, presentedToken);
}
