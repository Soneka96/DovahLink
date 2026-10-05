using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>Records page-response handoffs and returns no page for unused read requests.</summary>
public sealed class FakeTrackedQuestPageReader : ITrackedQuestPageReader
{
    /// <summary>Page captures accepted by this fake, in call order.</summary>
    public List<LiveCaptureContext> PageCaptures { get; } = [];

    /// <summary>Page requests received by this fake, in call order.</summary>
    public List<(AdapterCaptureSource Source, PlayContextSnapshot PlayContext, TrackedQuestPageKind Kind, uint QuestId, ushort Cursor)> ReadPageCalls { get; } = [];

    /// <summary>Optional scripted response for each page request.</summary>
    public Func<AdapterCaptureSource, PlayContextSnapshot, TrackedQuestPageKind, uint, ushort, CancellationToken, Task<LiveCaptureContext?>>? ReadPageOverride { get; set; }

    /// <inheritdoc/>
    public Task<LiveCaptureContext?> ReadPageAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        TrackedQuestPageKind kind,
        uint questId,
        ushort cursor,
        CancellationToken cancellationToken)
    {
        ReadPageCalls.Add((source, playContext, kind, questId, cursor));
        return ReadPageOverride?.Invoke(source, playContext, kind, questId, cursor, cancellationToken)
            ?? Task.FromResult<LiveCaptureContext?>(null);
    }

    /// <inheritdoc/>
    public void AcceptPageCapture(LiveCaptureContext context) => PageCaptures.Add(context);
}
