namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// The four detached byte fields of this Host's own pairing Bootstrap, as DovahLink encodes them.
/// Every field is public, non-secret data; the bytes are copied at construction and never interpreted
/// here.
/// </summary>
public sealed class CeremonyBootstrapFields
{
    /// <summary>The application identity bytes.</summary>
    private readonly byte[] applicationIdentity;

    /// <summary>The key-algorithm identifier bytes.</summary>
    private readonly byte[] keyAlgorithm;

    /// <summary>The long-term public key bytes.</summary>
    private readonly byte[] publicKey;

    /// <summary>The shared-context bytes.</summary>
    private readonly byte[] sharedContext;

    /// <summary>Copies the four Bootstrap fields.</summary>
    /// <param name="applicationIdentity">The non-empty application identity.</param>
    /// <param name="keyAlgorithm">The non-empty key-algorithm identifier.</param>
    /// <param name="publicKey">The non-empty long-term public key.</param>
    /// <param name="sharedContext">The shared context, which may be empty.</param>
    /// <exception cref="ArgumentException">A required field is empty.</exception>
    public CeremonyBootstrapFields(
        ReadOnlySpan<byte> applicationIdentity, ReadOnlySpan<byte> keyAlgorithm, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> sharedContext)
    {
        if (applicationIdentity.IsEmpty || keyAlgorithm.IsEmpty || publicKey.IsEmpty)
        {
            throw new ArgumentException("The application identity, key algorithm, and public key must not be empty.");
        }

        this.applicationIdentity = applicationIdentity.ToArray();
        this.keyAlgorithm = keyAlgorithm.ToArray();
        this.publicKey = publicKey.ToArray();
        this.sharedContext = sharedContext.ToArray();
    }

    /// <summary>The application identity bytes.</summary>
    public ReadOnlySpan<byte> ApplicationIdentity => applicationIdentity;

    /// <summary>The key-algorithm identifier bytes.</summary>
    public ReadOnlySpan<byte> KeyAlgorithm => keyAlgorithm;

    /// <summary>The long-term public key bytes.</summary>
    public ReadOnlySpan<byte> PublicKey => publicKey;

    /// <summary>The shared-context bytes.</summary>
    public ReadOnlySpan<byte> SharedContext => sharedContext;
}
