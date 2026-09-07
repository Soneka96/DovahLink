namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the host in response to <see cref="IpcTrustAdminRequestMessage"/>, carrying the exact
/// formatted text a Skyrim-facing console surface displays verbatim. The adapter performs no
/// interpretation or additional formatting of its own; the host's trust-administration service
/// already redacts credentials and persistence exceptions before this text is built.
/// </summary>
/// <param name="CorrelationId">Matches the <see cref="IpcTrustAdminRequestMessage"/> this responds to.</param>
/// <param name="ResultText">The bounded, display-ready result text.</param>
public sealed record IpcTrustAdminResultMessage(ulong CorrelationId, string ResultText) : IpcMessage(CorrelationId);
