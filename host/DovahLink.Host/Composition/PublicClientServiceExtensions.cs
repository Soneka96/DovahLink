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
        // No state area is registered and no real domain feed exists, so both placeholders below
        // reflect this composition root's actual current behavior.
        services.AddSingleton<IRegisteredStateAreaPolicy, RegisteredStateAreaPolicy>();
        services.AddSingleton<IStatePublicationFeed>(NullStatePublicationFeed.Instance);
        services.AddSingleton<IPublicWebSocketTransportDiagnostics>(NullPublicWebSocketTransportDiagnostics.Instance);

        services.AddSingleton<ILocalConnectionTokenAuthenticator, LocalConnectionTokenAuthenticator>();
        services.AddSingleton<ITrustedCredentialFailureThrottle, TrustedCredentialFailureThrottle>();
        services.AddSingleton<IClientMessageDispatcher, ClientMessageDispatcher>();
        services.AddSingleton<IPublicConnectionFactory, PublicConnectionFactory>();

        if (publicListenerPort is int boundPublicPort)
        {
            services.AddSingleton(new PublicListenerOptions(boundPublicPort));
            services.AddSingleton<IPublicWebSocketListener, PublicWebSocketListener>();
        }

        return services;
    }

    /// <summary>
    /// A composition-time placeholder for <see cref="IPublicWebSocketTransportDiagnostics"/> that
    /// deliberately discards every report. A synchronous console write here would risk violating that
    /// interface's own must-not-block contract (this is called on the connection's read/write path
    /// during teardown) if standard error is ever a stalled redirected pipe, so this placeholder
    /// stays a true no-op rather than trade that guarantee for an interim observable signal.
    /// </summary>
    private sealed class NullPublicWebSocketTransportDiagnostics : IPublicWebSocketTransportDiagnostics
    {
        /// <summary>The shared, stateless instance every connection reports through.</summary>
        public static readonly NullPublicWebSocketTransportDiagnostics Instance = new();

        /// <inheritdoc/>
        public void ReportAbnormalEnd(PublicWebSocketConnectionEndReason reason)
        {
        }
    }

    /// <summary>
    /// A minimal composition-time placeholder for <see cref="IStatePublicationFeed"/>: never has a
    /// current value and never raises <see cref="IStatePublicationFeed.EventOccurred"/>. Correct
    /// today's composition root's production behavior, since no state area is registered yet --
    /// <see cref="IRegisteredStateAreaPolicy.IsRegistered"/> already rejects every area before any
    /// caller would ever reach this feed, so its own responses are never actually exercised in
    /// production.
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
