using System.Collections.Concurrent;
using DovahLink.Host;
using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>Records every reason reported through <see cref="IPublicWebSocketTransportDiagnostics"/>, in order.</summary>
public sealed class FakePublicWebSocketTransportDiagnostics : IPublicWebSocketTransportDiagnostics
{
    /// <summary>The backing store for <see cref="Reports"/>.</summary>
    private readonly ConcurrentQueue<PublicWebSocketConnectionEndReason> reports = new();

    /// <summary>The reasons reported so far, in report order.</summary>
    public IReadOnlyCollection<PublicWebSocketConnectionEndReason> Reports => reports;

    /// <inheritdoc/>
    public void ReportAbnormalEnd(PublicWebSocketConnectionEndReason reason) => reports.Enqueue(reason);
}
