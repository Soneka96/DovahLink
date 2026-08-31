namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the host in response to <see cref="IpcHelloMessage"/> to conclude negotiation.
/// <see cref="Accepted"/> being <see langword="true"/> alone never proves the responder is the
/// legitimate host -- it only proves the responder checked the adapter's presented proof -- so the
/// receiver must independently verify <see cref="HostProof"/> and never trust <see cref="Accepted"/>
/// on its own.
/// </summary>
public sealed record IpcHelloAckMessage : IpcMessage
{
    /// <summary>Whether the host accepted the connection.</summary>
    public bool Accepted { get; }

    /// <summary>
    /// The negotiation failure reason when <see cref="Accepted"/> is <see langword="false"/>; otherwise
    /// <see cref="IpcHelloRejectReason.None"/>.
    /// </summary>
    public IpcHelloRejectReason RejectReason { get; }

    /// <summary>
    /// The owned HMAC-SHA256 proof bytes: <c>HMAC-SHA256(key = peerProofToken, message = challenge ||
    /// correlationId || adapterInstanceId || ownerLifetimeId)</c>, proving the host holds the shared
    /// secret. All-zero when <see cref="Accepted"/> is <see langword="false"/> -- the host does not
    /// compute a real proof for a connection it is refusing.
    /// </summary>
    private readonly byte[] hostProof;

    /// <summary>A fresh copy of the host proof bytes, safe for the caller to mutate.</summary>
    public byte[] HostProof => hostProof.ToArray();

    /// <summary>Creates a HelloAck and copies the caller's host-proof bytes into owned storage.</summary>
    /// <param name="correlationId">Matches the <see cref="IpcHelloMessage"/> this responds to.</param>
    /// <param name="accepted">Whether the host accepted the connection.</param>
    /// <param name="rejectReason">
    /// The negotiation failure reason when <paramref name="accepted"/> is <see langword="false"/>;
    /// otherwise <see cref="IpcHelloRejectReason.None"/>.
    /// </param>
    /// <param name="hostProof">
    /// The HMAC-SHA256 proof to copy, exactly <see cref="Constants.IpcHostProofBytes"/> bytes, or
    /// <see langword="null"/> for an all-zero placeholder (never a real value for an accepted
    /// HelloAck a host actually sends; the host always supplies a real computed proof).
    /// </param>
    public IpcHelloAckMessage(ulong correlationId, bool accepted, IpcHelloRejectReason rejectReason, byte[]? hostProof = null)
        : base(correlationId)
    {
        if (hostProof is not null && hostProof.Length != Constants.IpcHostProofBytes)
        {
            throw new ArgumentException($"The host proof must be exactly {Constants.IpcHostProofBytes} bytes.", nameof(hostProof));
        }

        Accepted = accepted;
        RejectReason = rejectReason;
        this.hostProof = hostProof?.ToArray() ?? new byte[Constants.IpcHostProofBytes];
    }
}
