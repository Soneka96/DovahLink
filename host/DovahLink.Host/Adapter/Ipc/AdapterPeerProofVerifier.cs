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

    /// <summary>
    /// This host process's own HMAC key for computing <see cref="IpcHelloAckMessage.HostProof"/>,
    /// independent of <see cref="ExpectedToken"/>. Never transmitted or written to the rendezvous
    /// wire format's bearer-credential field -- only the adapter-side field carrying the matching
    /// value for local HMAC use reaches it -- so observing a Hello's presented token alone can never
    /// yield this key.
    /// </summary>
    byte[] HostProofKey { get; }

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

    /// <summary>The owned HostProof HMAC key generated for this host process's lifetime.</summary>
    private readonly byte[] hostProofKey;

    /// <summary>
    /// Creates a verifier and generates fresh, independent random values for this host process's
    /// lifetime: the peer-proof bearer token and the HostProof HMAC key.
    /// </summary>
    public AdapterPeerProofVerifier()
    {
        expectedToken = RandomNumberGenerator.GetBytes(Constants.MaxIpcPeerProofTokenBytes);
        hostProofKey = RandomNumberGenerator.GetBytes(Constants.MaxIpcPeerProofTokenBytes);
    }

    /// <inheritdoc/>
    public byte[] ExpectedToken => expectedToken.ToArray();

    /// <inheritdoc/>
    public byte[] HostProofKey => hostProofKey.ToArray();

    /// <inheritdoc/>
    public bool Matches(ReadOnlySpan<byte> presentedToken) =>
        CryptographicOperations.FixedTimeEquals(expectedToken, presentedToken);
}
