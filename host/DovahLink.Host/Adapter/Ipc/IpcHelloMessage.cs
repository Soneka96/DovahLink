using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the connecting adapter to negotiate the private channel: the requested protocol version
/// (carried in the frame header, not this payload), the adapter's own instance identity, and a
/// bounded peer-ownership proof token. The proof's expected value and comparison policy belong to
/// the host channel that consumes this message, not to this wire contract.
/// </summary>
/// <param name="CorrelationId">Pairs this request with its <see cref="IpcHelloAckMessage"/> response.</param>
/// <param name="AdapterInstanceId">The connecting adapter's instance identity.</param>
/// <param name="PeerProofToken">
/// The bounded peer-ownership proof, at most <see cref="Constants.MaxIpcPeerProofTokenBytes"/> bytes.
/// </param>
public sealed record IpcHelloMessage(ulong CorrelationId, AdapterInstanceId AdapterInstanceId, byte[] PeerProofToken)
    : IpcMessage(CorrelationId);
