namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the adapter in response to <see cref="IpcListenEventMessage"/>, reporting whether the
/// event key now has, or already had, an approved persistent registration.
/// </summary>
/// <param name="CorrelationId">Matches the <see cref="IpcListenEventMessage"/> this responds to.</param>
/// <param name="Accepted">Whether the event key was accepted for registration.</param>
public sealed record IpcListenEventResultMessage(ulong CorrelationId, bool Accepted) : IpcMessage(CorrelationId);
