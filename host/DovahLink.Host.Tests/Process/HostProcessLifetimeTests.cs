using DovahLink.Host.Process;

namespace DovahLink.Host.Tests.Process;

/// <summary>Tests for the headless host process lifetime.</summary>
public class HostProcessLifetimeTests
{
    /// <summary>Verifies that an already-cancelled lifetime returns without waiting.</summary>
    [Fact]
    public async Task RunAsync_PreCancelledToken_CompletesImmediately()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var lifetime = new HostProcessLifetime();

        await lifetime.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(1));
    }

    /// <summary>Verifies that cancellation ends a running lifetime cleanly.</summary>
    [Fact]
    public async Task RunAsync_CancelledWhileRunning_Completes()
    {
        using var cancellation = new CancellationTokenSource();
        var lifetime = new HostProcessLifetime();
        Task run = lifetime.RunAsync(cancellation.Token);

        Assert.False(run.IsCompleted);

        cancellation.Cancel();

        await run.WaitAsync(TimeSpan.FromSeconds(1));
    }

    /// <summary>Verifies that the executable entry-point seam maps clean lifetime completion to success.</summary>
    [Fact]
    public async Task ProgramRunAsync_CancelledLifetime_ReturnsSuccessExitCode()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        int exitCode = await global::Program.RunAsync(new HostProcessLifetime(), cancellation.Token);

        Assert.Equal(0, exitCode);
    }
}
