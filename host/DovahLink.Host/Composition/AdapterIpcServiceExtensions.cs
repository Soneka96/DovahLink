using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Process;

namespace DovahLink.Host.Composition;

/// <summary>Composes <see cref="AdapterIpcServices"/>, the private adapter-IPC boundary.</summary>
public static class AdapterIpcServiceExtensions
{
    /// <summary>
    /// Constructs the adapter-IPC listener and its collaborators. Every accepted adapter connection
    /// gets its own <see cref="AdapterIpcConnection"/>/<see cref="AdapterIpcSession"/> pair, built
    /// fresh by the connection factory below -- connection-scoped, never shared across two accepted
    /// connections.
    /// </summary>
    /// <param name="core">The already-composed core services this graph is built on.</param>
    /// <param name="trust">The already-composed trust-and-session services this graph is built on.</param>
    /// <param name="listenerPort">The private adapter-IPC loopback port to bind, or zero to let the operating system assign one.</param>
    /// <param name="ownerLifetimeId">The owning Skyrim process's lifetime identity, verified against every accepted connection.</param>
    /// <returns>The composed adapter-IPC services.</returns>
    /// <exception cref="System.Net.Sockets.SocketException">The listener could not bind <paramref name="listenerPort"/>.</exception>
    public static AdapterIpcServices ComposeAdapterIpcServices(CoreServices core, TrustServices trust, int listenerPort, OwnerLifetimeId ownerLifetimeId)
    {
        var lifecycle = new AdapterConnectionLifecycle(core.AdapterAvailability);
        var verifier = new AdapterPeerProofVerifier();
        var codec = new IpcFrameCodec();

        var listener = new AdapterIpcListener(
            listenerPort,
            stream => new AdapterIpcConnection(stream, codec, new AdapterIpcSession(lifecycle, verifier, trust.TrustAdminRequestHandler, ownerLifetimeId), core.Clock));
        var notifier = new AdapterPairingNotifier(listener);

        return new AdapterIpcServices(verifier, listener, notifier);
    }
}
