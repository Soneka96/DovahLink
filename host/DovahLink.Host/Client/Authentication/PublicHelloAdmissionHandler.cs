using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Client.Authentication;

/// <summary>
/// Implements the public protocol's authentication and session-admission boundary: decodes and
/// validates the required initial <c>hello</c>, authenticates it through the three approved methods,
/// admits a fresh session with the correct trust tier, enforces the pre-authentication admission
/// deadline, replay protection, and the protocol-violation close policy. Enforces the per-tier
/// inbound message allowlist, directly answers the mechanical post-admission exchanges that need no
/// other service -- <c>capabilities</c>, <c>subscribe</c>, and <c>snapshot_request</c>, all currently
/// answered uniformly since no capability or state area is registered -- and routes every other
/// authorized message (<c>ping</c>, every pairing_* message, and <c>rename_request</c>) to the
/// injected <see cref="IClientMessageDispatcher"/>. A dispatch that reports
/// <see cref="ClientDispatchResult.UpgradeToFullTrust"/> upgrades this connection's own session to
/// full trust in place, exactly once, with no reconnect required; one that reports
/// <see cref="ClientDispatchResult.IsProtocolViolation"/> counts toward this connection's own
/// protocol-violation close policy without this handler sending a second error for the same message.
/// Records this connection's disconnect and reconnect with the pairing coordinator for its own
/// reconnect-grace tracking.
/// </summary>
/// <remarks>
/// One instance is constructed per accepted connection and holds this connection's own admission
/// state; every constructor-injected collaborator is shared across every connection.
/// </remarks>
public sealed class PublicHelloAdmissionHandler : IPublicWebSocketMessageHandler
{
    /// <summary>No admission outcome has been claimed yet.</summary>
    private const int OutcomePending = 0;

    /// <summary>A valid <c>hello</c> claimed the admission outcome before the deadline fired.</summary>
    private const int OutcomeAdmitted = 1;

    /// <summary>The pre-authentication deadline claimed the admission outcome before any <c>hello</c> succeeded.</summary>
    private const int OutcomeDeadlineExpired = 2;

    /// <summary>
    /// A fixed, valid-shaped verifier a <c>trusted_device_credential</c> attempt against a nonexistent
    /// or untrusted <c>clientId</c> compares against, so that path's credential comparison always runs
    /// with the same shape (and consumes <see cref="credentialThrottle"/>'s budget the same way) as a
    /// genuine wrong-credential attempt against a known, trusted client -- closing the clientId-rotation
    /// throttle bypass this otherwise-unreachable comparison would leave open.
    /// </summary>
    private static readonly string DummyCredentialVerifier = CredentialHasher.Hash("dummy-credential-verifier-never-matches-a-real-one");

    /// <summary>Decodes and encodes every message this handler sends or receives.</summary>
    private readonly IPublicEnvelopeCodec codec;

    /// <summary>Admits and invalidates this connection's session.</summary>
    private readonly ISessionRegistry sessionRegistry;

    /// <summary>Looks up persisted trust for <c>unpaired</c> and <c>trusted_device_credential</c> hellos.</summary>
    private readonly ITrustStore trustStore;

    /// <summary>Validates and consumes the <c>one_time_local_token</c> developer-authentication credential.</summary>
    private readonly ILocalConnectionTokenAuthenticator tokenAuthenticator;

    /// <summary>Throttles failed <c>trusted_device_credential</c> attempts, independent of <see cref="tokenAuthenticator"/>'s own throttle.</summary>
    private readonly ITrustedCredentialFailureThrottle credentialThrottle;

    /// <summary>Supplies the <c>playContextId</c> stamped onto every host-originated envelope.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The time source used for the protocol-violation window.</summary>
    private readonly IClock clock;

    /// <summary>Routes every authorized <c>ping</c>, pairing_*, and <c>rename_request</c> message to its owning service.</summary>
    private readonly IClientMessageDispatcher dispatcher;

    /// <summary>Records this connection's disconnect and reconnect for pairing reconnect-grace tracking.</summary>
    private readonly IPairingCoordinator pairingCoordinator;

    /// <summary>How long this connection may remain unadmitted before it is closed.</summary>
    private readonly TimeSpan admissionDeadline;

    /// <summary>This connection's own identity, minted once for its entire lifetime.</summary>
    private readonly ConnectionId connectionId = ConnectionId.NewId();

    /// <summary>
    /// Guards every mutable field below against the admission-deadline task's own thread, except
    /// <see cref="admissionOutcome"/>, which uses <see cref="Interlocked"/> instead so exactly one of
    /// a successful <c>hello</c> or the deadline can claim it without either ever blocking on this
    /// lock.
    /// </summary>
    private readonly object gate = new();

    /// <summary>Every client <c>messageId</c> already seen on this connection, bounded to <see cref="Constants.PublicProtocolMaxSessionMessages"/>.</summary>
    private readonly HashSet<string> seenMessageIds = [];

    /// <summary>The UTC time of each protocol violation still inside the close-policy window, oldest first.</summary>
    private readonly Queue<DateTimeOffset> violationTimestamps = new();

    /// <summary>Cancelled once this connection is admitted, ending its own pending admission-deadline task early.</summary>
    private CancellationTokenSource? admissionDeadlineCts;

    /// <summary>
    /// Which of <see cref="OutcomePending"/>, <see cref="OutcomeAdmitted"/>, or
    /// <see cref="OutcomeDeadlineExpired"/> has been claimed. Exactly one caller -- a successful
    /// <c>hello</c> or the admission-deadline task -- ever wins the transition away from
    /// <see cref="OutcomePending"/>. Guarded by <see cref="Interlocked"/>, not <see cref="gate"/>.
    /// </summary>
    private int admissionOutcome;

    /// <summary>Whether a session has been admitted on this connection.</summary>
    private bool admitted;

    /// <summary>This connection's admitted session identity, valid only once <see cref="admitted"/> is <see langword="true"/>.</summary>
    private SessionId sessionId;

    /// <summary>This connection's admitted message-authorization tier, valid only once <see cref="admitted"/> is <see langword="true"/>.</summary>
    private SessionTrustTier trustTier;

    /// <summary>This connection's admitted client identity, valid only once <see cref="admitted"/> is <see langword="true"/>.</summary>
    private ClientId admittedClientId;

    /// <summary>Creates a hello-admission handler for one accepted connection.</summary>
    /// <param name="codec">Decodes and encodes every message this handler sends or receives.</param>
    /// <param name="sessionRegistry">Admits and invalidates this connection's session.</param>
    /// <param name="trustStore">Looks up persisted trust for <c>unpaired</c> and <c>trusted_device_credential</c> hellos.</param>
    /// <param name="tokenAuthenticator">Validates and consumes the <c>one_time_local_token</c> developer-authentication credential.</param>
    /// <param name="credentialThrottle">Throttles failed <c>trusted_device_credential</c> attempts.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto every host-originated envelope.</param>
    /// <param name="clock">The time source used for the protocol-violation window.</param>
    /// <param name="dispatcher">Routes every authorized <c>ping</c>, pairing_*, and <c>rename_request</c> message to its owning service.</param>
    /// <param name="pairingCoordinator">Records this connection's disconnect and reconnect for pairing reconnect-grace tracking.</param>
    /// <param name="admissionDeadline">How long this connection may remain unadmitted before it is closed. Defaults to <see cref="Constants.PublicHelloAdmissionDeadline"/>.</param>
    public PublicHelloAdmissionHandler(
        IPublicEnvelopeCodec codec,
        ISessionRegistry sessionRegistry,
        ITrustStore trustStore,
        ILocalConnectionTokenAuthenticator tokenAuthenticator,
        ITrustedCredentialFailureThrottle credentialThrottle,
        IPlayContextTracker playContextTracker,
        IClock clock,
        IClientMessageDispatcher dispatcher,
        IPairingCoordinator pairingCoordinator,
        TimeSpan? admissionDeadline = null)
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
        this.admissionDeadline = admissionDeadline ?? Constants.PublicHelloAdmissionDeadline;
    }

    /// <inheritdoc/>
    public void HandleConnectionEstablished(IPublicConnectionContext connection)
    {
        var deadlineCts = new CancellationTokenSource();
        lock (gate)
        {
            admissionDeadlineCts = deadlineCts;
        }

        _ = RunAdmissionDeadlineAsync(connection, deadlineCts);
    }

    /// <summary>
    /// Waits for <see cref="admissionDeadline"/>, then closes the connection unless admission has
    /// already claimed the outcome. Cancelling <paramref name="deadlineCts"/> (from
    /// <see cref="Admit"/>) ends this wait early without closing anything.
    /// </summary>
    private async Task RunAdmissionDeadlineAsync(IPublicConnectionContext connectionContext, CancellationTokenSource deadlineCts)
    {
        try
        {
            await Task.Delay(admissionDeadline, deadlineCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref admissionOutcome, OutcomeDeadlineExpired, OutcomePending) == OutcomePending)
        {
            connectionContext.RequestClose();
        }
    }

    /// <inheritdoc/>
    public async Task HandleMessageAsync(IPublicConnectionContext connectionContext, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (!codec.TryDecode(payload, out PublicEnvelope? envelope))
        {
            RecordViolationAndReject(connectionContext, correlationId: null, PublicProtocolErrorCode.MalformedMessage, "The message could not be decoded.");
            return;
        }

        if (!TryRecordMessageId(envelope.MessageId, out bool boundAlreadyExceeded, out bool reachedBound))
        {
            if (!boundAlreadyExceeded)
            {
                RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.ReplayedMessage, "This messageId was already used on this session.");
            }

            // A message arriving after the session's message bound was already reached is silently
            // dropped rather than answered: the connection is already closing, per the security
            // contract's "do not retry invalid input indefinitely," and this message was never
            // recorded or dispatched.
            return;
        }

        try
        {
            bool isAdmitted;
            SessionId currentSessionId;
            lock (gate)
            {
                isAdmitted = admitted;
                currentSessionId = sessionId;
            }

            if (!isAdmitted)
            {
                if (envelope.MessageType != PublicMessageType.Hello)
                {
                    RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "Only hello is accepted before a session is admitted.");
                    return;
                }

                if (!IsValidPreAuthEnvelopeIdentity(envelope))
                {
                    RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The hello envelope carries an identity or context field that is not yet established.");
                    return;
                }

                HandleHello(connectionContext, envelope);
                return;
            }

            ClientId currentClientId;
            SessionTrustTier currentTier;
            lock (gate)
            {
                currentClientId = admittedClientId;
                currentTier = trustTier;
            }

            if (envelope.SessionId != currentSessionId.ToString() || !sessionRegistry.IsActive(currentSessionId, connectionId))
            {
                // The second condition catches a session this connection still believes is admitted but
                // that the authoritative registry no longer considers active -- for example, invalidated
                // by a concurrent Factory Reset after this connection's own admission completed. Local
                // admitted state alone must never be treated as authorization forever.
                RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.StaleSession, "This sessionId is not valid on this connection.");
                return;
            }

            if (envelope.BridgeInstanceId is not null || envelope.PlayContextId is not null ||
                !Guid.TryParse(envelope.ClientId, out Guid presentedClientId) || presentedClientId != currentClientId.Value)
            {
                // A post-admission client message must carry the socket-bound sessionId and its declared
                // clientId: clientId is required (not merely permitted) once a session exists. Comparing
                // the parsed Guid value rather than envelope.ClientId's raw wire string
                // against currentClientId.ToString() avoids reintroducing a textual-representation-aliasing
                // gap: Guid.TryParse accepts several equivalent textual forms (braces, hyphenless, etc.)
                // for the same identity, and a client is not required to reuse hello's exact wire form on
                // every later message.
                RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "This message carries an invalid envelope identity or context field.");
                return;
            }

            if (!IsAllowedForTier(envelope.MessageType, currentTier))
            {
                PublicProtocolErrorCode code = IsPostAdmissionClientMessageType(envelope.MessageType)
                    ? PublicProtocolErrorCode.Unauthorized
                    : PublicProtocolErrorCode.MalformedMessage;
                string message = code == PublicProtocolErrorCode.Unauthorized
                    ? "This session is not authorized to send this message."
                    : "This message type is not allowed on this session.";
                RecordViolationAndReject(connectionContext, envelope.MessageId, code, message);
                return;
            }

            switch (envelope.MessageType)
            {
                case PublicMessageType.Capabilities:
                    HandleCapabilities(connectionContext, envelope);
                    break;
                case PublicMessageType.Subscribe:
                    HandleSubscribe(connectionContext, envelope);
                    break;
                case PublicMessageType.SnapshotRequest:
                    HandleSnapshotRequest(connectionContext, envelope);
                    break;
                default:
                    // ping, every pairing_* message, and rename_request are authorized by the allowlist
                    // above; every other message this dispatcher is responsible for is mapped to its
                    // owning service.
                    ClientDispatchResult dispatchResult = await dispatcher.DispatchAsync(
                        currentClientId, currentSessionId, connectionContext, envelope, cancellationToken).ConfigureAwait(false);
                    if (dispatchResult.IsProtocolViolation)
                    {
                        RecordViolation(connectionContext);
                    }

                    if (dispatchResult.UpgradeToFullTrust)
                    {
                        // The pairing coordinator already committed this credential to durable trust
                        // before reporting this signal; this upgrades the same session's own
                        // authorization tier in place, exactly once, with no reconnect required. A losing
                        // race against a concurrent invalidation is caught the same way every other
                        // post-admission message already is -- the next message's own IsActive recheck --
                        // so an unconditional local update here can never grant authorization the
                        // authoritative registry does not also (still) recognize.
                        lock (gate)
                        {
                            trustTier = SessionTrustTier.Full;
                        }

                        sessionRegistry.TryUpgradeToFullTrust(currentSessionId, connectionId);
                    }

                    break;
            }
        }
        finally
        {
            // The message that reaches the session message bound is itself still accepted, per
            // ai/context/protocol/security.md's "the bridge closes the session before this bound is
            // exceeded": everything above -- including a response this exact message triggers, via
            // either a successful dispatch or RecordViolationAndReject's own error send -- has already
            // had its chance to enqueue onto the outbound queue before the orderly close begins here.
            if (reachedBound)
            {
                connectionContext.RequestClose();
            }
        }
    }

    /// <summary>
    /// Reports whether a pre-authentication <c>hello</c> envelope carries only the identity/context
    /// fields <c>protocol/schema/README.md</c>'s common envelope allows before a session exists:
    /// <c>sessionId</c>, <c>bridgeInstanceId</c>, the envelope-level <c>clientId</c>, and
    /// <c>correlationId</c> must all be <see langword="null"/> (none of them are established yet), and
    /// <c>playContextId</c> is host-authoritative, never a value a client asserts. This is separate
    /// from <see cref="HandleHello"/>'s own payload-level validation (<c>hello.clientId</c>,
    /// <c>endpoint</c>, <c>auth</c>): a structurally valid payload can still carry a semantically
    /// invalid envelope, per the contract to reject malformed envelope identity fields before calling
    /// authentication services.
    /// </summary>
    private static bool IsValidPreAuthEnvelopeIdentity(PublicEnvelope envelope) =>
        envelope.SessionId is null &&
        envelope.BridgeInstanceId is null &&
        envelope.ClientId is null &&
        envelope.CorrelationId is null &&
        envelope.PlayContextId is null;

    /// <summary>
    /// Reports whether <paramref name="messageType"/> is a client-originated message this session's
    /// trust tier may send, per <c>ai/context/protocol/security.md</c>'s "Hello authentication and
    /// session trust tiers". A server-originated type (<c>hello_ack</c>, <c>pairing_status</c>,
    /// <c>pairing_outcome</c>, <c>rename_outcome</c>, <c>subscription_ack</c>, <c>state_snapshot</c>,
    /// <c>state_event</c>, <c>error</c>, <c>session_invalidated</c>, <c>pong</c>) is never allowed
    /// from a client, in either tier; neither is a bare <c>hello</c> once a session already exists.
    /// </summary>
    private static bool IsAllowedForTier(PublicMessageType messageType, SessionTrustTier tier) => tier switch
    {
        SessionTrustTier.Restricted => messageType
            is PublicMessageType.Ping
            or PublicMessageType.Capabilities
            or PublicMessageType.PairingRequest
            or PublicMessageType.PairingConfirm
            or PublicMessageType.PairingAck
            or PublicMessageType.PairingRenotify
            or PublicMessageType.PairingCancel,
        SessionTrustTier.Full => messageType
            is PublicMessageType.Ping
            or PublicMessageType.Capabilities
            or PublicMessageType.RenameRequest
            or PublicMessageType.Subscribe
            or PublicMessageType.SnapshotRequest,
        _ => false,
    };

    /// <summary>
    /// Reports whether <paramref name="messageType"/> belongs to the post-admission client vocabulary
    /// -- allowed for at least one trust tier, per <see cref="IsAllowedForTier"/> -- as opposed to
    /// <c>hello</c> (valid only before a session exists) or a server-originated type (never valid from
    /// a client, in either tier). A message in this vocabulary rejected by <see cref="IsAllowedForTier"/>
    /// is a genuine trust-tier authorization failure (<c>unauthorized</c>); one outside it is a
    /// protocol shape/direction violation (<c>malformed_message</c>) that no tier could ever authorize.
    /// Derived directly from <see cref="IsAllowedForTier"/> rather than duplicating its message list,
    /// so the two classifications can never drift apart.
    /// </summary>
    private static bool IsPostAdmissionClientMessageType(PublicMessageType messageType) =>
        IsAllowedForTier(messageType, SessionTrustTier.Restricted) || IsAllowedForTier(messageType, SessionTrustTier.Full);

    /// <summary>
    /// Answers a <c>capabilities</c> advertisement: an empty list gets no response, per the schema's
    /// "a client may send an empty capabilities advertisement with no response"; a non-empty list of
    /// structurally valid descriptors is rejected as unsupported, since no capability is currently
    /// registered. A structurally invalid descriptor -- decoded no further than a well-formed JSON
    /// object by <see cref="IPublicEnvelopeCodec.TryDecode"/>, so it reaches this method rather than
    /// failing envelope decoding -- is rejected as malformed before this method ever classifies it as
    /// unsupported.
    /// </summary>
    private void HandleCapabilities(IPublicConnectionContext connectionContext, PublicEnvelope envelope)
    {
        if (!codec.TryDecodePayload(envelope, out CapabilitiesPayload? payload) || payload.Capabilities.Any(capability => capability is null))
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The capabilities message is malformed.");
            return;
        }

        if (payload.Capabilities.Count == 0)
        {
            return;
        }

        RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.UnsupportedCapability, "No capability is currently supported.");
    }

    /// <summary>Answers a <c>subscribe</c> request: every requested area is rejected, since no state area is currently registered.</summary>
    private void HandleSubscribe(IPublicConnectionContext connectionContext, PublicEnvelope envelope)
    {
        if (!codec.TryDecodePayload(envelope, out SubscribePayload? payload) || payload.StateAreas.Any(stateArea => stateArea is null))
        {
            // A null element within stateAreas is a schema violation the .NET nullable-annotation-aware
            // deserializer does not catch on its own: it validates a property's own value against its
            // declared nullability, not the nullability of a collection's individual elements.
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The subscribe message is malformed.");
            return;
        }

        SessionId currentSessionId;
        lock (gate)
        {
            currentSessionId = sessionId;
        }

        var ackPayload = new SubscriptionAckPayload { AcceptedStateAreas = [], RejectedStateAreas = payload.StateAreas };
        PlayContextSnapshot snapshot = playContextTracker.GetSnapshot();
        byte[] bytes = codec.Encode(
            PublicMessageType.SubscriptionAck,
            NewMessageId(),
            currentSessionId.ToString(),
            envelope.MessageId,
            snapshot.Current?.ToString(),
            null,
            ackPayload);
        connectionContext.TrySend(bytes);
    }

    /// <summary>Answers a <c>snapshot_request</c>: always rejected as unsupported, since no state area is currently registered.</summary>
    private void HandleSnapshotRequest(IPublicConnectionContext connectionContext, PublicEnvelope envelope)
    {
        if (!codec.TryDecodePayload(envelope, out SnapshotRequestPayload? _))
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The snapshot_request message is malformed.");
            return;
        }

        RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.UnsupportedCapability, "No state area is currently registered.");
    }

    /// <summary>
    /// Records <paramref name="messageId"/> as seen, rejecting a repeat as replay. Reports, without
    /// itself requesting the connection's close, when this message recording reaches the
    /// session-lifetime message bound, per <c>ai/context/protocol/security.md</c>'s "the bridge closes
    /// the session before this bound is exceeded" -- the message that reaches the bound is still
    /// accepted; only a later one is not. The caller alone decides when to actually request the close:
    /// it must first let this message's own dispatch and any response it produces reach the outbound
    /// queue, since <see cref="IPublicConnectionContext.RequestClose"/> completes that queue immediately
    /// rather than only once teardown finishes. Checks the bound before recording, not only after: a
    /// message already in flight through the read loop when the bound is reached could otherwise still
    /// reach this method and be recorded before the requested close actually takes effect. Checking the
    /// bound first means such a message is never added and never dispatched, regardless of timing.
    /// </summary>
    /// <param name="messageId">The message id to record.</param>
    /// <param name="boundAlreadyExceeded">
    /// Set when this call was rejected only because an earlier message already reached the bound --
    /// distinct from an ordinary replay, since <paramref name="messageId"/> itself was never seen before.
    /// </param>
    /// <param name="reachedBound">
    /// Set when this exact call's recording brought the session to its message bound; the caller must
    /// request this connection's close only after this message's own response, if any, has already had
    /// its chance to enqueue.
    /// </param>
    private bool TryRecordMessageId(string messageId, out bool boundAlreadyExceeded, out bool reachedBound)
    {
        lock (gate)
        {
            reachedBound = false;
            if (seenMessageIds.Count >= Constants.PublicProtocolMaxSessionMessages)
            {
                boundAlreadyExceeded = true;
                return false;
            }

            boundAlreadyExceeded = false;
            if (!seenMessageIds.Add(messageId))
            {
                return false;
            }

            reachedBound = seenMessageIds.Count >= Constants.PublicProtocolMaxSessionMessages;
            return true;
        }
    }

    /// <summary>
    /// Sends the canonical error for a rejected message, then records one protocol violation and
    /// closes the connection once <see cref="Constants.PublicProtocolMaxViolationsPerWindow"/> is
    /// reached within <see cref="Constants.PublicProtocolViolationWindow"/>.
    /// </summary>
    private void RecordViolationAndReject(
        IPublicConnectionContext connectionContext, string? correlationId, PublicProtocolErrorCode code, string message, bool retryable = false)
    {
        SendError(connectionContext, correlationId, code, message, retryable);
        RecordViolation(connectionContext);
    }

    /// <summary>
    /// Records one protocol violation and closes the connection once
    /// <see cref="Constants.PublicProtocolMaxViolationsPerWindow"/> is reached within
    /// <see cref="Constants.PublicProtocolViolationWindow"/>, without itself sending an error --
    /// for a violation the dispatcher already reported and sent its own error for, per
    /// <see cref="ClientDispatchResult.IsProtocolViolation"/>.
    /// </summary>
    private void RecordViolation(IPublicConnectionContext connectionContext)
    {
        bool exceeded;
        lock (gate)
        {
            DateTimeOffset now = clock.UtcNow;
            DateTimeOffset windowStart = now - Constants.PublicProtocolViolationWindow;
            while (violationTimestamps.Count > 0 && violationTimestamps.Peek() < windowStart)
            {
                violationTimestamps.Dequeue();
            }

            violationTimestamps.Enqueue(now);
            exceeded = violationTimestamps.Count >= Constants.PublicProtocolMaxViolationsPerWindow;
        }

        if (exceeded)
        {
            connectionContext.RequestClose();
        }
    }

    /// <summary>Sends a canonical <c>error</c> message, carrying this connection's session identity once admitted.</summary>
    private void SendError(
        IPublicConnectionContext connectionContext, string? correlationId, PublicProtocolErrorCode code, string message, bool retryable)
    {
        bool isAdmitted;
        SessionId currentSessionId;
        lock (gate)
        {
            isAdmitted = admitted;
            currentSessionId = sessionId;
        }

        var payload = new ErrorPayload { Code = code, Message = message, Retryable = retryable };
        PlayContextSnapshot snapshot = playContextTracker.GetSnapshot();
        byte[] bytes = codec.Encode(
            PublicMessageType.Error,
            NewMessageId(),
            isAdmitted ? currentSessionId.ToString() : null,
            correlationId,
            snapshot.Current?.ToString(),
            null,
            payload);
        connectionContext.TrySend(bytes);
    }

    /// <summary>Validates a decoded <c>hello</c> and dispatches to the presented authentication method.</summary>
    private void HandleHello(IPublicConnectionContext connectionContext, PublicEnvelope envelope)
    {
        if (!codec.TryDecodePayload(envelope, out HelloPayload? hello) ||
            hello.Endpoint != "client" ||
            !Guid.TryParse(hello.ClientId, out Guid clientIdValue) ||
            !IsValidAuth(hello.Auth))
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The hello message is malformed.");
            return;
        }

        var requestedClientId = new ClientId(clientIdValue);
        switch (hello.Auth.Method)
        {
            case HelloAuthMethod.OneTimeLocalToken:
                HandleOneTimeLocalTokenHello(connectionContext, envelope, requestedClientId, hello.Auth.Token!);
                break;
            case HelloAuthMethod.Unpaired:
                HandleTrustBackedHello(connectionContext, envelope, requestedClientId, SessionAuthenticationSource.Unpaired, credential: null);
                break;
            case HelloAuthMethod.TrustedDeviceCredential:
                HandleTrustBackedHello(connectionContext, envelope, requestedClientId, SessionAuthenticationSource.TrustedDeviceCredential, hello.Auth.Token);
                break;
        }
    }

    /// <summary>
    /// Validates <c>auth.token</c>'s required-or-absent shape for the presented method, per
    /// <c>protocol/schema/README.md</c>'s "<c>hello</c>" section. A <c>trusted_device_credential</c>
    /// token's wire shape -- exactly <see cref="Constants.PairingCredentialLength"/> hex characters --
    /// is validated here, before the credential ever reaches <see cref="CredentialHasher.Hash"/> or
    /// the credential throttle, so a malformed presented credential is rejected as malformed protocol
    /// input rather than as an ordinary failed secret comparison.
    /// </summary>
    private static bool IsValidAuth(HelloAuthPayload auth) => auth.Method switch
    {
        HelloAuthMethod.Unpaired => auth.Token is null,
        HelloAuthMethod.OneTimeLocalToken => !string.IsNullOrEmpty(auth.Token),
        HelloAuthMethod.TrustedDeviceCredential => CredentialHasher.IsValidHexCredential(auth.Token, Constants.PairingCredentialLength),
        _ => false,
    };

    /// <summary>
    /// Authenticates a <c>one_time_local_token</c> hello. Validates and reserves the token without
    /// consuming it (<see cref="ILocalConnectionTokenAuthenticator.TryValidate"/>), rolling the
    /// reservation back on every failure branch after that (a full session slot, losing the race
    /// against the pre-authentication deadline, or losing <see
    /// cref="ISessionRegistry.TryFinalizeAdmission"/>'s linearized race against an unconditional
    /// Factory Reset) so a retryable failure never burns the token, and commits consumption only once
    /// admission has fully succeeded. The Factory Reset branch also requests this connection's close:
    /// its one-shot admission outcome is already consumed by that point, so the connection can never
    /// complete admission again and must not be left open with no path to teardown.
    /// </summary>
    private void HandleOneTimeLocalTokenHello(
        IPublicConnectionContext connectionContext, PublicEnvelope envelope, ClientId requestedClientId, string token)
    {
        if (!tokenAuthenticator.TryValidate(token, out LocalConnectionTokenReservation reservation))
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Unauthenticated, "Authentication failed.");
            return;
        }

        if (!sessionRegistry.TryCreate(requestedClientId, connectionId, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId newSessionId))
        {
            tokenAuthenticator.RollbackReservation(reservation);
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.RateLimited, "The host cannot admit another session right now.", retryable: true);
            return;
        }

        if (!TryClaimAdmission())
        {
            tokenAuthenticator.RollbackReservation(reservation);
            sessionRegistry.Invalidate(newSessionId, connectionId);
            return;
        }

        if (!sessionRegistry.TryFinalizeAdmission(newSessionId, connectionId))
        {
            // TryFinalizeAdmission is the sole linearization point between this admission and a
            // concurrent unconditional invalidation (Factory Reset): an invalidation that reached the
            // registry before this call decides the outcome here, so this branch means the session is
            // already gone and must not be admitted. TryClaimAdmission already consumed this
            // connection's one-shot admission outcome (deliberately never reset back to Pending, so a
            // concurrently racing deadline task can never mistake this for still-pending admission),
            // so this connection can never complete admission again; close it explicitly rather than
            // leaving it open with no path to ever being torn down.
            tokenAuthenticator.RollbackReservation(reservation);
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.RateLimited, "The host cannot admit another session right now.", retryable: true);
            connectionContext.RequestClose();
            return;
        }

        tokenAuthenticator.CommitConsumption(reservation);
        Admit(connectionContext, envelope, requestedClientId, newSessionId, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full);
    }

    /// <summary>
    /// Authenticates an <c>unpaired</c> or <c>trusted_device_credential</c> hello against persisted
    /// trust, rechecking trust once more after the session slot is reserved so a losing race against
    /// a concurrent administrative Block/Revoke never leaves a session admitted for an identity that
    /// no longer qualifies. For <c>trusted_device_credential</c> specifically, the recheck also
    /// requires the authoritative current record to still exist, still be <see
    /// cref="KnownDeviceState.Trusted"/>, and still validate the presented credential against its
    /// current <see cref="TrustRecord.CredentialVerifier"/> -- not merely the absence of Blocked/Revoked
    /// -- so a Factory Reset that deletes the record, or an administrative credential rotation, between
    /// the initial check and this recheck can never admit a session for a credential that is no longer
    /// current. A deleted record is rejected the same way as a never-paired identity (<see
    /// cref="PublicProtocolErrorCode.Unauthenticated"/>), per <c>ai/context/protocol/security.md</c>'s
    /// "Because Factory Reset deletes every Known Device record ... the Bridge rejects it through the
    /// ordinary unrecognized-credential/unpaired path". This recheck compares directly rather than
    /// through <see cref="credentialThrottle"/>: it re-validates an attempt already accounted for by the
    /// initial check above, so re-throttling it here would let unrelated concurrent failures spuriously
    /// fail an otherwise-legitimate admission. Also calls <see
    /// cref="ISessionRegistry.TryFinalizeAdmission"/> as the last registry interaction before claiming
    /// admission's final outcome -- the linearization point between this admission and a concurrent
    /// unconditional Factory Reset -- so an invalidation that reached the registry first can never
    /// result in a <c>hello_ack</c> for a session the authoritative registry no longer knows about;
    /// that branch also requests this connection's close, since its one-shot admission outcome is
    /// already consumed and it can never complete admission again.
    /// </summary>
    private void HandleTrustBackedHello(
        IPublicConnectionContext connectionContext,
        PublicEnvelope envelope,
        ClientId requestedClientId,
        SessionAuthenticationSource source,
        string? credential)
    {
        TrustRecord? record = trustStore.TryGet(requestedClientId);
        if (record?.State == KnownDeviceState.Blocked)
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Blocked, "This client is blocked.");
            return;
        }

        SessionTrustTier tier;
        if (source == SessionAuthenticationSource.TrustedDeviceCredential)
        {
            if (record?.State == KnownDeviceState.Revoked)
            {
                RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Revoked, "This client's trust has been revoked.");
                return;
            }

            // A nonexistent/untrusted record compares against DummyCredentialVerifier instead of
            // short-circuiting before credentialThrottle.TryAttempt: otherwise an attacker could evade
            // the global failure budget entirely by rotating clientId, since a random unseeded clientId
            // would never reach the throttle at all. isEligible is combined with the bitwise `&`, not
            // the short-circuiting `&&`, so CredentialHasher.FixedTimeEquals always actually runs even
            // when isEligible is false -- keeping this path's timing shape identical to a genuine
            // wrong-credential comparison against a known, trusted client.
            bool isEligible = record is not null && record.State == KnownDeviceState.Trusted;
            string verifierToCompare = isEligible ? record!.CredentialVerifier : DummyCredentialVerifier;
            if (!credentialThrottle.TryAttempt(() => isEligible & CredentialHasher.FixedTimeEquals(verifierToCompare, CredentialHasher.Hash(credential!))))
            {
                RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Unauthenticated, "Authentication failed.");
                return;
            }

            tier = SessionTrustTier.Full;
        }
        else
        {
            tier = SessionTrustTier.Restricted;
        }

        if (!sessionRegistry.TryCreate(requestedClientId, connectionId, source, tier, out SessionId newSessionId))
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.RateLimited, "The host cannot admit another session right now.", retryable: true);
            return;
        }

        TrustRecord? recheck = trustStore.TryGet(requestedClientId);
        if (recheck?.State == KnownDeviceState.Blocked)
        {
            sessionRegistry.Invalidate(newSessionId, connectionId);
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Blocked, "This client is blocked.");
            return;
        }

        if (source == SessionAuthenticationSource.TrustedDeviceCredential && recheck?.State == KnownDeviceState.Revoked)
        {
            sessionRegistry.Invalidate(newSessionId, connectionId);
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Revoked, "This client's trust has been revoked.");
            return;
        }

        if (source == SessionAuthenticationSource.TrustedDeviceCredential &&
            (recheck is null || recheck.State != KnownDeviceState.Trusted ||
             !CredentialHasher.FixedTimeEquals(recheck.CredentialVerifier, CredentialHasher.Hash(credential!))))
        {
            // The authoritative record was deleted (Factory Reset), demoted, or its verifier rotated
            // between the initial check above and this point. A deleted record is classified the same
            // as a never-paired identity, not Blocked/Revoked, per this method's own documented
            // Factory Reset contract.
            sessionRegistry.Invalidate(newSessionId, connectionId);
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Unauthenticated, "Authentication failed.");
            return;
        }

        if (!TryClaimAdmission())
        {
            sessionRegistry.Invalidate(newSessionId, connectionId);
            return;
        }

        if (!sessionRegistry.TryFinalizeAdmission(newSessionId, connectionId))
        {
            // TryFinalizeAdmission is the sole linearization point between this admission and a
            // concurrent unconditional invalidation (Factory Reset): an invalidation that reached the
            // registry before this call decides the outcome here, so this branch means the session is
            // already gone and must not be admitted. TryClaimAdmission already consumed this
            // connection's one-shot admission outcome (deliberately never reset back to Pending, so a
            // concurrently racing deadline task can never mistake this for still-pending admission),
            // so this connection can never complete admission again; close it explicitly rather than
            // leaving it open with no path to ever being torn down.
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.RateLimited, "The host cannot admit another session right now.", retryable: true);
            connectionContext.RequestClose();
            return;
        }

        Admit(connectionContext, envelope, requestedClientId, newSessionId, source, tier);
    }

    /// <summary>
    /// Atomically claims this connection's admission outcome. Returns <see langword="false"/> when
    /// the pre-authentication deadline already claimed it first -- the deadline is already closing
    /// this connection, so the caller must not proceed with admission.
    /// </summary>
    private bool TryClaimAdmission() =>
        Interlocked.CompareExchange(ref admissionOutcome, OutcomeAdmitted, OutcomePending) == OutcomePending;

    /// <summary>
    /// Commits this connection's admission: records the session, cancels the pre-authentication
    /// deadline, and sends <c>hello_ack</c> followed by the unsolicited empty <c>capabilities</c>
    /// advertisement. <paramref name="source"/> -- not the session's trust tier -- decides the wire
    /// <c>clientIdentityKind</c>: a developer-authenticated (<see cref="SessionAuthenticationSource.OneTimeLocalToken"/>)
    /// session is unrestricted (full tier) but still reports <see cref="ClientIdentityKind.Unpaired"/>,
    /// per <c>ai/context/protocol/security.md</c>'s "Hello authentication and session trust tiers".
    /// </summary>
    private void Admit(
        IPublicConnectionContext connectionContext,
        PublicEnvelope helloEnvelope,
        ClientId admittedClientId,
        SessionId newSessionId,
        SessionAuthenticationSource source,
        SessionTrustTier tier)
    {
        CancellationTokenSource? deadlineCts;
        lock (gate)
        {
            admitted = true;
            sessionId = newSessionId;
            trustTier = tier;
            this.admittedClientId = admittedClientId;
            deadlineCts = admissionDeadlineCts;
        }

        deadlineCts?.Cancel();
        pairingCoordinator.NotifyReconnected(admittedClientId);

        ClientIdentityKind identityKind = source == SessionAuthenticationSource.TrustedDeviceCredential
            ? ClientIdentityKind.Paired
            : ClientIdentityKind.Unpaired;
        PlayContextSnapshot snapshot = playContextTracker.GetSnapshot();

        var ackPayload = new HelloAckPayload
        {
            BridgeVersion = Constants.PublicProtocolTransitionalBridgeVersion,
            ClientIdentityKind = identityKind,
        };
        byte[] ackBytes = codec.Encode(
            PublicMessageType.HelloAck,
            NewMessageId(),
            newSessionId.ToString(),
            helloEnvelope.MessageId,
            snapshot.Current?.ToString(),
            admittedClientId.ToString(),
            ackPayload);
        connectionContext.TrySend(ackBytes);

        var capabilitiesPayload = new CapabilitiesPayload { Capabilities = [] };
        byte[] capabilitiesBytes = codec.Encode(
            PublicMessageType.Capabilities,
            NewMessageId(),
            newSessionId.ToString(),
            null,
            snapshot.Current?.ToString(),
            null,
            capabilitiesPayload);
        connectionContext.TrySend(capabilitiesBytes);
    }

    /// <inheritdoc/>
    public void HandleConnectionEnded()
    {
        bool wasAdmitted;
        SessionId currentSessionId;
        ClientId currentClientId;
        CancellationTokenSource? deadlineCts;
        lock (gate)
        {
            wasAdmitted = admitted;
            currentSessionId = sessionId;
            currentClientId = admittedClientId;
            deadlineCts = admissionDeadlineCts;
        }

        deadlineCts?.Cancel();

        if (wasAdmitted)
        {
            sessionRegistry.Invalidate(currentSessionId, connectionId);
            pairingCoordinator.NotifyDisconnected(currentClientId);
        }
    }

    /// <inheritdoc/>
    public Task HandleDisconnectedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Generates a fresh, cryptographically random host-originated message identifier.</summary>
    private static string NewMessageId() => Guid.NewGuid().ToString();
}
