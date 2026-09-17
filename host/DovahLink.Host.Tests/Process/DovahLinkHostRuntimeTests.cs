using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Process;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Process;

/// <summary>Tests for <see cref="DovahLinkHostRuntime"/>.</summary>
public class DovahLinkHostRuntimeTests
{
    /// <summary>
    /// Verifies that startup publishes the rendezvous endpoint and starts both listeners before
    /// shutdown is ever requested, rather than leaving that ordering implicit.
    /// </summary>
    [Fact]
    public async Task RunAsync_BeforeShutdownRequested_PublishesRendezvousAndStartsBothListeners()
    {
        var adapterCompletion = new TaskCompletionSource();
        var publicCompletion = new TaskCompletionSource();
        var adapterListener = new FakeAdapterIpcListener { BoundPort = 111, RunAsyncCompletion = adapterCompletion.Task };
        var publicListener = new FakePublicWebSocketListener { BoundPort = 222, RunAsyncCompletion = publicCompletion.Task };
        var shutdownSignal = new FakeHostShutdownSignal();
        var rendezvousPublisher = new FakeHostRendezvousPublisher();
        var output = new SynchronizedTextCapture();
        var runtime = new DovahLinkHostRuntime(
            adapterListener, shutdownSignal, new HostProcessLifetime(), rendezvousPublisher, output,
            new FakeAdapterPeerProofVerifier { ExpectedToken = [1, 2, 3], HostProofKey = [4, 5, 6] },
            new LiveStateScheduler(adapterListener, LiveStateCatalog.Default), publicListener);
        using var shutdown = new CancellationTokenSource();

        Task<int> runTask = runtime.RunAsync(shutdown);
        await WaitUntilAsync(() => output.Snapshot().Contains("PUBLICPORT "), runTask);

        Assert.Equal([111], rendezvousPublisher.PublishedPorts);
        Assert.True(adapterListener.RunAsyncCalled);
        Assert.True(publicListener.RunAsyncCalled);
        string reported = output.Snapshot();
        Assert.Contains("PORT 111", reported);
        Assert.Contains("PROOF 010203", reported);
        Assert.Contains("HOSTPROOF 040506", reported);
        Assert.Contains("PUBLICPORT 222", reported);
        Assert.False(runTask.IsCompleted);

        // PORT, PROOF, and HOSTPROOF are always exactly the first three lines, in this exact order,
        // strictly before a present PUBLICPORT -- see DovahLinkHostRuntime.RunAsync's own doc comment
        // for why a real launched adapter's rendezvous reader depends on this.
        int portIndex = reported.IndexOf("PORT 111", StringComparison.Ordinal);
        int proofIndex = reported.IndexOf("PROOF 010203", StringComparison.Ordinal);
        int hostProofIndex = reported.IndexOf("HOSTPROOF 040506", StringComparison.Ordinal);
        int publicPortIndex = reported.IndexOf("PUBLICPORT 222", StringComparison.Ordinal);
        Assert.True(portIndex < proofIndex && proofIndex < hostProofIndex && hostProofIndex < publicPortIndex, $"Expected strict PORT < PROOF < HOSTPROOF < PUBLICPORT ordering, but got: {reported}");

        shutdown.Cancel();
        adapterCompletion.SetResult();
        publicCompletion.SetResult();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that omitting the public listener -- something only test code constructing
    /// <see cref="DovahLinkHostRuntime"/> directly can do, since the production <c>Main</c> entry
    /// point's own <see cref="global::Program.ResolvePublicListenerPort"/> always resolves a real
    /// port -- never writes a PUBLICPORT line.
    /// </summary>
    [Fact]
    public async Task RunAsync_NoPublicListener_NeverWritesPublicPortLine()
    {
        var output = new SynchronizedTextCapture();
        var noPublicListenerAdapterListener = new FakeAdapterIpcListener { BoundPort = 111 };
        var runtime = new DovahLinkHostRuntime(
            noPublicListenerAdapterListener, new FakeHostShutdownSignal(), new HostProcessLifetime(),
            new FakeHostRendezvousPublisher(), output, new FakeAdapterPeerProofVerifier { ExpectedToken = [1], HostProofKey = [2] },
            new LiveStateScheduler(noPublicListenerAdapterListener, LiveStateCatalog.Default));
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();

        await runtime.RunAsync(shutdown).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain("PUBLICPORT", output.Snapshot());
    }

    /// <summary>
    /// Verifies that shutdown teardown is ordered: the overall run does not complete until the
    /// adapter-IPC listener's own task completes, even when the public listener's task has already
    /// finished.
    /// </summary>
    [Fact]
    public async Task RunAsync_ShutdownRequested_AwaitsAdapterListenerBeforePublicListenerCompletionIsObserved()
    {
        var adapterRelease = new TaskCompletionSource();
        var adapterListener = new FakeAdapterIpcListener { RunAsyncCompletion = adapterRelease.Task };
        var publicListener = new FakePublicWebSocketListener();
        var shutdownSignal = new FakeHostShutdownSignal();
        var rendezvousPublisher = new FakeHostRendezvousPublisher();
        var output = new SynchronizedTextCapture();
        var runtime = new DovahLinkHostRuntime(
            adapterListener, shutdownSignal, new HostProcessLifetime(), rendezvousPublisher, output,
            new FakeAdapterPeerProofVerifier { ExpectedToken = [1], HostProofKey = [2] },
            new LiveStateScheduler(adapterListener, LiveStateCatalog.Default), publicListener);
        using var shutdown = new CancellationTokenSource();

        Task<int> runTask = runtime.RunAsync(shutdown);
        await WaitUntilAsync(() => publicListener.RunAsyncCalled, runTask);

        shutdown.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(200)); // let the already-completed public listener task race ahead, if it wrongly could

        Assert.False(runTask.IsCompleted, "The run must not complete while the adapter-IPC listener's own task is still pending.");

        adapterRelease.SetResult();

        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that a clean lifetime completion maps to a successful exit code.</summary>
    [Fact]
    public async Task RunAsync_LifetimeCompletes_ReturnsSuccessExitCode()
    {
        var lifetimeCompletesAdapterListener = new FakeAdapterIpcListener();
        var runtime = new DovahLinkHostRuntime(
            lifetimeCompletesAdapterListener, new FakeHostShutdownSignal(), new HostProcessLifetime(),
            new FakeHostRendezvousPublisher(), new SynchronizedTextCapture(),
            new FakeAdapterPeerProofVerifier { ExpectedToken = [1], HostProofKey = [2] },
            new LiveStateScheduler(lifetimeCompletesAdapterListener, LiveStateCatalog.Default), new FakePublicWebSocketListener());
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();

        int exitCode = await runtime.RunAsync(shutdown).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, exitCode);
    }

    /// <summary>Verifies that setting the adapter's named shutdown-request signal ends a run, exactly as an orderly Skyrim close would.</summary>
    [Fact]
    public async Task RunAsync_ShutdownSignalSet_Ends()
    {
        var shutdownSignal = new FakeHostShutdownSignal();
        var shutdownSignalSetAdapterListener = new FakeAdapterIpcListener();
        var runtime = new DovahLinkHostRuntime(
            shutdownSignalSetAdapterListener, shutdownSignal, new HostProcessLifetime(),
            new FakeHostRendezvousPublisher(), new SynchronizedTextCapture(),
            new FakeAdapterPeerProofVerifier { ExpectedToken = [1], HostProofKey = [2] },
            new LiveStateScheduler(shutdownSignalSetAdapterListener, LiveStateCatalog.Default), new FakePublicWebSocketListener());
        using var shutdown = new CancellationTokenSource();

        Task<int> runTask = runtime.RunAsync(shutdown);
        Assert.False(runTask.IsCompleted);

        shutdownSignal.Set();

        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Verifies that racing the caller's own <c>shutdown.Cancel()</c> against the adapter's named
    /// shutdown-signal firing at nearly the same instant -- whichever source wins -- never throws,
    /// never hangs, and never runs either listener's teardown more than once. Repeated across many
    /// fresh runtime instances rather than asserting one deterministic interleaving, for a real
    /// chance of exposing a timing bug instead of merely proving the race is possible.
    /// </summary>
    [Fact]
    public async Task RunAsync_CallerCancellationRacesNamedShutdownSignal_CompletesOnceWithNoDuplicateEffects()
    {
        for (int iteration = 0; iteration < 20; iteration++)
        {
            var adapterListener = new FakeAdapterIpcListener();
            var publicListener = new FakePublicWebSocketListener();
            var shutdownSignal = new FakeHostShutdownSignal();
            var runtime = new DovahLinkHostRuntime(
                adapterListener, shutdownSignal, new HostProcessLifetime(),
                new FakeHostRendezvousPublisher(), new SynchronizedTextCapture(),
                new FakeAdapterPeerProofVerifier { ExpectedToken = [1], HostProofKey = [2] },
                new LiveStateScheduler(adapterListener, LiveStateCatalog.Default), publicListener);
            using var shutdown = new CancellationTokenSource();

            Task<int> runTask = runtime.RunAsync(shutdown);

            await Task.WhenAll(Task.Run(shutdown.Cancel), Task.Run(shutdownSignal.Set));

            int exitCode = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, exitCode);
            Assert.True(adapterListener.RunAsyncCalled);
            Assert.True(publicListener.RunAsyncCalled);

            // Crossed/repeated signals after completion must never have any further effect.
            Assert.Null(Record.Exception(() => shutdown.Cancel()));
            Assert.Null(Record.Exception(shutdownSignal.Set));
        }
    }

    /// <summary>
    /// Verifies that the composed live-state scheduler actually runs as part of
    /// <see cref="DovahLinkHostRuntime.RunAsync"/> -- not merely constructed and left unused -- by
    /// observing a real sample send reach the adapter connection, and that shutdown still completes
    /// cleanly once the scheduler's own loop is cancelled alongside the others.
    /// </summary>
    [Fact]
    public async Task RunAsync_BeforeShutdownRequested_LiveStateSchedulerSendsSamplesOnTheAdapterConnection()
    {
        var adapterListener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendReadSampleResult = true };
        adapterListener.CurrentConnection = connection;
        var tinyIntervals = new Dictionary<RateClass, TimeSpan> { [RateClass.Fast] = TimeSpan.FromMilliseconds(5), [RateClass.Medium] = TimeSpan.FromMilliseconds(5) };
        var runtime = new DovahLinkHostRuntime(
            adapterListener, new FakeHostShutdownSignal(), new HostProcessLifetime(),
            new FakeHostRendezvousPublisher(), new SynchronizedTextCapture(),
            new FakeAdapterPeerProofVerifier { ExpectedToken = [1], HostProofKey = [2] },
            new LiveStateScheduler(adapterListener, LiveStateCatalog.Default, tinyIntervals));
        using var shutdown = new CancellationTokenSource();

        Task<int> runTask = runtime.RunAsync(shutdown);
        await WaitUntilAsync(() => connection.ReadSampleCalls.Count > 0, runTask);

        shutdown.Cancel();
        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Polls <paramref name="condition"/> until it is true, failing if <paramref name="runTask"/> ends first or the bound elapses.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, Task runTask)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (runTask.IsCompleted)
            {
                await runTask;
                Assert.Fail("The run ended before the expected condition became true.");
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting for the expected condition.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
