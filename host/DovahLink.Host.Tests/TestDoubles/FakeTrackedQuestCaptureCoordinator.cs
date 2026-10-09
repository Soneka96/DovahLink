using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable tracked-quest capture service for Host runtime tests.</summary>
public sealed class FakeTrackedQuestCaptureCoordinator : ITrackedQuestCaptureCoordinator
{
    /// <summary>Completes when <see cref="RunAsync"/> first starts.</summary>
    private readonly TaskCompletionSource runStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether the Host runtime started this service.</summary>
    public bool RunAsyncCalled { get; private set; }

    /// <summary>Completes when <see cref="RunAsync"/> first starts.</summary>
    public Task RunStarted => runStarted.Task;

    /// <inheritdoc/>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        RunAsyncCalled = true;
        runStarted.TrySetResult();
        return Task.CompletedTask;
    }
}
