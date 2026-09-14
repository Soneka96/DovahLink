using System.Diagnostics.CodeAnalysis;
using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Authentication;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Subscription;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
using DovahLink.Host.State;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Composition;

/// <summary>Registers the public client boundary.</summary>
public static class PublicClientServiceExtensions
{
    /// <summary>
    /// Registers the public client listener (when <paramref name="publicListenerPort"/> is supplied)
    /// and its collaborators. Every accepted client connection gets its own
    /// <see cref="PublicWebSocketConnection"/>/<see cref="PublicHelloAdmissionHandler"/>/
    /// <see cref="PublicStateSubscription"/>/<see cref="DataLaneOutboundQueue"/> set, built fresh by
    /// the composed <see cref="IPublicConnectionFactory"/> -- connection-scoped, never shared across
    /// two accepted connections. When <paramref name="publicListenerPort"/> is <see langword="null"/>,
    /// no <see cref="IPublicWebSocketListener"/> is registered at all, so resolving it later returns
    /// <see langword="null"/> rather than throwing. Requires
    /// <see cref="CoreServiceExtensions.AddCoreServices"/>, <see cref="TrustServiceExtensions.AddTrustServices"/>,
    /// and <see cref="AdapterIpcServiceExtensions.AddAdapterIpcServices"/> to already be registered on
    /// <paramref name="services"/> -- the dispatcher this graph builds forwards pairing display
    /// requests through the adapter-IPC boundary's own registered <see cref="IPairingAdapterNotifier"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="publicListenerPort">The public loopback port to bind, or <see langword="null"/> to leave the public listener uncomposed.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddPublicClientServices(this IServiceCollection services, int? publicListenerPort)
    {
        // No state area is registered yet and no real domain feed exists -- a later concept
        // registers each real Skyrim domain here and supplies a feed that adapts its captured
        // values, per ai/context/protocol/security.md's "no state area is currently registered".
        services.AddSingleton<IRegisteredStateAreaPolicy, RegisteredStateAreaPolicy>();
        services.AddSingleton<IStatePublicationFeed>(NullStatePublicationFeed.Instance);
        services.AddSingleton<IPublicWebSocketTransportDiagnostics>(NullPublicWebSocketTransportDiagnostics.Instance);

        services.AddSingleton<ILocalConnectionTokenAuthenticator>(sp => new LocalConnectionTokenAuthenticator(sp.GetRequiredService<IClock>()));
        services.AddSingleton<ITrustedCredentialFailureThrottle>(sp => new TrustedCredentialFailureThrottle(sp.GetRequiredService<IClock>()));
        services.AddSingleton<IClientMessageDispatcher>(sp => new ClientMessageDispatcher(
            sp.GetRequiredService<IPublicEnvelopeCodec>(), sp.GetRequiredService<ITrustAdminService>(), sp.GetRequiredService<IPairingCoordinator>(),
            sp.GetRequiredService<IPairingAdapterNotifier>(), sp.GetRequiredService<IPlayContextTracker>(), sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ISessionRegistry>()));

        services.AddSingleton<IPublicConnectionFactory>(sp => new PublicConnectionFactory(
            sp.GetRequiredService<IPublicEnvelopeCodec>(), sp.GetRequiredService<ISessionRegistry>(), sp.GetRequiredService<ITrustStore>(),
            sp.GetRequiredService<ILocalConnectionTokenAuthenticator>(), sp.GetRequiredService<ITrustedCredentialFailureThrottle>(),
            sp.GetRequiredService<IPlayContextTracker>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<IClientMessageDispatcher>(),
            sp.GetRequiredService<IPairingCoordinator>(), sp.GetRequiredService<IPublicSessionConnectionRegistry>(),
            sp.GetRequiredService<IRegisteredStateAreaPolicy>(), sp.GetRequiredService<IStatePublicationFeed>(),
            sp.GetRequiredService<IStateAuthorityLifecycle>(), sp.GetRequiredService<IPublicWebSocketTransportDiagnostics>()));

        if (publicListenerPort is int boundPublicPort)
        {
            services.AddSingleton<IPublicWebSocketListener>(sp => new PublicWebSocketListener(
                boundPublicPort, sp.GetRequiredService<IPublicConnectionFactory>().Create, sp.GetRequiredService<HostSettings>().MaxActiveSessions));
        }

        return services;
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
