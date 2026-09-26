using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Authentication;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Subscription;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
using DovahLink.Host.State;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Client.Transport;

/// <summary>Builds one fresh connection-owned graph for each accepted public client stream.</summary>
public interface IPublicConnectionFactory
{
    /// <summary>
    /// Builds a fresh <see cref="PublicWebSocketConnection"/>/<see cref="PublicHelloAdmissionHandler"/>/
    /// <see cref="PublicStateSubscription"/>/<see cref="IDataLaneOutboundQueue"/> graph for
    /// <paramref name="stream"/>. Every collaborator this method constructs is connection-owned and
    /// never shared across two accepted connections; every collaborator this factory itself was
    /// constructed with is a Host-lifetime singleton shared by every connection.
    /// </summary>
    /// <param name="stream">The accepted connection's own transport stream.</param>
    /// <returns>A connection-owned <see cref="IPublicWebSocketConnection"/>, ready to run.</returns>
    IPublicWebSocketConnection Create(Stream stream);
}

/// <inheritdoc cref="IPublicConnectionFactory"/>
public sealed class PublicConnectionFactory : IPublicConnectionFactory
{
    /// <summary>Encodes and decodes the public wire envelope.</summary>
    private readonly IPublicEnvelopeCodec codec;

    /// <summary>Tracks active sessions.</summary>
    private readonly ISessionRegistry sessionRegistry;

    /// <summary>Owns the durable trust domain.</summary>
    private readonly ITrustStore trustStore;

    /// <summary>Verifies a presented one-time local connection token.</summary>
    private readonly ILocalConnectionTokenAuthenticator tokenAuthenticator;

    /// <summary>Throttles repeated authentication failures.</summary>
    private readonly ITrustedCredentialFailureThrottle credentialThrottle;

    /// <summary>Tracks the current play context.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The time source every connection reports through.</summary>
    private readonly IClock clock;

    /// <summary>Dispatches admitted client messages.</summary>
    private readonly IClientMessageDispatcher dispatcher;

    /// <summary>Owns pairing state.</summary>
    private readonly IPairingCoordinator pairingCoordinator;

    /// <summary>Resolves a session's exact live connection.</summary>
    private readonly IPublicSessionConnectionRegistry connectionRegistry;

    /// <summary>Reports which state areas are currently registered.</summary>
    private readonly IRegisteredStateAreaPolicy registeredStateAreaPolicy;

    /// <summary>The domain feed each connection's subscription reads from.</summary>
    private readonly IStatePublicationFeed statePublicationFeed;

    /// <summary>Owns the state-authority continuity-epoch rotation.</summary>
    private readonly IStateAuthorityLifecycle stateAuthorityLifecycle;

    /// <summary>The Host identity exposed in each connection handshake.</summary>
    private readonly HostIdentity hostIdentity;

    /// <summary>Reports abnormal per-connection transport endings.</summary>
    private readonly IPublicWebSocketTransportDiagnostics diagnostics;

    /// <summary>Creates a factory over the Host-lifetime singletons every accepted connection shares.</summary>
    /// <param name="codec">Encodes and decodes the public wire envelope.</param>
    /// <param name="sessionRegistry">Tracks active sessions.</param>
    /// <param name="trustStore">Owns the durable trust domain.</param>
    /// <param name="tokenAuthenticator">Verifies a presented one-time local connection token.</param>
    /// <param name="credentialThrottle">Throttles repeated authentication failures.</param>
    /// <param name="playContextTracker">Tracks the current play context.</param>
    /// <param name="clock">The time source every connection reports through.</param>
    /// <param name="dispatcher">Dispatches admitted client messages.</param>
    /// <param name="pairingCoordinator">Owns pairing state.</param>
    /// <param name="connectionRegistry">Resolves a session's exact live connection.</param>
    /// <param name="registeredStateAreaPolicy">Reports which state areas are currently registered.</param>
    /// <param name="statePublicationFeed">The domain feed each connection's subscription reads from.</param>
    /// <param name="stateAuthorityLifecycle">Owns the state-authority continuity-epoch rotation.</param>
    /// <param name="hostIdentity">The stable Host ID and current OS computer name exposed in each hello acknowledgement.</param>
    /// <param name="diagnostics">Reports abnormal per-connection transport endings.</param>
    public PublicConnectionFactory(
        IPublicEnvelopeCodec codec,
        ISessionRegistry sessionRegistry,
        ITrustStore trustStore,
        ILocalConnectionTokenAuthenticator tokenAuthenticator,
        ITrustedCredentialFailureThrottle credentialThrottle,
        IPlayContextTracker playContextTracker,
        IClock clock,
        IClientMessageDispatcher dispatcher,
        IPairingCoordinator pairingCoordinator,
        IPublicSessionConnectionRegistry connectionRegistry,
        IRegisteredStateAreaPolicy registeredStateAreaPolicy,
        IStatePublicationFeed statePublicationFeed,
        IStateAuthorityLifecycle stateAuthorityLifecycle,
        HostIdentity hostIdentity,
        IPublicWebSocketTransportDiagnostics diagnostics)
    {
        this.codec = codec;
        this.sessionRegistry = sessionRegistry;
        this.trustStore = trustStore;
        this.tokenAuthenticator = tokenAuthenticator;
        this.credentialThrottle = credentialThrottle;
        this.playContextTracker = playContextTracker;
        this.clock = clock;
        this.dispatcher = dispatcher;
        this.pairingCoordinator = pairingCoordinator;
        this.connectionRegistry = connectionRegistry;
        this.registeredStateAreaPolicy = registeredStateAreaPolicy;
        this.statePublicationFeed = statePublicationFeed;
        this.stateAuthorityLifecycle = stateAuthorityLifecycle;
        this.hostIdentity = hostIdentity;
        this.diagnostics = diagnostics;
    }

    /// <inheritdoc/>
    public IPublicWebSocketConnection Create(Stream stream) =>
        new PublicWebSocketConnection(
            stream,
            new PublicHelloAdmissionHandler(
                codec, sessionRegistry, trustStore, tokenAuthenticator, credentialThrottle,
                playContextTracker, clock, dispatcher, pairingCoordinator, connectionRegistry,
                hostIdentity,
                subscription: new PublicStateSubscription(registeredStateAreaPolicy, statePublicationFeed, codec, playContextTracker, stateAuthorityLifecycle)),
            clock,
            new PublicWebSocketTransportOptions(),
            diagnostics,
            new DataLaneOutboundQueue());
}
