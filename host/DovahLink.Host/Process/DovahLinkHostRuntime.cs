using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Process;

/// <summary>
/// Controls one composed Host lifetime's explicit startup and shutdown ordering: publishes the
/// adapter-IPC rendezvous endpoint, runs the adapter-IPC and (when composed) public listeners until
/// the host lifetime ends or shutdown is requested -- by the caller, or by this runtime's own
/// named-shutdown-signal watcher -- then tears both down in the exact order already proven safe:
/// adapter-IPC listener first, then the public listener, then the shutdown-signal watcher. Ownership
/// and disposal of every collaborator below remain with the composition root that constructs this
/// runtime; this type only makes the run/shutdown sequence explicit and named, not a resource owner.
/// </summary>
public sealed class DovahLinkHostRuntime
{
    /// <summary>Accepts the private adapter-IPC connection.</summary>
    private readonly IAdapterIpcListener adapterListener;

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

    /// <summary>This host process's own peer-ownership proof token.</summary>
    private readonly byte[] peerProofToken;

    /// <summary>This host process's own HostProof HMAC key.</summary>
    private readonly byte[] hostProofKey;

    /// <summary>Creates a runtime over an already-composed listener/lifecycle graph.</summary>
    /// <param name="adapterListener">Accepts the private adapter-IPC connection.</param>
    /// <param name="publicListener">Accepts public client connections, or <see langword="null"/> to run without one.</param>
    /// <param name="shutdownSignal">Watched for the adapter's own named shutdown-request signal.</param>
    /// <param name="lifetime">Run until the process is asked to exit.</param>
    /// <param name="rendezvousPublisher">Publishes the adapter-IPC rendezvous endpoint to its discovery file.</param>
    /// <param name="rendezvousOutput">Reports the rendezvous endpoint to a launching adapter reading this process's standard output.</param>
    /// <param name="peerProofToken">This host process's own peer-ownership proof token.</param>
    /// <param name="hostProofKey">This host process's own HostProof HMAC key.</param>
    public DovahLinkHostRuntime(
        IAdapterIpcListener adapterListener,
        IPublicWebSocketListener? publicListener,
        IHostShutdownSignal shutdownSignal,
        IHostProcessLifetime lifetime,
        IHostRendezvousPublisher rendezvousPublisher,
        TextWriter rendezvousOutput,
        byte[] peerProofToken,
        byte[] hostProofKey)
    {
        this.adapterListener = adapterListener;
        this.publicListener = publicListener;
        this.shutdownSignal = shutdownSignal;
        this.lifetime = lifetime;
        this.rendezvousPublisher = rendezvousPublisher;
        this.rendezvousOutput = rendezvousOutput;
        this.peerProofToken = peerProofToken;
        this.hostProofKey = hostProofKey;
    }

    /// <summary>
    /// Publishes the rendezvous endpoint, starts both listeners, runs until <paramref name="shutdown"/>
    /// is cancelled, then tears down in order: cancels shutdown, awaits the adapter-IPC listener, then
    /// the public listener, then the shutdown-signal watcher.
    /// </summary>
    /// <param name="shutdown">
    /// The shared shutdown source; cancelled by the caller on process exit, and internally by this
    /// runtime's own named shutdown-signal watcher. Both listeners stop admitting new connections and
    /// tear down through this one shared token.
    /// </param>
    /// <returns>A successful process exit code once shutdown completes and teardown finishes.</returns>
    public async Task<int> RunAsync(CancellationTokenSource shutdown)
    {
        Task shutdownWatchTask = WatchShutdownSignalAsync(shutdownSignal, shutdown);

        rendezvousPublisher.Publish(adapterListener.BoundPort, peerProofToken, hostProofKey);

        // PORT, PROOF, and HOSTPROOF are always exactly the first three lines, in this exact
        // order: a real launched process's own native launcher (Win32AdapterHostProcessLauncher)
        // reads exactly three lines from this stream and treats them positionally as those three
        // values, with no public-listener awareness of its own. PUBLICPORT is written last,
        // strictly after them and only when the public listener is composed, so its presence can
        // never shift PROOF or HOSTPROOF into the position that reader expects the other to occupy.
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

        await lifetime.RunAsync(shutdown.Token);

        shutdown.Cancel();
        await adapterListenerTask;
        await publicListenerTask;
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
