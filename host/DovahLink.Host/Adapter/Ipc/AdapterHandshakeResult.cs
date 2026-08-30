namespace DovahLink.Host.Adapter.Ipc;

/// <summary>The result of evaluating a connecting adapter's <see cref="IpcHelloMessage"/>.</summary>
/// <param name="Accepted">Whether the connection was accepted.</param>
/// <param name="AckMessage">The acknowledgement to send back to the peer.</param>
public sealed record AdapterHandshakeResult(bool Accepted, IpcHelloAckMessage AckMessage);
