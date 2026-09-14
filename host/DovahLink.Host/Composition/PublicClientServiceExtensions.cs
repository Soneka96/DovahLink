using System.Diagnostics.CodeAnalysis;
using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Authentication;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Subscription;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.State;

namespace DovahLink.Host.Composition;

/// <summary>Composes <see cref="PublicClientServices"/>, the public client boundary.</summary>
public static class PublicClientServiceExtensions
{
    /// <summary>
    /// Constructs the public client listener (when <paramref name="publicListenerPort"/> is
    /// supplied) and its collaborators. Every accepted client connection gets its own
    /// <see cref="PublicWebSocketConnection"/>/<see cref="PublicHelloAdmissionHandler"/>/
    /// <see cref="PublicStateSubscription"/>/<see cref="DataLaneOutboundQueue"/> set, built fresh by
    /// the connection factory below -- connection-scoped, never shared across two accepted
    /// connections.
    /// </summary>
    /// <param name="core">The already-composed core services this graph is built on.</param>
    /// <param name="trust">The already-composed trust-and-session services this graph is built on.</param>
    /// <param name="adapterNotifier">The already-composed adapter-IPC pairing notifier the dispatcher forwards pairing display requests through.</param>
    /// <param name="publicListenerPort">The public loopback port to bind, or <see langword="null"/> to leave the public listener uncomposed.</param>
    /// <returns>The composed public client services.</returns>
    /// <exception cref="System.Net.Sockets.SocketException">The listener could not bind <paramref name="publicListenerPort"/>.</exception>
    public static PublicClientServices ComposePublicClientServices(
        CoreServices core, TrustServices trust, IPairingAdapterNotifier adapterNotifier, int? publicListenerPort)
    {
        // No state area is registered yet and no real domain feed exists -- a later concept
        // registers each real Skyrim domain here and supplies a feed that adapts its captured
        // values, per ai/context/protocol/security.md's "no state area is currently registered".
        IRegisteredStateAreaPolicy registeredStateAreaPolicy = new RegisteredStateAreaPolicy();
        IStatePublicationFeed statePublicationFeed = NullStatePublicationFeed.Instance;

        ILocalConnectionTokenAuthenticator tokenAuthenticator = new LocalConnectionTokenAuthenticator(core.Clock);
        ITrustedCredentialFailureThrottle credentialThrottle = new TrustedCredentialFailureThrottle(core.Clock);
        IClientMessageDispatcher dispatcher = new ClientMessageDispatcher(
            trust.EnvelopeCodec, trust.TrustAdminService, trust.PairingCoordinator, adapterNotifier, trust.PlayContextTracker, core.Clock, trust.SessionRegistry);

        IPublicWebSocketListener? listener = publicListenerPort is int boundPublicPort
            ? new PublicWebSocketListener(
                boundPublicPort,
                stream => new PublicWebSocketConnection(
                    stream,
                    new PublicHelloAdmissionHandler(
                        trust.EnvelopeCodec, trust.SessionRegistry, trust.TrustStore, tokenAuthenticator, credentialThrottle,
                        trust.PlayContextTracker, core.Clock, dispatcher, trust.PairingCoordinator, trust.ConnectionRegistry,
                        subscription: new PublicStateSubscription(registeredStateAreaPolicy, statePublicationFeed, trust.EnvelopeCodec, trust.PlayContextTracker, core.StateAuthorityLifecycle)),
                    core.Clock,
                    new PublicWebSocketTransportOptions(),
                    NullPublicWebSocketTransportDiagnostics.Instance,
                    new DataLaneOutboundQueue()),
                core.Settings.MaxActiveSessions)
            : null;

        return new PublicClientServices(listener);
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

    /// <summary>
    /// A minimal composition-time placeholder for <see cref="IStatePublicationFeed"/>: never has a
    /// current value and never raises <see cref="IStatePublicationFeed.EventOccurred"/>. Correct
    /// today's composition root's production behavior, since no state area is registered yet --
    /// <see cref="IRegisteredStateAreaPolicy.IsRegistered"/> already rejects every area before any
    /// caller would ever reach this feed, so its own responses are never actually exercised in
    /// production. A later concept, once a real domain is registered, supplies a real feed instead.
    /// </summary>
    private sealed class NullStatePublicationFeed : IStatePublicationFeed
    {
        /// <summary>The shared, stateless instance every connection reads through.</summary>
        public static readonly NullStatePublicationFeed Instance = new();

        /// <inheritdoc/>
        public event Action<StateEventPublication>? EventOccurred
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public event Action<StateSnapshotPublication>? SnapshotChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot)
        {
            snapshot = null;
            return false;
        }
    }
}
