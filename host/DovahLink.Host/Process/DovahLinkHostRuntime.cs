using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Process;

/// <summary>
/// Owns one composed Host lifetime's explicit startup and shutdown ordering: publishes the
/// adapter-IPC rendezvous endpoint, runs the adapter-IPC and (when composed) public listeners and the
/// live-state sampling scheduler until the host lifetime ends or shutdown is requested, then tears
/// them all down in a proven-safe order.
/// </summary>
public interface IHostRuntime
{
    /// <summary>
    /// Publishes the rendezvous endpoint, starts both listeners and the live-state scheduler, runs
    /// until <paramref name="shutdown"/> is cancelled, then tears down in order: cancels shutdown,
    /// awaits the adapter-IPC listener, then the public listener, then the live-state scheduler, then
    /// the shutdown-signal watcher.
    /// </summary>
    /// <param name="shutdown">
    /// The shared shutdown source; cancelled by the caller on process exit, and internally by this
    /// runtime's own named shutdown-signal watcher. Both listeners and the live-state scheduler stop
    /// and tear down through this one shared token.
    /// </param>
    /// <returns>A successful process exit code once shutdown completes and teardown finishes.</returns>
    Task<int> RunAsync(CancellationTokenSource shutdown);
}

/// <inheritdoc cref="IHostRuntime"/>
/// <remarks>
/// Ownership and disposal of every collaborator below remain with the composition root that
/// constructs this runtime; this type only makes the run/shutdown sequence explicit and named, not
/// a resource owner.
/// </remarks>
public sealed class DovahLinkHostRuntime : IHostRuntime
{
    /// <summary>Accepts the private adapter-IPC connection.</summary>
    private readonly IAdapterIpcListener adapterListener;

    /// <summary>Drives the host's own sampling cadence for rate-classed capture units.</summary>
    private readonly LiveStateScheduler liveStateScheduler;

    /// <summary>Accepts public client connections, or <see langword="null"/> when not composed.</summary>
    private readonly IPublicWebSocketListener? publicListener;

    /// <summary>Watched for the adapter's own named shutdown-request signal.</summary>
    private readonly IHostShutdownSignal shutdownSignal;

    /// <summary>Run until the process is asked to exit.</summary>
    private readonly IHostProcessLifetime lifetime;

    /// <summary>Publishes the adapter-IPC rendezvous endpoint to its discovery file.</summary>
    private readonly IHostRendezvousPublisher rendezvousPublisher;

    /// <summary>Reports the rendezvous endpoint to a launching adapter reading this process's standard output.</summary>
    private readonly TextWriter rendezvousOutput;

    /// <summary>Supplies this host process's own peer-ownership proof token and HostProof HMAC key.</summary>
    private readonly IAdapterPeerProofVerifier peerProofVerifier;

    /// <summary>
    /// Never read again after construction: held only so dependency injection resolves and
    /// constructs this singleton at startup, which is what registers its play-context-transition
    /// subscription for the host process's own lifetime. Nothing else in the graph depends on it.
    /// </summary>
    private readonly IPlayContextResynchronizationTrigger playContextResynchronizationTrigger;

    /// <summary>Creates a runtime over an already-composed listener/lifecycle graph.</summary>
    /// <param name="adapterListener">Accepts the private adapter-IPC connection.</param>
    /// <param name="shutdownSignal">Watched for the adapter's own named shutdown-request signal.</param>
    /// <param name="lifetime">Run until the process is asked to exit.</param>
    /// <param name="rendezvousPublisher">Publishes the adapter-IPC rendezvous endpoint to its discovery file.</param>
    /// <param name="rendezvousOutput">Reports the rendezvous endpoint to a launching adapter reading this process's standard output.</param>
    /// <param name="peerProofVerifier">Supplies this host process's own peer-ownership proof token and HostProof HMAC key.</param>
    /// <param name="liveStateScheduler">Drives the host's own sampling cadence for rate-classed capture units.</param>
    /// <param name="playContextResynchronizationTrigger">Resolved purely to force its construction; see the field's own doc comment.</param>
    /// <param name="publicListener">
    /// Accepts public client connections, or <see langword="null"/> to run without one. Defaults to
    /// <see langword="null"/> so automatic constructor resolution supplies it without throwing when
    /// <see cref="DovahLink.Host.Composition.PublicClientServiceExtensions.AddPublicClientServices"/> left it unregistered.
    /// </param>
    public DovahLinkHostRuntime(
        IAdapterIpcListener adapterListener,
        IHostShutdownSignal shutdownSignal,
        IHostProcessLifetime lifetime,
        IHostRendezvousPublisher rendezvousPublisher,
        TextWriter rendezvousOutput,
        IAdapterPeerProofVerifier peerProofVerifier,
        LiveStateScheduler liveStateScheduler,
        IPlayContextResynchronizationTrigger playContextResynchronizationTrigger,
        IPublicWebSocketListener? publicListener = null)
    {
        this.adapterListener = adapterListener;
        this.publicListener = publicListener;
        this.shutdownSignal = shutdownSignal;
        this.lifetime = lifetime;
        this.rendezvousPublisher = rendezvousPublisher;
        this.rendezvousOutput = rendezvousOutput;
        this.peerProofVerifier = peerProofVerifier;
        this.liveStateScheduler = liveStateScheduler;
        this.playContextResynchronizationTrigger = playContextResynchronizationTrigger;
    }

    /// <inheritdoc/>
    public async Task<int> RunAsync(CancellationTokenSource shutdown)
    {
        Task shutdownWatchTask = WatchShutdownSignalAsync(shutdownSignal, shutdown);

        byte[] peerProofToken = peerProofVerifier.ExpectedToken;
        byte[] hostProofKey = peerProofVerifier.HostProofKey;
        rendezvousPublisher.Publish(adapterListener.BoundPort, peerProofToken, hostProofKey);

        // PORT, PROOF, and HOSTPROOF are always exactly the first three lines, in this order: a
        // real launched process's native launcher reads exactly three positional lines. PUBLICPORT
        // is written last and only when composed, so it can never shift the other three out of position.
        await rendezvousOutput.WriteLineAsync($"PORT {adapterListener.BoundPort}");
        await rendezvousOutput.WriteLineAsync($"PROOF {Convert.ToHexStringLower(peerProofToken)}");
        await rendezvousOutput.WriteLineAsync($"HOSTPROOF {Convert.ToHexStringLower(hostProofKey)}");
        if (publicListener is not null)
        {
            await rendezvousOutput.WriteLineAsync($"PUBLICPORT {publicListener.BoundPort}");
        }

        await rendezvousOutput.FlushAsync();

        Task adapterListenerTask = adapterListener.RunAsync(shutdown.Token);
        Task publicListenerTask = publicListener?.RunAsync(shutdown.Token) ?? Task.CompletedTask;
        Task liveStateSchedulerTask = liveStateScheduler.RunAsync(shutdown.Token);

        await lifetime.RunAsync(shutdown.Token);

        shutdown.Cancel();
        await adapterListenerTask;
        await publicListenerTask;
        await liveStateSchedulerTask;
        await shutdownWatchTask;
        return 0;
    }

    /// <summary>Cancels <paramref name="shutdown"/> once the adapter's named shutdown-request signal is set.</summary>
    /// <param name="signal">The shutdown signal to wait on.</param>
    /// <param name="shutdown">The shared shutdown source to cancel once the signal fires.</param>
    private static async Task WatchShutdownSignalAsync(IHostShutdownSignal signal, CancellationTokenSource shutdown)
    {
        await signal.WaitAsync(shutdown.Token);
        shutdown.Cancel();
    }
}
