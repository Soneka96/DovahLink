using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IAdapterIpcListener"/> whose <see cref="CurrentConnection"/> is settable directly.</summary>
public sealed class FakeAdapterIpcListener : IAdapterIpcListener
{
    /// <inheritdoc/>
    public int BoundPort { get; set; }

    /// <inheritdoc/>
    public IAdapterIpcConnection? CurrentConnection { get; set; }

    /// <inheritdoc/>
    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
