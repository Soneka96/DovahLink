namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Asks the Adapter for one bounded page of tracked quest facts.</summary>
/// <param name="CorrelationId">The nonzero request identity used for cancellation and response routing.</param>
/// <param name="PageKind">Which page of facts to read.</param>
/// <param name="QuestId">The runtime quest FormID, or zero for a quest-ID page.</param>
/// <param name="Cursor">The tracked-ID or objective offset.</param>
public sealed record IpcReadTrackedQuestPageMessage(
    ulong CorrelationId,
    TrackedQuestPageKind PageKind,
    uint QuestId,
    ushort Cursor) : IpcMessage(CorrelationId);
