using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A settable stand-in for <see cref="IAdapterPeerProofVerifier"/>, for tests that need deterministic proof values.</summary>
public sealed class FakeAdapterPeerProofVerifier : IAdapterPeerProofVerifier
{
    /// <inheritdoc/>
    public required byte[] ExpectedToken { get; set; }

    /// <inheritdoc/>
    public required byte[] HostProofKey { get; set; }

    /// <inheritdoc/>
    public bool Matches(ReadOnlySpan<byte> presentedToken) => presentedToken.SequenceEqual(ExpectedToken);
}
