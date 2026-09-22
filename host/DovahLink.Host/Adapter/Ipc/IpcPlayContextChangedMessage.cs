using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the adapter to notify the host of a new Skyrim play context (one save/load lifetime):
/// starting a new game, loading a save, or announcing the context already current after a
/// reconnect. Best effort and unsolicited: the host sends no reply.
/// </summary>
/// <param name="CorrelationId">Always zero; this notification is unsolicited and expects no reply.</param>
/// <param name="PlayContextId">The adapter-generated play-context identity.</param>
public sealed record IpcPlayContextChangedMessage(ulong CorrelationId, PlayContextId PlayContextId) : IpcMessage(CorrelationId);
