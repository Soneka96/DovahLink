using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Client.Authentication;

/// <summary>
/// Implements the public protocol's authentication and session-admission boundary: decodes and
/// validates the required initial <c>hello</c>, authenticates it through the three approved methods,
/// admits a fresh session with the correct trust tier, enforces the pre-authentication admission
/// deadline, replay protection, and the protocol-violation close policy. Message dispatch beyond
/// admission and authorization -- pairing, liveness, rename, capabilities, and state requests --
/// belongs to a later concept; this type only decides whether a message may proceed.
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

    /// <summary>How long this connection may remain unadmitted before it is closed.</summary>
    private readonly TimeSpan admissionDeadline;

    /// <summary>This connection's own identity, minted once for its entire lifetime.</summary>
    private readonly ConnectionId connectionId = ConnectionId.NewId();

    /// <summary>Guards every mutable field below against the admission-deadline task's own thread.</summary>
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
    /// <see cref="OutcomePending"/>.
    /// </summary>
    private int admissionOutcome;

    /// <summary>Whether a session has been admitted on this connection.</summary>
    private bool admitted;

    /// <summary>This connection's admitted session identity, valid only once <see cref="admitted"/> is <see langword="true"/>.</summary>
    private SessionId sessionId;

    /// <summary>Creates a hello-admission handler for one accepted connection.</summary>
    /// <param name="codec">Decodes and encodes every message this handler sends or receives.</param>
    /// <param name="sessionRegistry">Admits and invalidates this connection's session.</param>
    /// <param name="trustStore">Looks up persisted trust for <c>unpaired</c> and <c>trusted_device_credential</c> hellos.</param>
    /// <param name="tokenAuthenticator">Validates and consumes the <c>one_time_local_token</c> developer-authentication credential.</param>
    /// <param name="credentialThrottle">Throttles failed <c>trusted_device_credential</c> attempts.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto every host-originated envelope.</param>
    /// <param name="clock">The time source used for the protocol-violation window.</param>
    /// <param name="admissionDeadline">How long this connection may remain unadmitted before it is closed. Defaults to <see cref="Constants.PublicHelloAdmissionDeadline"/>.</param>
    public PublicHelloAdmissionHandler(
        IPublicEnvelopeCodec codec,
        ISessionRegistry sessionRegistry,
        ITrustStore trustStore,
        ILocalConnectionTokenAuthenticator tokenAuthenticator,
        ITrustedCredentialFailureThrottle credentialThrottle,
        IPlayContextTracker playContextTracker,
        IClock clock,
        TimeSpan? admissionDeadline = null)
    {
        this.codec = codec;
        this.sessionRegistry = sessionRegistry;
        this.trustStore = trustStore;
        this.tokenAuthenticator = tokenAuthenticator;
        this.credentialThrottle = credentialThrottle;
        this.playContextTracker = playContextTracker;
        this.clock = clock;
        this.admissionDeadline = admissionDeadline ?? Constants.PublicHelloAdmissionDeadline;
    }

    /// <inheritdoc/>
    public void HandleConnectionEstablished(IPublicConnectionContext connection)
    {
        var deadlineCts = new CancellationTokenSource();
        admissionDeadlineCts = deadlineCts;
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
    public Task HandleMessageAsync(IPublicConnectionContext connectionContext, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (!codec.TryDecode(payload, out PublicEnvelope? envelope))
        {
            RecordViolationAndReject(connectionContext, correlationId: null, PublicProtocolErrorCode.MalformedMessage, "The message could not be decoded.");
            return Task.CompletedTask;
        }

        if (!TryRecordMessageId(connectionContext, envelope.MessageId))
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.ReplayedMessage, "This messageId was already used on this session.");
            return Task.CompletedTask;
        }

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
                return Task.CompletedTask;
            }

            HandleHello(connectionContext, envelope);
            return Task.CompletedTask;
        }

        if (envelope.SessionId != currentSessionId.ToString())
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.StaleSession, "This sessionId is not valid on this connection.");
            return Task.CompletedTask;
        }

        // Per-tier message authorization and dispatch beyond this point is a later concept's scope.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records <paramref name="messageId"/> as seen, rejecting a repeat as replay. Closes the
    /// connection once the session-lifetime message bound is reached, per
    /// <c>ai/context/protocol/security.md</c>'s "the bridge closes the session before this bound is
    /// exceeded" -- the message that reaches the bound is still accepted; only a later one is not.
    /// </summary>
    private bool TryRecordMessageId(IPublicConnectionContext connectionContext, string messageId)
    {
        lock (gate)
        {
            if (!seenMessageIds.Add(messageId))
            {
                return false;
            }

            if (seenMessageIds.Count >= Constants.PublicProtocolMaxSessionMessages)
            {
                connectionContext.RequestClose();
            }

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

    /// <summary>Validates <c>auth.token</c>'s required-or-absent shape for the presented method, per <c>protocol/schema/README.md</c>'s "<c>hello</c>" section.</summary>
    private static bool IsValidAuth(HelloAuthPayload auth) => auth.Method switch
    {
        HelloAuthMethod.Unpaired => auth.Token is null,
        HelloAuthMethod.OneTimeLocalToken => !string.IsNullOrEmpty(auth.Token),
        HelloAuthMethod.TrustedDeviceCredential => !string.IsNullOrEmpty(auth.Token),
        _ => false,
    };

    /// <summary>
    /// Authenticates a <c>one_time_local_token</c> hello. Validates the token without consuming it
    /// (<see cref="ILocalConnectionTokenAuthenticator.TryValidate"/>) so a full session slot never
    /// burns a retryable token, and commits consumption only once admission has fully succeeded.
    /// </summary>
    private void HandleOneTimeLocalTokenHello(
        IPublicConnectionContext connectionContext, PublicEnvelope envelope, ClientId requestedClientId, string token)
    {
        if (!tokenAuthenticator.TryValidate(token))
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Unauthenticated, "Authentication failed.");
            return;
        }

        if (!sessionRegistry.TryCreate(requestedClientId, connectionId, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId newSessionId))
        {
            RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.RateLimited, "The host cannot admit another session right now.", retryable: true);
            return;
        }

        if (!TryClaimAdmission())
        {
            sessionRegistry.Invalidate(newSessionId, connectionId);
            return;
        }

        tokenAuthenticator.CommitConsumption();
        Admit(connectionContext, envelope, requestedClientId, newSessionId, SessionAuthenticationSource.OneTimeLocalToken);
    }

    /// <summary>
    /// Authenticates an <c>unpaired</c> or <c>trusted_device_credential</c> hello against persisted
    /// trust, rechecking trust once more after the session slot is reserved so a losing race against
    /// a concurrent administrative Block/Revoke never leaves a session admitted for an identity that
    /// no longer qualifies.
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

            if (record is null || record.State != KnownDeviceState.Trusted)
            {
                RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Unauthenticated, "Authentication failed.");
                return;
            }

            if (!credentialThrottle.IsAllowed())
            {
                RecordViolationAndReject(connectionContext, envelope.MessageId, PublicProtocolErrorCode.Unauthenticated, "Authentication failed.");
                return;
            }

            if (!CredentialHasher.FixedTimeEquals(record.CredentialVerifier, CredentialHasher.Hash(credential!)))
            {
                credentialThrottle.RecordFailure();
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

        if (!TryClaimAdmission())
        {
            sessionRegistry.Invalidate(newSessionId, connectionId);
            return;
        }

        Admit(connectionContext, envelope, requestedClientId, newSessionId, source);
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
        IPublicConnectionContext connectionContext, PublicEnvelope helloEnvelope, ClientId admittedClientId, SessionId newSessionId, SessionAuthenticationSource source)
    {
        lock (gate)
        {
            admitted = true;
            sessionId = newSessionId;
        }

        admissionDeadlineCts?.Cancel();

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
        admissionDeadlineCts?.Cancel();

        bool wasAdmitted;
        SessionId currentSessionId;
        lock (gate)
        {
            wasAdmitted = admitted;
            currentSessionId = sessionId;
        }

        if (wasAdmitted)
        {
            sessionRegistry.Invalidate(currentSessionId, connectionId);
        }
    }

    /// <inheritdoc/>
    public Task HandleDisconnectedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Generates a fresh, cryptographically random host-originated message identifier.</summary>
    private static string NewMessageId() => Guid.NewGuid().ToString();
}
