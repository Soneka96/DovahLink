namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the adapter to notify the host that the current play context has ended: loading has
/// started (before the new context is established), or the player returned to the main menu. Best
/// effort and unsolicited: the host sends no reply. No new context is established until a later
/// <see cref="IpcPlayContextChangedMessage"/>.
/// </summary>
/// <param name="CorrelationId">Always zero; this notification is unsolicited and expects no reply.</param>
public sealed record IpcPlayContextEndedMessage(ulong CorrelationId) : IpcMessage(CorrelationId);
