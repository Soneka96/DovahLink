using DovahLink.Host;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Authentication;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Process;
using DovahLink.Host.Security;
using DovahLink.Host.Sessions;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

/// <summary>Composes and runs the headless DovahLink host process.</summary>
internal static class Program
{
    /// <summary>Starts the host and keeps it alive until the process is asked to exit.</summary>
    /// <param name="args">The process launch arguments; see <see cref="ParseOwnerLifetimeIdArgument"/>.</param>
    /// <returns>A successful process exit code.</returns>
    private static async Task<int> Main(string[] args)
    {
        OwnerLifetimeId ownerLifetimeId = ParseOwnerLifetimeIdArgument(args);
        int publicListenerPort = ResolvePublicListenerPort(
            Environment.GetEnvironmentVariable(Constants.TestPublicListenerPortEnvironmentVariableName));
        ITrustStorePersistence? trustStorePersistence = ResolveTestTrustStorePersistence(
            Environment.GetEnvironmentVariable(Constants.TestTrustStorePathEnvironmentVariableName));

        using var shutdown = new CancellationTokenSource();
        EventHandler processExitHandler = (_, _) => shutdown.Cancel();
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
        {
            return await ComposeAndRunAsync(
                ownerLifetimeId, Constants.AdapterIpcLoopbackPort, Console.Out, new HostProcessLifetime(), shutdown, publicListenerPort,
                trustStorePersistence);
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
        }
    }

    /// <summary>
    /// Composes the real adapter-IPC stack and the shared trust-services graph for one Skyrim
    /// lifetime, publishes the adapter-IPC rendezvous endpoint, reports it over
    /// <paramref name="rendezvousOutput"/> for a launching adapter to read, and runs until
    /// <paramref name="shutdown"/> is cancelled -- either by the process exiting or by the adapter's
    /// own named shutdown-request signal. Trust persistence is loaded -- and, per its own contract,
    /// fails this entire call closed on malformed or undecryptable data -- before either listener is
    /// constructed, so neither can ever admit a client under a partially loaded or silently reset
    /// trust store.
    /// </summary>
    /// <param name="ownerLifetimeId">The owning Skyrim process's lifetime identity.</param>
    /// <param name="listenerPort">The private adapter-IPC loopback port to bind, or zero to let the operating system assign one.</param>
    /// <param name="rendezvousOutput">
    /// Where to report the bound adapter-IPC port and, once bound, the public listener's own bound
    /// port (as a <c>PUBLICPORT</c> line), peer-proof token, and HostProof HMAC key.
    /// </param>
    /// <param name="lifetime">The host lifetime to run once composition completes.</param>
    /// <param name="shutdown">
    /// The shared shutdown source; cancelled by the caller on process exit, and internally by this
    /// method's own named shutdown-signal watcher. Both the adapter-IPC and (when composed) public
    /// listeners stop admitting new connections and tear down through this one shared token.
    /// </param>
    /// <param name="publicListenerPort">
    /// The public loopback port to bind, or zero to let the operating system assign one. The public
    /// listener is composed and run only when this is supplied; the production <see cref="Main"/>
    /// entry point always supplies one -- <see cref="Constants.PublicWebSocketPort"/> unless
    /// overridden, per <see cref="ResolvePublicListenerPort"/> -- so only test code that calls this
    /// method directly, without going through <see cref="Main"/>, can pass <see langword="null"/> to
    /// leave the public listener uncomposed.
    /// </param>
    /// <param name="trustStorePersistence">
    /// The trust-store persistence adapter to load from and write through to. Defaults to the real
    /// per-Windows-user DPAPI-protected file. A test that calls this method directly may override it
    /// to exercise startup ordering and fail-closed behavior without touching a real encrypted file;
    /// the production <see cref="Main"/> entry point instead redirects it to a private, per-test file
    /// only when <see cref="ResolveTestTrustStorePersistence"/> resolves an override from
    /// <see cref="Constants.TestTrustStorePathEnvironmentVariableName"/>, so a real cross-process test
    /// launch never touches the real store.
    /// </param>
    /// <param name="onComposed">
    /// Invoked once, immediately after composition, with the composed session registry and pairing
    /// coordinator -- test observability only, so a test can inspect authoritative state after this
    /// method's own shutdown teardown has run, without this composition root exposing that state as
    /// part of its own return value or a new production service. Never invoked by the production
    /// <see cref="Main"/> entry point.
    /// </param>
    /// <returns>A successful process exit code once <paramref name="shutdown"/> is cancelled and teardown completes.</returns>
    /// <exception cref="System.Net.Sockets.SocketException">A listener could not bind its configured port.</exception>
    /// <exception cref="InvalidDataException">The persisted trust store exists but could not be decrypted or parsed.</exception>
    internal static async Task<int> ComposeAndRunAsync(
        OwnerLifetimeId ownerLifetimeId,
        int listenerPort,
        TextWriter rendezvousOutput,
        IHostProcessLifetime lifetime,
        CancellationTokenSource shutdown,
        int? publicListenerPort = null,
        ITrustStorePersistence? trustStorePersistence = null,
        Action<SessionRegistry, PairingCoordinator>? onComposed = null)
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        var verifier = new AdapterPeerProofVerifier();
        var codec = new IpcFrameCodec();
        var clock = new SystemClock();

        // Trust-services composition: shared by adapter-originated trust-admin requests and by the
        // public client boundary composed below, over this same instance graph.
        var securityStateGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustStore.CreateAsync(
            trustStorePersistence ?? new WindowsDpapiTrustStorePersistence(), clock, securityStateGate);
        var sessionRegistry = new SessionRegistry(securityStateGate);
        var pairingCoordinator = new PairingCoordinator(trustStore, clock);
        onComposed?.Invoke(sessionRegistry, pairingCoordinator);
        var playContextTracker = new PlayContextTracker();
        var envelopeCodec = new PublicEnvelopeCodec();
        var connectionRegistry = new PublicSessionConnectionRegistry();
        ISessionTerminationNotifier terminationNotifier = new PublicSessionTerminationNotifier(connectionRegistry, envelopeCodec, playContextTracker);
        IClientSessionInvalidator sessionInvalidator = new ClientSessionInvalidator(sessionRegistry, terminationNotifier);
        ITrustAdminService trustAdminService = new TrustAdminService(trustStore, sessionInvalidator, pairingCoordinator);
        ITrustResetService trustResetService = new TrustResetService(trustStore, sessionInvalidator, pairingCoordinator, clock);
        IAdapterTrustAdminRequestHandler trustAdminRequestHandler = new AdapterTrustAdminRequestHandler(trustAdminService, trustResetService, clock);

        using IAdapterIpcListener adapterListener = new AdapterIpcListener(
            listenerPort,
            stream => new AdapterIpcConnection(stream, codec, new AdapterIpcSession(lifecycle, verifier, trustAdminRequestHandler, ownerLifetimeId), clock));
        IPairingAdapterNotifier adapterNotifier = new AdapterPairingNotifier(adapterListener);
        ILocalConnectionTokenAuthenticator tokenAuthenticator = new LocalConnectionTokenAuthenticator(clock);
        ITrustedCredentialFailureThrottle credentialThrottle = new TrustedCredentialFailureThrottle(clock);
        IClientMessageDispatcher dispatcher = new ClientMessageDispatcher(
            envelopeCodec, trustAdminService, pairingCoordinator, adapterNotifier, playContextTracker, clock, sessionRegistry);

        using IPublicWebSocketListener? publicListener = publicListenerPort is int boundPublicPort
            ? new PublicWebSocketListener(boundPublicPort, stream => new PublicWebSocketConnection(
                stream,
                new PublicHelloAdmissionHandler(
                    envelopeCodec, sessionRegistry, trustStore, tokenAuthenticator, credentialThrottle,
                    playContextTracker, clock, dispatcher, pairingCoordinator, connectionRegistry),
                clock,
                new PublicWebSocketTransportOptions(),
                NullPublicWebSocketTransportDiagnostics.Instance))
            : null;

        using var shutdownSignal = new NamedEventHostShutdownSignal(Constants.ShutdownEventName(ownerLifetimeId));
        Task shutdownWatchTask = WatchShutdownSignalAsync(shutdownSignal, shutdown);

        var rendezvousPublisher = new FileHostRendezvousPublisher(Constants.RendezvousFilePath(ownerLifetimeId));
        rendezvousPublisher.Publish(adapterListener.BoundPort, verifier.ExpectedToken, verifier.HostProofKey);

        // PORT, PROOF, and HOSTPROOF are always exactly the first three lines, in this exact
        // order: a real launched process's own native launcher (Win32AdapterHostProcessLauncher)
        // reads exactly three lines from this stream and treats them positionally as those three
        // values, with no public-listener awareness of its own. PUBLICPORT is written last,
        // strictly after them and only when the public listener is composed, so its presence can
        // never shift PROOF or HOSTPROOF into the position that reader expects the other to occupy.
        await rendezvousOutput.WriteLineAsync($"PORT {adapterListener.BoundPort}");
        await rendezvousOutput.WriteLineAsync($"PROOF {Convert.ToHexStringLower(verifier.ExpectedToken)}");
        await rendezvousOutput.WriteLineAsync($"HOSTPROOF {Convert.ToHexStringLower(verifier.HostProofKey)}");
        if (publicListener is not null)
        {
            await rendezvousOutput.WriteLineAsync($"PUBLICPORT {publicListener.BoundPort}");
        }

        await rendezvousOutput.FlushAsync();

        Task adapterListenerTask = adapterListener.RunAsync(shutdown.Token);
        Task publicListenerTask = publicListener?.RunAsync(shutdown.Token) ?? Task.CompletedTask;

        int exitCode = await RunAsync(lifetime, shutdown.Token);

        shutdown.Cancel();
        await adapterListenerTask;
        await publicListenerTask;
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

    /// <summary>
    /// Parses <see cref="Constants.TestPublicListenerPortEnvironmentVariableName"/>'s value into an
    /// explicit public listener port override. A real cross-process test launch sets this to pin the
    /// public listener to a specific known port instead of the production default. An unset or
    /// unparseable value returns <see langword="null"/>, so <see cref="ResolvePublicListenerPort"/>
    /// falls back to <see cref="Constants.PublicWebSocketPort"/>.
    /// </summary>
    /// <param name="value">The environment variable's raw value, or <see langword="null"/> if unset.</param>
    internal static int? ParseTestPublicListenerPort(string? value) =>
        int.TryParse(value, out int port) ? port : null;

    /// <summary>
    /// Resolves the public listener port the production <see cref="Main"/> entry point composes:
    /// <see cref="ParseTestPublicListenerPort"/>'s override when the environment variable is set to a
    /// valid port, otherwise <see cref="Constants.PublicWebSocketPort"/>. Unlike
    /// <see cref="ParseTestPublicListenerPort"/> alone, this always returns a usable port, so the
    /// public listener is composed on every normal production launch rather than only when a test
    /// sets the override.
    /// </summary>
    /// <param name="testPublicListenerPortEnvironmentVariableValue">
    /// <see cref="Constants.TestPublicListenerPortEnvironmentVariableName"/>'s raw value, or
    /// <see langword="null"/> if unset.
    /// </param>
    internal static int ResolvePublicListenerPort(string? testPublicListenerPortEnvironmentVariableValue) =>
        ParseTestPublicListenerPort(testPublicListenerPortEnvironmentVariableValue) ?? Constants.PublicWebSocketPort;

    /// <summary>
    /// Resolves <see cref="Constants.TestTrustStorePathEnvironmentVariableName"/>'s value into an
    /// explicit trust-store persistence override. A real cross-process test launch sets this to
    /// redirect trust persistence to a private, per-test file instead of the real per-Windows-user
    /// DPAPI store. An unset or all-whitespace value returns <see langword="null"/>, so
    /// <see cref="ComposeAndRunAsync"/> falls back to its own default -- the real store -- exactly as
    /// it always has.
    /// </summary>
    /// <param name="value">
    /// <see cref="Constants.TestTrustStorePathEnvironmentVariableName"/>'s raw value, or
    /// <see langword="null"/> if unset.
    /// </param>
    internal static ITrustStorePersistence? ResolveTestTrustStorePersistence(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new WindowsDpapiTrustStorePersistence(value);

    /// <summary>Cancels <paramref name="shutdown"/> once the adapter's named shutdown-request signal is set.</summary>
    /// <param name="signal">The shutdown signal to wait on.</param>
    /// <param name="shutdown">The shared shutdown source to cancel once the signal fires.</param>
    private static async Task WatchShutdownSignalAsync(IHostShutdownSignal signal, CancellationTokenSource shutdown)
    {
        await signal.WaitAsync(shutdown.Token);
        shutdown.Cancel();
    }

    /// <summary>
    /// A minimal composition-time placeholder for <see cref="IPublicWebSocketTransportDiagnostics"/>:
    /// reports to the process's own standard error stream. <see cref="IPublicWebSocketTransportDiagnostics"/>'s
    /// own documentation defers the real logging/telemetry sink to a later concept; this exists only
    /// so today's composition root has some observable signal rather than silently discarding every
    /// report.
    /// </summary>
    private sealed class NullPublicWebSocketTransportDiagnostics : IPublicWebSocketTransportDiagnostics
    {
        /// <summary>The shared, stateless instance every connection reports through.</summary>
        public static readonly NullPublicWebSocketTransportDiagnostics Instance = new();

        /// <inheritdoc/>
        public void ReportAbnormalEnd(PublicWebSocketConnectionEndReason reason)
        {
            try
            {
                Console.Error.WriteLine($"[public-websocket] abnormal end: {reason}");
            }
            catch
            {
                // Must never throw or block; see the interface's own documented contract.
            }
        }
    }
}
