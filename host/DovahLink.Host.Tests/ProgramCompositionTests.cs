using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Process;

namespace DovahLink.Host.Tests;

/// <summary>Tests for <see cref="global::Program.ComposeAndRunAsync"/>.</summary>
public class ProgramCompositionTests
{
    /// <summary>Verifies that a missing launch argument falls back to the default owner-lifetime-id.</summary>
    [Fact]
    public void ParseOwnerLifetimeIdArgument_NoArguments_ReturnsDefault()
    {
        OwnerLifetimeId result = global::Program.ParseOwnerLifetimeIdArgument([]);

        Assert.Equal(default, result);
    }

    /// <summary>Verifies that an unparseable first argument falls back to the default owner-lifetime-id.</summary>
    [Fact]
    public void ParseOwnerLifetimeIdArgument_Unparseable_ReturnsDefault()
    {
        OwnerLifetimeId result = global::Program.ParseOwnerLifetimeIdArgument(["not-valid-hex"]);

        Assert.Equal(default, result);
    }

    /// <summary>Verifies that a valid first argument is parsed into the matching owner-lifetime-id.</summary>
    [Fact]
    public void ParseOwnerLifetimeIdArgument_ValidArgument_ReturnsParsedValue()
    {
        var expected = new OwnerLifetimeId(1234, 5678);

        OwnerLifetimeId result = global::Program.ParseOwnerLifetimeIdArgument([expected.Format()]);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that composing and running reports the bound port, peer-proof token, and HostProof
    /// key over the rendezvous output.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_ReportsPortAndProofOverRendezvousOutput()
    {
        var ownerLifetimeId = UniqueOwnerLifetimeId();
        using var shutdown = new CancellationTokenSource();
        var output = new StringWriter();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            ownerLifetimeId, listenerPort: 0, output, new HostProcessLifetime(), shutdown);
        await WaitUntilAsync(() => output.ToString().Contains("PORT "), runTask);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        string reported = output.ToString();
        Assert.Matches(@"PORT \d+", reported);
        Assert.Matches("PROOF [0-9a-f]+", reported);
        Assert.Matches("HOSTPROOF [0-9a-f]+", reported);
    }

    /// <summary>
    /// Verifies that composing and running publishes the rendezvous file for the given
    /// owner-lifetime-id, including the HostProof key.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_PublishesRendezvousFile()
    {
        var ownerLifetimeId = UniqueOwnerLifetimeId();
        using var shutdown = new CancellationTokenSource();
        var output = new StringWriter();
        string rendezvousPath = Constants.RendezvousFilePath(ownerLifetimeId);

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            ownerLifetimeId, listenerPort: 0, output, new HostProcessLifetime(), shutdown);
        await WaitUntilAsync(() => File.Exists(rendezvousPath), runTask);

        try
        {
            string content = File.ReadAllText(rendezvousPath);
            Assert.Matches(@"PORT \d+", content);
            Assert.Matches("PROOF [0-9a-f]+", content);
            Assert.Matches("HOSTPROOF [0-9a-f]+", content);
        }
        finally
        {
            shutdown.Cancel();
            await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(rendezvousPath);
        }
    }

    /// <summary>Verifies that cancelling the shared shutdown source ends a composed run.</summary>
    [Fact]
    public async Task ComposeAndRunAsync_ShutdownCancelled_Ends()
    {
        using var shutdown = new CancellationTokenSource();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), listenerPort: 0, new StringWriter(), new HostProcessLifetime(), shutdown);
        Assert.False(runTask.IsCompleted);

        shutdown.Cancel();

        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that setting the adapter's named shutdown-request signal ends a composed run, exactly as an orderly Skyrim close would.</summary>
    [Fact]
    public async Task ComposeAndRunAsync_ShutdownSignalSet_Ends()
    {
        var ownerLifetimeId = UniqueOwnerLifetimeId();
        using var shutdown = new CancellationTokenSource();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            ownerLifetimeId, listenerPort: 0, new StringWriter(), new HostProcessLifetime(), shutdown);
        Assert.False(runTask.IsCompleted);

        using var adapterSideHandle = new EventWaitHandle(
            false, EventResetMode.ManualReset, Constants.ShutdownEventName(ownerLifetimeId));
        adapterSideHandle.Set();

        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that a private-IPC listener bind failure propagates out of composition rather than being swallowed.</summary>
    [Fact]
    public async Task ComposeAndRunAsync_ListenerPortAlreadyBound_Throws()
    {
        using var occupyingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        occupyingSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        occupyingSocket.Listen(1);
        int occupiedPort = ((IPEndPoint)occupyingSocket.LocalEndPoint!).Port;
        using var shutdown = new CancellationTokenSource();

        await Assert.ThrowsAsync<SocketException>(() => global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), occupiedPort, new StringWriter(), new HostProcessLifetime(), shutdown));
    }

    /// <summary>Builds a unique owner-lifetime-id per test, so parallel and repeated test runs never collide over the same rendezvous file or named event.</summary>
    private static OwnerLifetimeId UniqueOwnerLifetimeId() =>
        new((uint)Random.Shared.Next(), (ulong)Random.Shared.NextInt64());

    /// <summary>Polls <paramref name="condition"/> until it is true, failing if <paramref name="runTask"/> ends first or the bound elapses.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, Task runTask)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (runTask.IsCompleted)
            {
                await runTask;
                Assert.Fail("The composed run ended before the expected condition became true.");
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting for the expected condition.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
