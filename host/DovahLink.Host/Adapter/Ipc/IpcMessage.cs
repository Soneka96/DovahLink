namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Base type for a decoded or to-be-encoded private host-to-adapter IPC message envelope value.
/// Every concrete message is an owned plain value; none may retain a Skyrim/CommonLib pointer,
/// borrowed buffer, or public-protocol object.
/// </summary>
/// <param name="CorrelationId">
/// Pairs a request with its response. Zero means the message is unsolicited and expects no reply.
/// </param>
public abstract record IpcMessage(ulong CorrelationId);
