using System.Text;
using System.Text.Json;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Client.Dispatch;

/// <summary>Tests for <see cref="ClientMessageDispatcher"/>.</summary>
public class ClientMessageDispatcherTests
{
    private static readonly PublicEnvelopeCodec Codec = new();

    // ---- ping ----

    /// <summary>Verifies that a well-formed ping is answered with pong, correlated to the request.</summary>
    [Fact]
    public async Task DispatchAsync_Ping_SendsPong()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.Ping, "msg-1", "session-1", new EmptyPayload());
        SessionId sessionId = SessionId.NewId();

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), sessionId, connection, envelope, CancellationToken.None);

        (PublicEnvelope pongEnvelope, EmptyPayload _) = DecodeSent<EmptyPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicMessageType.Pong, pongEnvelope.MessageType);
        Assert.Equal("msg-1", pongEnvelope.CorrelationId);
        Assert.Equal(sessionId.ToString(), pongEnvelope.SessionId);
        Assert.False(result.IsProtocolViolation);
        Assert.False(result.UpgradeToFullTrust);
    }

    /// <summary>Verifies that a response is stamped with the tracker's current play-context snapshot, not a fabricated or default value.</summary>
    [Fact]
    public async Task DispatchAsync_StampsCurrentPlayContextSnapshot()
    {
        var playContextTracker = new FakePlayContextTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        playContextTracker.NotifyTransition(playContextId);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, playContextTracker: playContextTracker);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.Ping, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (PublicEnvelope pongEnvelope, _) = DecodeSent<EmptyPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(playContextId.ToString(), pongEnvelope.PlayContextId);
    }

    /// <summary>Verifies that a malformed ping sends a malformed_message error and reports a protocol violation.</summary>
    [Fact]
    public async Task DispatchAsync_MalformedPing_SendsErrorAndReportsViolation()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildRawEnvelope("ping", "msg-1", "session-1", """{"unexpectedField":true}""");

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (PublicEnvelope errorEnvelope, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicMessageType.Error, errorEnvelope.MessageType);
        Assert.Equal("msg-1", errorEnvelope.CorrelationId);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.True(result.IsProtocolViolation);
    }

    // ---- rename_request ----

    /// <summary>Verifies that a successful rename sends the renamed outcome with the new display name.</summary>
    [Fact]
    public async Task DispatchAsync_RenameRequest_SendsRenamedOutcome()
    {
        var trustAdminService = new FakeTrustAdminService();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, trustAdminService: trustAdminService);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        SessionId sessionId = SessionId.NewId();
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.RenameRequest, "msg-1", "session-1", new RenameRequestPayload { DisplayName = "New Name" });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            clientId, sessionId, connection, envelope, CancellationToken.None);

        Assert.Equal((clientId, "New Name"), trustAdminService.LastRenameCall);
        (PublicEnvelope outcomeEnvelope, RenameOutcomePayload outcome) = DecodeSent<RenameOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicMessageType.RenameOutcome, outcomeEnvelope.MessageType);
        Assert.Equal("msg-1", outcomeEnvelope.CorrelationId);
        Assert.Equal(sessionId.ToString(), outcomeEnvelope.SessionId);
        Assert.Equal(RenameOutcomeWireValue.Renamed, outcome.Outcome);
        Assert.Equal("New Name", outcome.DisplayName);
        Assert.False(result.IsProtocolViolation);
    }

    /// <summary>Verifies that an empty display name -- which clears the name -- is accepted and echoed back empty.</summary>
    [Fact]
    public async Task DispatchAsync_RenameRequestWithEmptyDisplayName_SendsRenamedOutcomeWithEmptyName()
    {
        var trustAdminService = new FakeTrustAdminService();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, trustAdminService: trustAdminService);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.RenameRequest, "msg-1", "session-1", new RenameRequestPayload { DisplayName = string.Empty });

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, RenameOutcomePayload outcome) = DecodeSent<RenameOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(RenameOutcomeWireValue.Renamed, outcome.Outcome);
        Assert.Equal(string.Empty, outcome.DisplayName);
    }

    /// <summary>Verifies that a malformed rename_request sends a malformed_message error, reports a violation, and never reaches the trust service.</summary>
    [Fact]
    public async Task DispatchAsync_MalformedRenameRequest_SendsErrorAndReportsViolationWithoutCallingTrustService()
    {
        var trustAdminService = new FakeTrustAdminService();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, trustAdminService: trustAdminService);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildRawEnvelope("rename_request", "msg-1", "session-1", "{}");

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.True(result.IsProtocolViolation);
        Assert.Null(trustAdminService.LastRenameCall);
    }

    /// <summary>Verifies that an invalid display name (rejected by the trust service) maps to invalid_display_name, not a raw exception.</summary>
    [Fact]
    public async Task DispatchAsync_RenameRequestInvalidDisplayName_SendsInvalidDisplayNameOutcome()
    {
        var trustAdminService = new FakeTrustAdminService { ThrowOnRename = new ArgumentException("too long") };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, trustAdminService: trustAdminService);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.RenameRequest, "msg-1", "session-1", new RenameRequestPayload { DisplayName = "Bad\nName" });

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, RenameOutcomePayload outcome) = DecodeSent<RenameOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(RenameOutcomeWireValue.InvalidDisplayName, outcome.Outcome);
        Assert.Null(outcome.DisplayName);
    }

    /// <summary>Verifies that an unrecognized identity (KeyNotFoundException) maps to not_trusted, never a raw exception.</summary>
    [Fact]
    public async Task DispatchAsync_RenameRequestUnknownIdentity_SendsNotTrustedOutcome()
    {
        var trustAdminService = new FakeTrustAdminService { ThrowOnRename = new KeyNotFoundException("no known device") };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, trustAdminService: trustAdminService);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.RenameRequest, "msg-1", "session-1", new RenameRequestPayload { DisplayName = "New Name" });

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, RenameOutcomePayload outcome) = DecodeSent<RenameOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(RenameOutcomeWireValue.NotTrusted, outcome.Outcome);
    }

    /// <summary>Verifies that a not-currently-trusted identity (InvalidOperationException) maps to not_trusted, never a raw exception.</summary>
    [Fact]
    public async Task DispatchAsync_RenameRequestNotCurrentlyTrusted_SendsNotTrustedOutcome()
    {
        var trustAdminService = new FakeTrustAdminService { ThrowOnRename = new InvalidOperationException("not trusted") };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, trustAdminService: trustAdminService);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.RenameRequest, "msg-1", "session-1", new RenameRequestPayload { DisplayName = "New Name" });

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, RenameOutcomePayload outcome) = DecodeSent<RenameOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(RenameOutcomeWireValue.NotTrusted, outcome.Outcome);
    }

    /// <summary>Verifies that an unexpected failure (for example a persistence error) maps to a safe, redacted, retryable internal_error rather than exposing the raw exception.</summary>
    [Fact]
    public async Task DispatchAsync_RenameRequestUnexpectedFailure_SendsRedactedRetryableInternalError()
    {
        var trustAdminService = new FakeTrustAdminService { ThrowOnRename = new IOException("disk full, path C:\\secret\\trust-store.dat") };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, trustAdminService: trustAdminService);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.RenameRequest, "msg-1", "session-1", new RenameRequestPayload { DisplayName = "New Name" });

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.InternalError, error.Code);
        Assert.True(error.Retryable);
        Assert.DoesNotContain("secret", error.Message);
        Assert.DoesNotContain("disk full", error.Message);
    }

    /// <summary>Verifies that cancellation propagates rather than being swallowed into an internal_error response.</summary>
    [Fact]
    public async Task DispatchAsync_RenameRequestCancelled_PropagatesCancellation()
    {
        var trustAdminService = new FakeTrustAdminService { ThrowOnRename = new OperationCanceledException() };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, trustAdminService: trustAdminService);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.RenameRequest, "msg-1", "session-1", new RenameRequestPayload { DisplayName = "New Name" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, cancellation.Token));
        Assert.Empty(fakeConnection.SentPayloads);
    }

    // ---- pairing_request ----

    /// <summary>Verifies that a new challenge the adapter accepts is reported available with its full remaining lifetime.</summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestNewChallenge_AdapterAccepts_SendsAvailable()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptDisplay = true };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (PublicEnvelope statusEnvelope, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicMessageType.PairingStatus, statusEnvelope.MessageType);
        Assert.Equal("msg-1", statusEnvelope.CorrelationId);
        Assert.Equal(PairingStatusWireState.Available, status.State);
        Assert.Equal(300, status.ExpiresInSeconds);
        Assert.Single(adapterNotifier.DisplayedCodes);
    }

    /// <summary>
    /// Verifies that a new challenge the adapter rejects is reported unavailable and rolled back -- a
    /// following request starts a brand-new challenge rather than resuming a falsely advertised one.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestNewChallenge_AdapterRejects_SendsUnavailableAndRollsBackChallenge()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptDisplay = false };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.Unavailable, status.State);
        Assert.Null(status.ExpiresInSeconds);
        Assert.Equal(PairingStartOutcome.Started, pairingCoordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that resuming an owned active challenge reports in_progress with its remaining seconds and never re-displays.</summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestResumedActiveChallenge_SendsInProgressWithSeconds()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        pairingCoordinator.BeginPairing(clientId);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.InProgress, status.State);
        Assert.Equal(300, status.ExpiresInSeconds);
        Assert.Empty(adapterNotifier.DisplayedCodes);
    }

    /// <summary>Verifies that resuming an owned pending credential -- its code already consumed -- reports in_progress with a null remaining figure.</summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestResumedPendingCredentialOnly_SendsInProgressWithNullSeconds()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.InProgress, status.State);
        Assert.Null(status.ExpiresInSeconds);
    }

    /// <summary>Verifies that a different client's active challenge is reported other_device_pairing with expiresInSeconds omitted entirely.</summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestOtherDeviceActive_SendsOtherDevicePairingWithNoExpiresKey()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        pairingCoordinator.BeginPairing(ClientId.NewId());
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        byte[] sent = Assert.Single(fakeConnection.SentPayloads);
        using JsonDocument document = JsonDocument.Parse(sent);
        Assert.False(document.RootElement.GetProperty("payload").TryGetProperty("expiresInSeconds", out _));
        (_, PairingStatusOtherDevicePayload status) = DecodeSent<PairingStatusOtherDevicePayload>(sent);
        Assert.Equal(PairingStatusWireState.OtherDevicePairing, status.State);
    }

    /// <summary>Verifies that secure code generation failure is reported as a redacted, retryable internal_error, not an outcome state.</summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestGeneratorFails_SendsRetryableInternalError()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock, pairingCodeGenerator: () => null);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.InternalError, error.Code);
        Assert.True(error.Retryable);
    }

    /// <summary>Verifies that a malformed pairing_request sends a malformed_message error and reports a protocol violation.</summary>
    [Fact]
    public async Task DispatchAsync_MalformedPairingRequest_SendsErrorAndReportsViolation()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildRawEnvelope("pairing_request", "msg-1", "session-1", """{"unexpectedField":true}""");

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.True(result.IsProtocolViolation);
    }

    // ---- pairing_confirm ----

    /// <summary>Verifies that a correct code sends credential_issued with the issued credential and display name.</summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmCorrectCode_SendsCredentialIssued()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1",
            new PairingConfirmPayload { Code = start.Challenge!.Code, DisplayName = "Living Room PC" });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (PublicEnvelope outcomeEnvelope, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicMessageType.PairingOutcome, outcomeEnvelope.MessageType);
        Assert.Equal("msg-1", outcomeEnvelope.CorrelationId);
        Assert.Equal(PairingOutcomeWireValue.CredentialIssued, outcome.Outcome);
        Assert.Equal(32, outcome.Credential!.Length);
        Assert.Equal("Living Room PC", outcome.DisplayName);
        Assert.Null(outcome.ShortId);
    }

    /// <summary>
    /// Verifies that a first wrong attempt -- eligible for auto-renotify -- sends invalid and requests
    /// a best-effort incorrect-code redisplay of the still-active code, never disclosed in the response.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmFirstWrongAttempt_SendsInvalidAndRequestsIncorrectCodeRedisplay()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = "000000" });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.Invalid, outcome.Outcome);
        Assert.Null(outcome.Credential);
        await WaitUntilAsync(() => adapterNotifier.IncorrectCodeNotifications.Count > 0);
        Assert.Equal([start.Challenge!.Code], adapterNotifier.IncorrectCodeNotifications);
    }

    /// <summary>
    /// Verifies that a second wrong attempt still within the auto-renotify cooldown sends invalid
    /// without requesting a second redisplay.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmSecondWrongAttemptWithinAutoRenotifyCooldown_DoesNotRequestRedisplay()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.ConfirmCode(clientId, "000000", null);
        clock.Advance(TimeSpan.FromSeconds(1));
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = "000000" });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.Invalid, outcome.Outcome);
        Assert.Empty(adapterNotifier.IncorrectCodeNotifications);
    }

    /// <summary>Verifies that confirming after challenge expiry sends expired.</summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmAfterExpiry_SendsExpired()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = start.Challenge!.Code });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.Expired, outcome.Outcome);
        Assert.Null(outcome.Credential);
    }

    /// <summary>Verifies that an expired challenge -- unlike a wrong attempt -- never requests any adapter notification.</summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmAfterExpiry_RequestsNoAdapterNotification()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = start.Challenge!.Code });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        Assert.Empty(adapterNotifier.IncorrectCodeNotifications);
        Assert.Equal(0, adapterNotifier.AttemptsExhaustedCallCount);
    }

    /// <summary>Verifies that an attempt made too soon after the previous one sends pacing_limited with the rounded-up retry wait.</summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmTooSoon_SendsPacingLimitedWithRetryAfterSeconds()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.ConfirmCode(clientId, "000000", null);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = start.Challenge!.Code });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.PacingLimited, outcome.Outcome);
        Assert.Equal(1, outcome.RetryAfterSeconds);
    }

    /// <summary>Verifies that a fractional remaining pacing wait rounds up rather than truncating.</summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmTooSoon_RoundsFractionalRetryAfterUp()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.ConfirmCode(clientId, "000000", null);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = start.Challenge!.Code });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(1, outcome.RetryAfterSeconds);
    }

    /// <summary>Verifies that the fifth wrong attempt sends hard_limit_reached and requests the no-code attempts-exhausted notification.</summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmFifthWrongAttempt_SendsHardLimitReachedAndRequestsAttemptsExhausted()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        pairingCoordinator.BeginPairing(clientId);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            pairingCoordinator.ConfirmCode(clientId, "000000", null);
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = "000000" });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.HardLimitReached, outcome.Outcome);
        await WaitUntilAsync(() => adapterNotifier.AttemptsExhaustedCallCount > 0);
    }

    /// <summary>Verifies that secure credential generation failure is reported as a redacted, retryable internal_error, not an outcome state.</summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmGeneratorFails_SendsRetryableInternalError()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock, credentialGenerator: () => null);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = start.Challenge!.Code });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.InternalError, error.Code);
        Assert.True(error.Retryable);
    }

    /// <summary>Verifies that a malformed pairing_confirm sends a malformed_message error and reports a protocol violation.</summary>
    [Fact]
    public async Task DispatchAsync_MalformedPairingConfirm_SendsErrorAndReportsViolation()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildRawEnvelope("pairing_confirm", "msg-1", "session-1", "{}");

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.True(result.IsProtocolViolation);
    }

    /// <summary>
    /// Verifies that a display name the pairing coordinator itself rejects (over the 64-byte limit or
    /// containing a control character) -- a validation layer past envelope decoding, since the wire
    /// payload places no such constraint -- is mapped to a correlated malformed_message error rather
    /// than propagating the coordinator's ArgumentException uncaught.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmInvalidDisplayName_SendsMalformedMessageAndReportsViolation()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1",
            new PairingConfirmPayload { Code = start.Challenge!.Code, DisplayName = "Bad\nName" });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.True(result.IsProtocolViolation);
    }

    /// <summary>
    /// Verifies that a fault from a best-effort adapter notification never surfaces to the dispatch
    /// caller: the pairing_outcome response still sends normally and no exception propagates.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmAdapterNotificationFaults_DoesNotPropagateOrBlockResponse()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier { ThrowOnNotifyCodeIncorrect = new InvalidOperationException("adapter unavailable") };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        pairingCoordinator.BeginPairing(clientId);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = "000000" });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.Invalid, outcome.Outcome);
        Assert.False(result.IsProtocolViolation);
        await WaitUntilAsync(() => adapterNotifier.IncorrectCodeNotifications.Count > 0);
    }

    // ---- unhandled message types ----

    /// <summary>
    /// Verifies that a pairing_* message type not yet mapped by this step sends nothing and reports no
    /// side effect -- authorized by the connection handler's allowlist, mapped by a later step.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_NotYetMappedPairingMessageType_SendsNothing()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingAck, "msg-1", "session-1", new EmptyPayload());

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        Assert.Empty(fakeConnection.SentPayloads);
        Assert.False(result.IsProtocolViolation);
        Assert.False(result.UpgradeToFullTrust);
    }

    // ---- Helpers ----

    /// <summary>Polls <paramref name="condition"/> until it becomes true, failing the test if it does not within 5 seconds.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met within the expected time.");
            await Task.Delay(10);
        }
    }

    /// <summary>Encodes then decodes a payload into a real <see cref="PublicEnvelope"/>, matching what the connection handler would hand the dispatcher.</summary>
    private static PublicEnvelope BuildEnvelope<TPayload>(PublicMessageType messageType, string messageId, string sessionId, TPayload payload)
    {
        byte[] bytes = Codec.Encode(messageType, messageId, sessionId, null, null, null, payload);
        Assert.True(Codec.TryDecode(bytes, out PublicEnvelope? envelope));
        return envelope!;
    }

    /// <summary>Decodes a raw payload JSON string into a real <see cref="PublicEnvelope"/> whose payload has not yet been shape-validated, for malformed-payload tests.</summary>
    private static PublicEnvelope BuildRawEnvelope(string wireMessageType, string messageId, string sessionId, string payloadJson)
    {
        string json = $$"""
            {"messageType":"{{wireMessageType}}","messageId":"{{messageId}}","sessionId":"{{sessionId}}","correlationId":null,"payload":{{payloadJson}},"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));
        return envelope!;
    }

    /// <summary>Decodes a sent wire message's envelope and typed payload, failing the test if either step does not succeed.</summary>
    private static (PublicEnvelope Envelope, TPayload Payload) DecodeSent<TPayload>(byte[] bytes) where TPayload : class
    {
        Assert.True(Codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope, out TPayload? payload));
        return (envelope, payload);
    }
}
