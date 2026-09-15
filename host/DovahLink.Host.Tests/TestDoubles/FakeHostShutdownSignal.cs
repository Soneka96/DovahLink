using DovahLink.Host.Process;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IHostShutdownSignal"/> whose signal is settable directly.</summary>
public sealed class FakeHostShutdownSignal : IHostShutdownSignal
{
    /// <summary>Completed once <see cref="Set"/> is called.</summary>
    private readonly TaskCompletionSource signalSet = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether <see cref="Dispose"/> has been called.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Sets the signal, completing any in-progress or future <see cref="WaitAsync"/> call.</summary>
    public void Set() => signalSet.TrySetResult();

    /// <inheritdoc/>
    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAny(signalSet.Task, Task.Delay(Timeout.Infinite, cancellationToken));

    /// <inheritdoc/>
    public void Dispose() => Disposed = true;
}
