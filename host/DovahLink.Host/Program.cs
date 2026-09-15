using System.Diagnostics.CodeAnalysis;
using DovahLink.Host;
using DovahLink.Host.Composition;
using DovahLink.Host.Pairing;
using DovahLink.Host.Process;
using DovahLink.Host.Security;
using DovahLink.Host.Sessions;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

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
    /// lifetime. Trust persistence is loaded -- and, per its own contract, fails this entire call
    /// closed on malformed or undecryptable data -- before either listener is constructed, so
    /// neither can ever admit a client under a partially loaded or silently reset trust store.
    /// </summary>
    /// <param name="ownerLifetimeId">The owning Skyrim process's lifetime identity.</param>
    /// <param name="listenerPort">The private adapter-IPC loopback port to bind, or zero to let the operating system assign one.</param>
    /// <param name="rendezvousOutput">
    /// Where to report the bound adapter-IPC port and, once bound, the public listener's own bound
    /// port (as a <c>PUBLICPORT</c> line), peer-proof token, and HostProof HMAC key.
    /// </param>
    /// <param name="lifetime">The host lifetime to run once composition completes.</param>
    /// <param name="shutdown">
    /// The shared shutdown source; cancelled by the caller on process exit or by this method's own
    /// named shutdown-signal watcher. Both listeners tear down through this one shared token.
    /// </param>
    /// <param name="publicListenerPort">
    /// The public loopback port to bind, or zero for an OS-assigned port. Composed only when
    /// supplied; the production <see cref="Main"/> entry point always supplies one, so only test
    /// code calling this method directly can pass <see langword="null"/> to leave it uncomposed.
    /// </param>
    /// <param name="trustStorePersistence">
    /// The trust-store persistence adapter to load from and write through to. Defaults to the real
    /// per-Windows-user DPAPI-protected file; a test may override it, and the production
    /// <see cref="Main"/> entry point redirects it per <see cref="ResolveTestTrustStorePersistence"/>.
    /// </param>
    /// <param name="onComposed">
    /// Invoked once, immediately after composition, with the composed session registry and pairing
    /// coordinator -- test observability only, letting a test capture references to inspect after
    /// teardown. Never invoked by the production <see cref="Main"/> entry point.
    /// </param>
    /// <param name="hostSettingsProvider">
    /// The provider the user-configured device cap is resolved from. Defaults to the real
    /// <see cref="HostSettingsProvider"/>; a test may override it to exercise a specific cap.
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
        Action<ISessionRegistry, IPairingCoordinator>? onComposed = null,
        IHostSettingsProvider? hostSettingsProvider = null)
    {
        // Trust must load and fail this call closed on malformed data before the container is built --
        // every trust-graph service depends on an already-loaded store. Both are registered as these
        // exact instances below, so every consumer still resolves them through the container.
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, trustStorePersistence);

        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown, hostSettingsProvider);
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort, ownerLifetimeId);
        services.AddPublicClientServices(publicListenerPort);
        services.AddHostRuntime(lifetime, rendezvousOutput);

        // ValidateOnBuild only checks that dependencies are resolvable; it never constructs anything, so a
        // bad-port SocketException or malformed-store InvalidDataException still surfaces, unwrapped, only
        // once IHostRuntime is resolved below and real construction happens.
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        onComposed?.Invoke(provider.GetRequiredService<ISessionRegistry>(), provider.GetRequiredService<IPairingCoordinator>());

        // Resolving the runtime is what triggers construction (and, for the listeners, socket bind) of the
        // whole remaining graph, in the same adapter-then-public order the manual composition it replaces used.
        IHostRuntime runtime = provider.GetRequiredService<IHostRuntime>();
        return await runtime.RunAsync(shutdown);
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
    /// public listener to a specific known port instead of the production default, or to <c>0</c> to
    /// request an OS-assigned ephemeral port. An unset, unparseable, or out-of-range value returns
    /// <see langword="null"/>, so <see cref="ResolvePublicListenerPort"/> falls back to
    /// <see cref="Constants.PublicWebSocketPort"/> rather than passing an invalid port to the
    /// listener.
    /// </summary>
    /// <param name="value">The environment variable's raw value, or <see langword="null"/> if unset.</param>
    internal static int? ParseTestPublicListenerPort(string? value) =>
        int.TryParse(value, out int port) && port is >= 0 and <= 65535 ? port : null;

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
}
