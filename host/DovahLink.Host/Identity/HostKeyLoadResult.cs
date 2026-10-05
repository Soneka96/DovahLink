namespace DovahLink.Host.Identity;

/// <summary>
/// The outcome of loading a Host installation's persistent identity key: either an available key's
/// public half, or one fail-closed reason with no key. Private key material never appears here.
/// </summary>
public sealed class HostKeyLoadResult
{
    /// <summary>Creates a result whose key presence always matches its status.</summary>
    /// <param name="status">The load outcome.</param>
    /// <param name="publicKey">The available key's public half, or <see langword="null"/> for a failure.</param>
    private HostKeyLoadResult(HostKeyStatus status, P256PublicKey? publicKey)
    {
        Status = status;
        PublicKey = publicKey;
    }

    /// <summary>The load outcome.</summary>
    public HostKeyStatus Status { get; }

    /// <summary>The Host key's public half when <see cref="IsAvailable"/>; otherwise <see langword="null"/>.</summary>
    public P256PublicKey? PublicKey { get; }

    /// <summary>Whether the Host key exists, is policy-valid, and matches its record.</summary>
    public bool IsAvailable => PublicKey is not null;

    /// <summary>Creates the result for a key created and recorded by this load.</summary>
    /// <param name="publicKey">The new key's public half.</param>
    /// <returns>A <see cref="HostKeyStatus.Provisioned"/> result.</returns>
    public static HostKeyLoadResult Provisioned(P256PublicKey publicKey) =>
        new(HostKeyStatus.Provisioned, publicKey ?? throw new ArgumentNullException(nameof(publicKey)));

    /// <summary>Creates the result for an existing key that matches its record.</summary>
    /// <param name="publicKey">The key's public half.</param>
    /// <returns>A <see cref="HostKeyStatus.Loaded"/> result.</returns>
    public static HostKeyLoadResult Loaded(P256PublicKey publicKey) =>
        new(HostKeyStatus.Loaded, publicKey ?? throw new ArgumentNullException(nameof(publicKey)));

    /// <summary>Creates a fail-closed result that carries no key.</summary>
    /// <param name="status">A failure status: anything other than <see cref="HostKeyStatus.Provisioned"/> or <see cref="HostKeyStatus.Loaded"/>.</param>
    /// <returns>The unavailable result.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is a success or undefined status.</exception>
    public static HostKeyLoadResult Unavailable(HostKeyStatus status) =>
        status is HostKeyStatus.Provisioned or HostKeyStatus.Loaded || !Enum.IsDefined(status)
            ? throw new ArgumentOutOfRangeException(nameof(status), status, "A failure result needs a failure status.")
            : new HostKeyLoadResult(status, null);
}
