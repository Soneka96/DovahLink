using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>Records page-response handoffs and returns no page for unused read requests.</summary>
public sealed class FakeTrackedQuestPageReader : ITrackedQuestPageReader
{
    /// <summary>Page captures accepted by this fake, in call order.</summary>
    public List<LiveCaptureContext> PageCaptures { get; } = [];

    /// <inheritdoc/>
    public Task<LiveCaptureContext?> ReadPageAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        TrackedQuestPageKind kind,
        uint questId,
        ushort cursor,
        CancellationToken cancellationToken) => Task.FromResult<LiveCaptureContext?>(null);

    /// <inheritdoc/>
    public void AcceptPageCapture(LiveCaptureContext context) => PageCaptures.Add(context);
}
