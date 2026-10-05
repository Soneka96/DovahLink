using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable tracked-quest capture service for Host runtime tests.</summary>
public sealed class FakeTrackedQuestCaptureCoordinator : ITrackedQuestCaptureCoordinator
{
    /// <summary>Whether the Host runtime started this service.</summary>
    public bool RunAsyncCalled { get; private set; }

    /// <summary>Page captures accepted by this fake, in call order.</summary>
    public List<LiveCaptureContext> PageCaptures { get; } = [];

    /// <inheritdoc/>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        RunAsyncCalled = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void AcceptPageCapture(LiveCaptureContext context) => PageCaptures.Add(context);
}
