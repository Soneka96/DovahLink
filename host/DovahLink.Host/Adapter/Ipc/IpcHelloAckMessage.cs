namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Sent by the host in response to <see cref="IpcHelloMessage"/> to conclude negotiation.</summary>
/// <param name="CorrelationId">Matches the <see cref="IpcHelloMessage"/> this responds to.</param>
/// <param name="Accepted">Whether the host accepted the connection.</param>
/// <param name="RejectReason">
/// The negotiation failure reason when <see cref="Accepted"/> is <see langword="false"/>; otherwise
/// <see cref="IpcHelloRejectReason.None"/>.
/// </param>
public sealed record IpcHelloAckMessage(
    ulong CorrelationId,
    bool Accepted,
    IpcHelloRejectReason RejectReason) : IpcMessage(CorrelationId);
