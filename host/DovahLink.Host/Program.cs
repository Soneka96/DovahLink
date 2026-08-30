using DovahLink.Host;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Process;
using DovahLink.Host.Time;

/// <summary>Composes and runs the headless DovahLink host process.</summary>
internal static class Program
{
    /// <summary>Starts the host and keeps it alive until the process is asked to exit.</summary>
    /// <param name="args">The process launch arguments; see <see cref="ParseOwnerLifetimeIdArgument"/>.</param>
    /// <returns>A successful process exit code.</returns>
    private static async Task<int> Main(string[] args)
    {
        OwnerLifetimeId ownerLifetimeId = ParseOwnerLifetimeIdArgument(args);

        using var shutdown = new CancellationTokenSource();
        EventHandler processExitHandler = (_, _) => shutdown.Cancel();
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
        {
            return await ComposeAndRunAsync(
                ownerLifetimeId, Constants.AdapterIpcLoopbackPort, Console.Out, new HostProcessLifetime(), shutdown);
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
        }
    }

    /// <summary>
    /// Composes the real adapter-IPC stack for one Skyrim lifetime, publishes its rendezvous
    /// endpoint, reports it over <paramref name="rendezvousOutput"/> for a launching adapter to
    /// read, and runs until <paramref name="shutdown"/> is cancelled -- either by the process
    /// exiting or by the adapter's own named shutdown-request signal.
    /// </summary>
    /// <param name="ownerLifetimeId">The owning Skyrim process's lifetime identity.</param>
    /// <param name="listenerPort">The loopback port to bind, or zero to let the operating system assign one.</param>
    /// <param name="rendezvousOutput">Where to report the bound port and peer-proof token, once bound.</param>
    /// <param name="lifetime">The host lifetime to run once composition completes.</param>
    /// <param name="shutdown">
    /// The shared shutdown source; cancelled by the caller on process exit, and internally by this
    /// method's own named shutdown-signal watcher.
    /// </param>
    /// <returns>A successful process exit code once <paramref name="shutdown"/> is cancelled and teardown completes.</returns>
    /// <exception cref="System.Net.Sockets.SocketException">The private-IPC listener could not bind <paramref name="listenerPort"/>.</exception>
    internal static async Task<int> ComposeAndRunAsync(
        OwnerLifetimeId ownerLifetimeId,
        int listenerPort,
        TextWriter rendezvousOutput,
        IHostProcessLifetime lifetime,
        CancellationTokenSource shutdown)
    {
        var tracker = new AdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var codec = new IpcFrameCodec();
        var clock = new SystemClock();
        using IAdapterIpcListener listener = new AdapterIpcListener(
            listenerPort,
            stream => new AdapterIpcConnection(stream, codec, new AdapterIpcSession(tracker, verifier, ownerLifetimeId), clock));

        using var shutdownSignal = new NamedEventHostShutdownSignal(Constants.ShutdownEventName(ownerLifetimeId));
        Task shutdownWatchTask = WatchShutdownSignalAsync(shutdownSignal, shutdown);

        var rendezvousPublisher = new FileHostRendezvousPublisher(Constants.RendezvousFilePath(ownerLifetimeId));
        rendezvousPublisher.Publish(listener.BoundPort, verifier.ExpectedToken);

        await rendezvousOutput.WriteLineAsync($"PORT {listener.BoundPort}");
        await rendezvousOutput.WriteLineAsync($"PROOF {Convert.ToHexStringLower(verifier.ExpectedToken)}");
        await rendezvousOutput.FlushAsync();

        Task listenerTask = listener.RunAsync(shutdown.Token);

        int exitCode = await RunAsync(lifetime, shutdown.Token);

        shutdown.Cancel();
        await listenerTask;
        await shutdownWatchTask;
        return exitCode;
    }

    /// <summary>Runs an injected host lifetime and maps clean shutdown to a successful exit code.</summary>
    /// <param name="lifetime">The host lifetime to run.</param>
    /// <param name="cancellationToken">The token used to request shutdown.</param>
    /// <returns>A successful process exit code after the lifetime ends.</returns>
    internal static async Task<int> RunAsync(IHostProcessLifetime lifetime, CancellationToken cancellationToken)
    {
        await lifetime.RunAsync(cancellationToken);
        return 0;
    }

    /// <summary>
    /// Parses the launch arguments' owning Skyrim process lifetime identity. An absent or
    /// unparseable first argument falls back to <see langword="default"/>, which only a
    /// same-lifetime adapter's matching all-zero placeholder could ever satisfy, so this fails
    /// closed rather than starting with a mismatched configuration silently.
    /// </summary>
    /// <param name="args">The process launch arguments.</param>
    internal static OwnerLifetimeId ParseOwnerLifetimeIdArgument(string[] args) =>
        args.Length > 0 && OwnerLifetimeId.TryParse(args[0], out OwnerLifetimeId parsed) ? parsed : default;

    /// <summary>Cancels <paramref name="shutdown"/> once the adapter's named shutdown-request signal is set.</summary>
    /// <param name="signal">The shutdown signal to wait on.</param>
    /// <param name="shutdown">The shared shutdown source to cancel once the signal fires.</param>
    private static async Task WatchShutdownSignalAsync(IHostShutdownSignal signal, CancellationTokenSource shutdown)
    {
        await signal.WaitAsync(shutdown.Token);
        shutdown.Cancel();
    }
}
