using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PairingCeremony;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests;

/// <summary>Builders for representative test values, grouped by area.</summary>
public static class Fixtures
{
    // ---- Identity ----

    /// <summary>Builds a valid Host identity for composition and handshake tests.</summary>
    public static HostIdentity BuildHostIdentity() =>
        new(new HostId(Guid.Parse("81869993-955c-4ba3-a7d0-d35ca86078ea")), "Soneka-Desktop");

    /// <summary>Builds a ready-to-use, never-faulted state-authority lifecycle backed by a fresh fake adapter-availability tracker.</summary>
    public static IStateAuthorityLifecycle BuildStateAuthorityLifecycle() => new StateAuthorityLifecycle(new FakeAdapterAvailabilityTracker());

    /// <summary>
    /// Builds the canonical P-256 SubjectPublicKeyInfo of a published test-vector key. The default is
    /// the DovahLink Bootstrap v1 test Host key; its private scalar is public, so it is never a real key.
    /// </summary>
    /// <param name="subjectPublicKeyInfoHex">The lowercase hex of the SubjectPublicKeyInfo.</param>
    /// <returns>A fresh copy of the SubjectPublicKeyInfo bytes.</returns>
    public static byte[] BuildP256SubjectPublicKeyInfo(
        string subjectPublicKeyInfoHex =
            "3059301306072a8648ce3d020106082a8648ce3d03010703420004f2422a662eb6e5065e3ea5587ed92dd959deff9b9e4115bb76dcb02abf07144a68e354b81cc01714608a8ecd61f8d9ac453cda8b0d20056623db09859432498a") =>
        Convert.FromHexString(subjectPublicKeyInfoHex);

    // ---- Pairing ceremony ----

    /// <summary>Builds this Host's Bootstrap fields from the vector Host ID and published test Host key.</summary>
    /// <returns>The Host Bootstrap fields.</returns>
    public static CeremonyBootstrapFields BuildCeremonyBootstrapFields()
    {
        DovahLinkBootstrap bootstrap = DovahLinkBootstrap.ForHost(
            new HostId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")),
            P256PublicKey.FromSubjectPublicKeyInfo(BuildP256SubjectPublicKeyInfo()));
        return new CeremonyBootstrapFields(bootstrap.ApplicationIdentity, bootstrap.KeyAlgorithm, bootstrap.PublicKey, bootstrap.SharedContext);
    }

    /// <summary>Builds ceremony-host options; the default path is never loaded by a fake native session.</summary>
    /// <param name="nativeLibraryPath">The absolute native library path.</param>
    /// <param name="authorityScope">The authority scope, or <see langword="null"/> for the vector Host scope.</param>
    /// <returns>The options.</returns>
    public static PairingCeremonyHostOptions BuildPairingCeremonyHostOptions(
        string nativeLibraryPath = "C:\\DovahLink\\fake\\sas_pairing_core.dll", byte[]? authorityScope = null) =>
        new(
            nativeLibraryPath,
            authorityScope ?? DovahLinkPairingMapping.EncodeHostAuthorityScope(new HostId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"))),
            BuildCeremonyBootstrapFields());

    /// <summary>Builds a detached local result whose peer frame is the vector client Bootstrap.</summary>
    /// <param name="ceremonyIdentityFill">The byte every ceremony-identity byte is set to.</param>
    /// <param name="peerRole">The peer's role.</param>
    /// <param name="profileVersion">The profile version.</param>
    /// <param name="peerBootstrap">The authenticated peer frame, or <see langword="null"/> for <see cref="BuildClientCandidateFrame"/>.</param>
    /// <param name="sharedContext">The authenticated shared context, or <see langword="null"/> for the DovahLink constant.</param>
    /// <param name="profileIdentifier">The profile identifier, or <see langword="null"/> for the frozen profile's.</param>
    /// <returns>The snapshot.</returns>
    public static CeremonyResultSnapshot BuildCeremonyResultSnapshot(
        byte ceremonyIdentityFill = 0x5a,
        CeremonyPeerRole peerRole = CeremonyPeerRole.Initiator,
        uint profileVersion = 1,
        byte[]? peerBootstrap = null,
        byte[]? sharedContext = null,
        byte[]? profileIdentifier = null) =>
        new(
            Enumerable.Repeat(ceremonyIdentityFill, 32).ToArray(),
            peerRole,
            profileVersion,
            new byte[16],
            peerBootstrap ?? BuildClientCandidateFrame(),
            sharedContext ?? "dovahlink.sas-pairing.bootstrap-v1.pairing"u8.ToArray(),
            profileIdentifier ?? "sas-pairing-vodozemac-profile-draft-01"u8.ToArray());

    /// <summary>
    /// Builds the canonical client Bootstrap frame from candidate values; the defaults are the vector
    /// client ID and published test client key.
    /// </summary>
    /// <param name="clientUuid">The candidate client ID text.</param>
    /// <param name="clientKeyHex">The candidate client key SPKI hex.</param>
    /// <returns>The canonical frame.</returns>
    public static byte[] BuildClientCandidateFrame(
        string clientUuid = "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0",
        string clientKeyHex =
            "3059301306072a8648ce3d020106082a8648ce3d03010703420004fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cbe8be70c2230db3aa58ac0bd5fd14dbb15e6c94a249aee685890665d145a07484") =>
        DovahLinkBootstrap.ForClientCandidate(
            new ClientId(Guid.Parse(clientUuid)),
            P256PublicKey.FromSubjectPublicKeyInfo(BuildP256SubjectPublicKeyInfo(clientKeyHex))).EncodeCanonicalFrame();

    // ---- Protocol ----

    /// <summary>
    /// Builds a codec that can encode every message type, including <c>hello_ack</c>/<c>state_snapshot</c>/<c>state_event</c>,
    /// backed by <see cref="BuildStateAuthorityLifecycle"/>. A test that only decodes, or only encodes
    /// message types that never carry <c>stateAuthorityId</c>, can use a plain <c>new PublicEnvelopeCodec()</c> instead.
    /// </summary>
    public static IPublicEnvelopeCodec BuildPublicEnvelopeCodec() => new PublicEnvelopeCodec(BuildStateAuthorityLifecycle());

    // ---- Client transport ----

    /// <summary>
    /// Builds a <see cref="PublicWebSocketTransportOptions"/> with representative defaults; a test
    /// that wants the default calls this with no arguments, and a test that needs one bound different
    /// overrides only that parameter.
    /// </summary>
    public static PublicWebSocketTransportOptions BuildPublicWebSocketTransportOptions(
        TimeSpan? handshakeTimeout = null,
        TimeSpan? keepAliveInterval = null,
        TimeSpan? keepAlivePongTimeout = null,
        int? maxMessageBytes = null,
        int? maxInboundMessagesPerSecond = null,
        TimeSpan? inboundMessageRateWindow = null,
        int? controlOutboundQueueMaxMessages = null,
        long? outboundQueueMaxBytes = null,
        TimeSpan? gracefulCloseTimeout = null,
        int? maxHandshakeRequestBytes = null,
        TimeSpan? disconnectNotificationTimeout = null,
        TimeSpan? fragmentAssemblyTimeout = null,
        int? dataOutboundQueueMaxMessages = null) =>
        new()
        {
            HandshakeTimeout = handshakeTimeout ?? Constants.PublicWebSocketHandshakeTimeout,
            KeepAliveInterval = keepAliveInterval ?? Constants.PublicWebSocketKeepAliveInterval,
            KeepAlivePongTimeout = keepAlivePongTimeout ?? Constants.PublicWebSocketKeepAlivePongTimeout,
            MaxMessageBytes = maxMessageBytes ?? Constants.PublicWebSocketMaxMessageBytes,
            MaxInboundMessagesPerSecond = maxInboundMessagesPerSecond ?? Constants.PublicWebSocketMaxMessagesPerSecond,
            InboundMessageRateWindow = inboundMessageRateWindow ?? Constants.PublicWebSocketMessageRateWindow,
            ControlOutboundQueueMaxMessages = controlOutboundQueueMaxMessages ?? Constants.PublicWebSocketControlOutboundQueueMaxMessages,
            OutboundQueueMaxBytes = outboundQueueMaxBytes ?? Constants.PublicWebSocketOutboundQueueMaxBytes,
            GracefulCloseTimeout = gracefulCloseTimeout ?? Constants.PublicWebSocketGracefulCloseTimeout,
            MaxHandshakeRequestBytes = maxHandshakeRequestBytes ?? Constants.PublicWebSocketMaxHandshakeRequestBytes,
            DisconnectNotificationTimeout = disconnectNotificationTimeout ?? Constants.PublicWebSocketDisconnectNotificationTimeout,
            FragmentAssemblyTimeout = fragmentAssemblyTimeout ?? Constants.PublicWebSocketFragmentAssemblyTimeout,
            DataOutboundQueueMaxMessages = dataOutboundQueueMaxMessages ?? Constants.PublicWebSocketDataOutboundQueueMaxMessages,
        };

    /// <summary>
    /// Builds a <see cref="PublicWebSocketConnection"/> with representative collaborator defaults; a
    /// test that does not care about the clock, options, or diagnostics passes only what it needs to
    /// override.
    /// </summary>
    public static PublicWebSocketConnection BuildPublicWebSocketConnection(
        Stream stream,
        IPublicWebSocketMessageHandler messageHandler,
        IClock? clock = null,
        PublicWebSocketTransportOptions? options = null,
        IPublicWebSocketTransportDiagnostics? diagnostics = null,
        IDataLaneOutboundQueue? dataLaneQueue = null,
        HostIdentity? hostIdentity = null,
        TimeProvider? fragmentAssemblyTimeProvider = null) =>
        new(
            stream,
            messageHandler,
            clock ?? new SystemClock(),
            options ?? BuildPublicWebSocketTransportOptions(),
            diagnostics ?? new FakePublicWebSocketTransportDiagnostics(),
            dataLaneQueue ?? new DataLaneOutboundQueue(),
            hostIdentity ?? BuildHostIdentity(),
            fragmentAssemblyTimeProvider ?? TimeProvider.System);

    // ---- Client dispatch ----

    /// <summary>
    /// Builds a <see cref="ClientMessageDispatcher"/> with representative collaborator defaults; a
    /// test that wants the default calls this with no arguments, and a test that needs one
    /// collaborator different overrides only that parameter.
    /// </summary>
    public static ClientMessageDispatcher BuildClientMessageDispatcher(
        IPublicEnvelopeCodec? codec = null,
        ITrustAdminService? trustAdminService = null,
        IPairingCoordinator? pairingCoordinator = null,
        IPairingAdapterNotifier? adapterNotifier = null,
        IPlayContextTracker? playContextTracker = null,
        IClock? clock = null,
        ISessionRegistry? sessionRegistry = null) =>
        new(
            codec ?? new PublicEnvelopeCodec(),
            trustAdminService ?? new FakeTrustAdminService(),
            pairingCoordinator ?? new PairingCoordinator(new FakeTrustStore(), clock ?? new FakeClock()),
            adapterNotifier ?? new FakePairingAdapterNotifier(),
            playContextTracker ?? new FakePlayContextTracker(),
            clock ?? new FakeClock(),
            sessionRegistry ?? new FakeSessionRegistry());

    /// <summary>
    /// Builds a <see cref="ClientMessageDispatcher"/> the same way as <see cref="BuildClientMessageDispatcher"/>,
    /// together with one already-active session for <paramref name="clientId"/> on the returned
    /// <see cref="ConnectionId"/>, registered on a fresh <see cref="FakeSessionRegistry"/> the built
    /// dispatcher uses for its own client-bound authorization guard. A test exercising
    /// <c>pairing_request</c>, <c>pairing_cancel</c>, <c>pairing_renotify</c>, <c>pairing_confirm</c>, or
    /// <c>rename_request</c> -- every message type the dispatcher gates on session liveness -- needs a
    /// genuinely registered session rather than an arbitrary unregistered <see cref="SessionId"/>, or
    /// every such dispatch would be rejected as stale before ever reaching the pairing coordinator or
    /// trust admin service.
    /// </summary>
    /// <param name="clientId">The client identity the registered session belongs to.</param>
    /// <param name="codec">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="trustAdminService">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="pairingCoordinator">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="adapterNotifier">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="playContextTracker">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="clock">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    public static (ClientMessageDispatcher Dispatcher, SessionId SessionId, ConnectionId ConnectionId) BuildClientMessageDispatcherWithActiveSession(
        ClientId clientId,
        IPublicEnvelopeCodec? codec = null,
        ITrustAdminService? trustAdminService = null,
        IPairingCoordinator? pairingCoordinator = null,
        IPairingAdapterNotifier? adapterNotifier = null,
        IPlayContextTracker? playContextTracker = null,
        IClock? clock = null)
    {
        var sessionRegistry = new FakeSessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        SessionId sessionId = sessionRegistry.Create(clientId, connectionId);
        ClientMessageDispatcher dispatcher = BuildClientMessageDispatcher(
            codec, trustAdminService, pairingCoordinator, adapterNotifier, playContextTracker, clock, sessionRegistry);
        return (dispatcher, sessionId, connectionId);
    }

    // ---- Sessions ----

    /// <summary>
    /// Builds a <see cref="SessionInvalidationTarget"/> with representative defaults; a test that
    /// wants the default calls this with no arguments, and a test that needs one field different
    /// overrides only that parameter.
    /// </summary>
    public static SessionInvalidationTarget BuildSessionInvalidationTarget(
        SessionId? sessionId = null,
        ConnectionId? connectionId = null,
        ClientId? clientId = null,
        SessionInvalidationReason reason = SessionInvalidationReason.Revoked,
        SessionAuthenticationSource authenticationSource = SessionAuthenticationSource.TrustedDeviceCredential) =>
        new(
            sessionId ?? SessionId.NewId(),
            connectionId ?? ConnectionId.NewId(),
            clientId ?? ClientId.NewId(),
            reason,
            authenticationSource);

    // ---- PlayContext ----

    /// <summary>
    /// Builds a <see cref="FakePlayContextTracker"/> that already has an active play context
    /// established, so a test whose own focus is some other gate or behavior is not also confounded
    /// by a consumer's separate active-context gate (for example <see cref="DovahLink.Host.Adapter.Ipc.LiveStateScheduler"/>'s).
    /// </summary>
    public static FakePlayContextTracker BuildActivePlayContextTracker()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId());
        return tracker;
    }
}
