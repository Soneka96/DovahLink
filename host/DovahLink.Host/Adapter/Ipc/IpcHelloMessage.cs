using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the connecting adapter to establish the private channel: the adapter's own instance
/// identity and a bounded peer-ownership proof token. The proof's expected value and comparison
/// policy belong to the host channel that consumes this message, not to this wire contract.
/// </summary>
/// The correlation id pairs this request with its <see cref="IpcHelloAckMessage"/> response.
public sealed record IpcHelloMessage : IpcMessage
{
    /// <summary>The connecting adapter's instance identity.</summary>
    public AdapterInstanceId AdapterInstanceId { get; }

    /// <summary>The owned bounded peer-ownership proof bytes.</summary>
    private readonly byte[] peerProofToken;

    /// <summary>A fresh copy of the bounded peer-ownership proof, safe for the caller to mutate.</summary>
    public byte[] PeerProofToken => peerProofToken.ToArray();

    /// <summary>Creates a Hello message and copies the caller's proof token into owned storage.</summary>
    /// <param name="correlationId">Pairs this request with its response.</param>
    /// <param name="adapterInstanceId">The connecting adapter's instance identity.</param>
    /// <param name="peerProofToken">The bounded peer-ownership proof to copy.</param>
    public IpcHelloMessage(ulong correlationId, AdapterInstanceId adapterInstanceId, byte[] peerProofToken)
        : base(correlationId)
    {
        ArgumentNullException.ThrowIfNull(peerProofToken);
        AdapterInstanceId = adapterInstanceId;
        this.peerProofToken = peerProofToken.ToArray();
    }
}
