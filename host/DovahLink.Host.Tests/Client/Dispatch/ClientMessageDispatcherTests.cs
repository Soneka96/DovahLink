using System.Text;
using System.Text.Json;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;

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

    /// <summary>
    /// Verifies that an exception from the adapter's initial-display notification still rolls back the
    /// reservation rather than leaving it uncommitted and occupying the pairing slot: exception safety
    /// comes from a <c>finally</c> guarded on whether the commit actually succeeded, not from the
    /// explicit rollback call on the ordinary rejection path alone.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestNewChallenge_AdapterThrows_RollsBackReservation()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier { ThrowOnNotifyCodeAvailable = new InvalidOperationException("adapter unavailable") };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None));

        Assert.Empty(fakeConnection.SentPayloads);
        Assert.Equal(PairingStartOutcome.Started, pairingCoordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>
    /// Verifies the same exception-safe rollback guarantee as
    /// <see cref="DispatchAsync_PairingRequestNewChallenge_AdapterThrows_RollsBackReservation"/> when
    /// the adapter await is cancelled instead of faulted.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestNewChallenge_AdapterCancelled_RollsBackReservation()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier { ThrowOnNotifyCodeAvailable = new OperationCanceledException("connection closing") };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None));

        Assert.Empty(fakeConnection.SentPayloads);
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
        PairingStartResult started = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, started.Challenge!.Id);
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
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
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

    /// <summary>
    /// Verifies that resuming while a concurrent operation raced ownership away to a different client
    /// reports other_device_pairing rather than folding into unavailable -- the exhaustive mapping fix
    /// for <see cref="PairingStatusKind.OtherDeviceActive"/> in the resumed-snapshot path. The real
    /// coordinator cannot deterministically reach <see cref="PairingStartOutcome.Resumed"/> together
    /// with a snapshot of <see cref="PairingStatusKind.OtherDeviceActive"/> for the same client without
    /// a genuine cross-thread race, so this exercises the dispatcher's own mapping directly through a
    /// configurable test double.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestResumedButRacedToOtherDeviceActive_SendsOtherDevicePairing()
    {
        var pairingCoordinator = new FakePairingCoordinator
        {
            BeginPairingResult = new PairingStartResult(PairingStartOutcome.Resumed, null),
            StatusSnapshotResult = new PairingStatusSnapshot(PairingStatusKind.OtherDeviceActive, null),
        };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        byte[] sent = Assert.Single(fakeConnection.SentPayloads);
        using JsonDocument document = JsonDocument.Parse(sent);
        Assert.False(document.RootElement.GetProperty("payload").TryGetProperty("expiresInSeconds", out _));
        (_, PairingStatusOtherDevicePayload status) = DecodeSent<PairingStatusOtherDevicePayload>(sent);
        Assert.Equal(PairingStatusWireState.OtherDevicePairing, status.State);
    }

    /// <summary>
    /// Verifies the other race-only combination in the resumed-snapshot switch, symmetric with
    /// <see cref="DispatchAsync_PairingRequestResumedButRacedToOtherDeviceActive_SendsOtherDevicePairing"/>:
    /// when the same ownership race instead clears this client's own operation entirely before the
    /// snapshot is taken, <see cref="PairingStatusKind.Idle"/> still reports <c>unavailable</c>,
    /// exercising that explicit case arm directly rather than relying only on the initial-request path.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestResumedButRacedToIdle_SendsUnavailable()
    {
        var pairingCoordinator = new FakePairingCoordinator
        {
            BeginPairingResult = new PairingStartResult(PairingStartOutcome.Resumed, null),
            StatusSnapshotResult = new PairingStatusSnapshot(PairingStatusKind.Idle, null),
        };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.Unavailable, status.State);
        Assert.Null(status.ExpiresInSeconds);
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

    /// <summary>
    /// Verifies that a stale adapter acceptance arriving after the reservation it was displaying was
    /// cancelled can never advertise a challenge that no longer exists, and leaves the client free to
    /// start a fresh challenge.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestDisplayPendingThenCancelled_LateSuccessCannotAdvertiseAvailable()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptDisplay = true, BeforeNotifyCodeAvailable = () => releaseGate.Task };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        Task<ClientDispatchResult> dispatchTask = dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);
        await WaitUntilAsync(() => adapterNotifier.DisplayedCodes.Count > 0);

        pairingCoordinator.Cancel(clientId);
        releaseGate.SetResult();
        await dispatchTask;

        (_, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.Unavailable, status.State);
        Assert.Equal(PairingStartOutcome.Started, pairingCoordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>
    /// Verifies that a stale adapter rejection arriving after the reservation it was displaying was
    /// replaced by a fresh challenge for the same client can never roll back that replacement.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestDisplayPendingThenReplacement_LateFailureCannotCancelReplacement()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptDisplay = false, BeforeNotifyCodeAvailable = () => releaseGate.Task };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        Task<ClientDispatchResult> dispatchTask = dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);
        await WaitUntilAsync(() => adapterNotifier.DisplayedCodes.Count > 0);

        pairingCoordinator.Cancel(clientId);
        PairingStartResult replacement = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id);
        releaseGate.SetResult();
        await dispatchTask;

        (_, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.Unavailable, status.State);
        Assert.Equal(replacement.Challenge, pairingCoordinator.GetStatusSnapshot(clientId).Challenge);
    }

    /// <summary>
    /// Verifies that a concurrent <c>pairing_request</c> for the same client while the first
    /// reservation's display acknowledgement is still pending reports <c>unavailable</c> -- an
    /// uncommitted reservation has never actually been shown to the client, so it is not a displayable
    /// challenge, and it must never be reported as <c>in_progress</c> the way an owned pending
    /// credential legitimately is.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestUncommittedInitialDisplay_CannotBeResumedAsInProgress()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptDisplay = true, BeforeNotifyCodeAvailable = () => releaseGate.Task };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var firstConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        var secondConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        Task<ClientDispatchResult> firstDispatch = dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), new PublicConnectionContext(firstConnection), envelope, CancellationToken.None);
        await WaitUntilAsync(() => adapterNotifier.DisplayedCodes.Count > 0);

        await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), new PublicConnectionContext(secondConnection), envelope, CancellationToken.None);

        (_, PairingStatusPayload secondStatus) = DecodeSent<PairingStatusPayload>(Assert.Single(secondConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.Unavailable, secondStatus.State);
        Assert.Null(secondStatus.ExpiresInSeconds);

        releaseGate.SetResult();
        await firstDispatch;
    }

    /// <summary>
    /// Verifies that an administrative <see cref="IPairingCoordinator.CancelAll"/> racing a pending
    /// initial-display acknowledgement leaves the request reporting <c>unavailable</c> rather than a
    /// stale <c>available</c> for a challenge the administrative action already cleared.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestAdministrativeCancelAllDuringDisplayAcknowledgement_ReportsUnavailable()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptDisplay = true, BeforeNotifyCodeAvailable = () => releaseGate.Task };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        Task<ClientDispatchResult> dispatchTask = dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);
        await WaitUntilAsync(() => adapterNotifier.DisplayedCodes.Count > 0);

        pairingCoordinator.CancelAll();
        releaseGate.SetResult();
        await dispatchTask;

        (_, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.Unavailable, status.State);
    }

    /// <summary>
    /// Verifies that a disconnect/reconnect blip for the reserving client while its display
    /// acknowledgement is still pending does not corrupt the reservation: the commit still succeeds
    /// normally once the adapter accepts.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRequestDisconnectReconnectDuringDisplayAcknowledgement_DoesNotCorruptState()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptDisplay = true, BeforeNotifyCodeAvailable = () => releaseGate.Task };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        Task<ClientDispatchResult> dispatchTask = dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);
        await WaitUntilAsync(() => adapterNotifier.DisplayedCodes.Count > 0);

        pairingCoordinator.NotifyDisconnected(clientId);
        pairingCoordinator.NotifyReconnected(clientId);
        releaseGate.SetResult();
        await dispatchTask;

        (_, PairingStatusPayload status) = DecodeSent<PairingStatusPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingStatusWireState.Available, status.State);
        Assert.Equal(300, status.ExpiresInSeconds);
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
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
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
        pairingCoordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
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
    /// Verifies that a first wrong attempt against a challenge whose initial display has never
    /// committed never requests an incorrect-code redisplay: the code was never actually shown, so
    /// there is nothing displayed to redisplay, even though the coordinator itself allows confirming
    /// an uncommitted reservation's code.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingConfirmFirstWrongAttemptOnUncommittedReservation_DoesNotRequestRedisplay()
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
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = "000000" });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.Invalid, outcome.Outcome);
        Assert.Empty(adapterNotifier.IncorrectCodeNotifications);
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
        BeginAndDisplayPairing(pairingCoordinator, clientId);
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
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
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
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
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
        BeginAndDisplayPairing(pairingCoordinator, clientId);
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
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
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
        PairingStartResult started = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, started.Challenge!.Id);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingConfirm, "msg-1", "session-1", new PairingConfirmPayload { Code = "000000" });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.Invalid, outcome.Outcome);
        Assert.False(result.IsProtocolViolation);
        await WaitUntilAsync(() => adapterNotifier.IncorrectCodeNotifications.Count > 0);
    }

    // ---- pairing_ack ----

    /// <summary>Verifies that a matching pending credential sends trusted and signals a session upgrade.</summary>
    [Fact]
    public async Task DispatchAsync_PairingAckMatchingPendingCredential_SendsTrustedAndSignalsUpgrade()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
        PairingConfirmationResult issued = pairingCoordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingAck, "msg-1", "session-1", new PairingAckPayload { Credential = issued.Credential! });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (PublicEnvelope outcomeEnvelope, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicMessageType.PairingOutcome, outcomeEnvelope.MessageType);
        Assert.Equal("msg-1", outcomeEnvelope.CorrelationId);
        Assert.Equal(PairingOutcomeWireValue.Trusted, outcome.Outcome);
        Assert.Equal(issued.Credential, outcome.Credential);
        Assert.Equal(5, outcome.ShortId!.Length);
        Assert.Equal("Living Room PC", outcome.DisplayName);
        Assert.True(result.UpgradeToFullTrust);
    }

    /// <summary>
    /// Verifies that an idempotent retry of an already-committed credential sends already_trusted and
    /// still signals a session upgrade -- per security.md's trust-tier upgrade point covering both
    /// outcomes, since both prove the presented credential is genuinely, currently trusted.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingAckRepeatedCredential_SendsAlreadyTrustedAndSignalsUpgrade()
    {
        var trustStore = new FakeTrustStore();
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(trustStore, clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
        PairingConfirmationResult issued = pairingCoordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        await pairingCoordinator.CommitPendingAsync(clientId, issued.Credential!);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingAck, "msg-1", "session-1", new PairingAckPayload { Credential = issued.Credential! });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.AlreadyTrusted, outcome.Outcome);
        Assert.Equal(issued.Credential, outcome.Credential);
        Assert.Equal(5, outcome.ShortId!.Length);
        Assert.Equal("Living Room PC", outcome.DisplayName);
        Assert.True(result.UpgradeToFullTrust);
    }

    /// <summary>Verifies that no matching pending credential sends pending_not_found without signalling an upgrade.</summary>
    [Fact]
    public async Task DispatchAsync_PairingAckNoPendingCredential_SendsPendingNotFound()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingAck, "msg-1", "session-1", new PairingAckPayload { Credential = "deadbeefdeadbeefdeadbeefdeadbeef" });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.PendingNotFound, outcome.Outcome);
        Assert.Null(outcome.Credential);
        Assert.Null(outcome.ShortId);
        Assert.Null(outcome.DisplayName);
        Assert.False(result.UpgradeToFullTrust);
    }

    /// <summary>Verifies that an administrative trust mutation fencing the pending credential sends pairing_invalidated.</summary>
    [Fact]
    public async Task DispatchAsync_PairingAckAfterTrustMutation_SendsPairingInvalidated()
    {
        var trustStore = new FakeTrustStore();
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(trustStore, clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
        PairingConfirmationResult issued = pairingCoordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        await trustStore.UpsertAsync(new TrustRecord(ClientId.NewId(), "12345", "Other", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingAck, "msg-1", "session-1", new PairingAckPayload { Credential = issued.Credential! });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.PairingInvalidated, outcome.Outcome);
        Assert.Null(outcome.Credential);
        Assert.False(result.UpgradeToFullTrust);
    }

    /// <summary>Verifies that persistence failure is reported as a redacted, retryable internal_error without signalling an upgrade.</summary>
    [Fact]
    public async Task DispatchAsync_PairingAckPersistenceFails_SendsRetryableInternalError()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(trustStore, clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
        PairingConfirmationResult issued = pairingCoordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingAck, "msg-1", "session-1", new PairingAckPayload { Credential = issued.Credential! });

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.InternalError, error.Code);
        Assert.True(error.Retryable);
        Assert.False(result.UpgradeToFullTrust);
    }

    /// <summary>Verifies that exhausting short-id candidates is reported as a redacted, retryable internal_error.</summary>
    [Fact]
    public async Task DispatchAsync_PairingAckShortIdGenerationFails_SendsRetryableInternalError()
    {
        var trustStore = new FakeTrustStore();
        trustStore.Seed(new TrustRecord(ClientId.NewId(), "12345", "Existing", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(trustStore, clock, shortIdGenerator: () => "12345");
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
        PairingConfirmationResult issued = pairingCoordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingAck, "msg-1", "session-1", new PairingAckPayload { Credential = issued.Credential! });

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.InternalError, error.Code);
        Assert.True(error.Retryable);
    }

    /// <summary>Verifies that a malformed pairing_ack sends a malformed_message error and reports a protocol violation.</summary>
    [Fact]
    public async Task DispatchAsync_MalformedPairingAck_SendsErrorAndReportsViolation()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildRawEnvelope("pairing_ack", "msg-1", "session-1", "{}");

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.True(result.IsProtocolViolation);
    }

    /// <summary>Verifies that cancellation propagates rather than being swallowed into an internal_error response.</summary>
    [Fact]
    public async Task DispatchAsync_PairingAckCancelled_PropagatesCancellation()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
        PairingConfirmationResult issued = pairingCoordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        PublicEnvelope envelope = BuildEnvelope(
            PublicMessageType.PairingAck, "msg-1", "session-1", new PairingAckPayload { Credential = issued.Credential! });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, cancellation.Token));
        Assert.Empty(fakeConnection.SentPayloads);
    }

    // ---- pairing_renotify ----

    /// <summary>Verifies that an eligible redisplay the adapter accepts sends renotified and commits the cooldown.</summary>
    [Fact]
    public async Task DispatchAsync_PairingRenotifyEligible_AdapterAccepts_SendsRenotifiedAndCommitsCooldown()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptRedisplay = true };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRenotify, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (PublicEnvelope outcomeEnvelope, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicMessageType.PairingOutcome, outcomeEnvelope.MessageType);
        Assert.Equal("msg-1", outcomeEnvelope.CorrelationId);
        Assert.Equal(PairingOutcomeWireValue.Renotified, outcome.Outcome);
        Assert.Equal([start.Challenge!.Code], adapterNotifier.RedisplayedCodes);
        // Cooldown was committed: a second immediate peek reports Cooldown, not Renotified.
        Assert.Equal(PairingRenotifyOutcome.Cooldown, pairingCoordinator.TryRenotify(clientId).Outcome);
    }

    /// <summary>Verifies that an active cooldown sends renotify_cooldown without ever contacting the adapter.</summary>
    [Fact]
    public async Task DispatchAsync_PairingRenotifyDuringCooldown_SendsRenotifyCooldownWithoutContactingAdapter()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult started = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, started.Challenge!.Id);
        pairingCoordinator.CommitRenotify(clientId, started.Challenge!.Id);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRenotify, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.RenotifyCooldown, outcome.Outcome);
        Assert.Equal(5, outcome.RetryAfterSeconds);
        Assert.Empty(adapterNotifier.RedisplayedCodes);
    }

    /// <summary>Verifies that a client owning no active challenge sends already_idle without contacting the adapter.</summary>
    [Fact]
    public async Task DispatchAsync_PairingRenotifyNoOwnedChallenge_SendsAlreadyIdleWithoutContactingAdapter()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier();
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRenotify, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.AlreadyIdle, outcome.Outcome);
        Assert.Empty(adapterNotifier.RedisplayedCodes);
    }

    /// <summary>
    /// Verifies that a client whose challenge reservation's initial display has not yet committed
    /// sends already_idle without contacting the adapter: the reservation has never actually been
    /// shown, so there is nothing yet to redisplay.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRenotifyUncommittedReservation_SendsAlreadyIdleWithoutContactingAdapter()
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
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRenotify, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.AlreadyIdle, outcome.Outcome);
        Assert.Empty(adapterNotifier.RedisplayedCodes);
    }

    /// <summary>
    /// Verifies that an adapter rejection sends a retryable internal_error and never commits the
    /// cooldown -- a following renotify remains eligible rather than falsely rate-limited by a
    /// redisplay that never actually happened.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRenotifyAdapterRejects_SendsRetryableErrorAndPreservesEligibility()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptRedisplay = false };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRenotify, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.InternalError, error.Code);
        Assert.True(error.Retryable);
        Assert.Equal(PairingRenotifyOutcome.Renotified, pairingCoordinator.TryRenotify(clientId).Outcome);
    }

    /// <summary>Verifies that a malformed pairing_renotify sends a malformed_message error and reports a protocol violation.</summary>
    [Fact]
    public async Task DispatchAsync_MalformedPairingRenotify_SendsErrorAndReportsViolation()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildRawEnvelope("pairing_renotify", "msg-1", "session-1", """{"unexpectedField":true}""");

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.True(result.IsProtocolViolation);
    }

    /// <summary>
    /// Verifies that a challenge replaced while its redisplay acknowledgement is still pending cannot
    /// have its cooldown consumed by the stale commit -- the replacement remains fully eligible for
    /// its own redisplay afterward.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRenotifyChallengeReplacedDuringAdapterAck_DoesNotCommitReplacementCooldown()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptRedisplay = true, BeforeNotifyRedisplay = () => releaseGate.Task };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRenotify, "msg-1", "session-1", new EmptyPayload());

        Task<ClientDispatchResult> dispatchTask = dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);
        await WaitUntilAsync(() => adapterNotifier.RedisplayedCodes.Count > 0);

        pairingCoordinator.Cancel(clientId);
        PairingStartResult replacement = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id);
        releaseGate.SetResult();
        await dispatchTask;

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.AlreadyIdle, outcome.Outcome);
        Assert.Equal(PairingRenotifyOutcome.Renotified, pairingCoordinator.TryRenotify(clientId).Outcome);
    }

    /// <summary>
    /// Verifies that a stale redisplay acceptance arriving after its challenge was cancelled -- with no
    /// replacement -- reports <c>already_idle</c> rather than a fabricated <c>renotified</c>.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRenotifyLateAckForOldChallenge_DoesNotReportRenotified()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptRedisplay = true, BeforeNotifyRedisplay = () => releaseGate.Task };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRenotify, "msg-1", "session-1", new EmptyPayload());

        Task<ClientDispatchResult> dispatchTask = dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);
        await WaitUntilAsync(() => adapterNotifier.RedisplayedCodes.Count > 0);

        pairingCoordinator.Cancel(clientId);
        releaseGate.SetResult();
        await dispatchTask;

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.AlreadyIdle, outcome.Outcome);
    }

    /// <summary>
    /// Verifies that a stale commit for a cancelled-then-replaced challenge leaves the new challenge
    /// completely untouched and usable: a fresh <c>pairing_renotify</c> for it still succeeds
    /// end-to-end after the earlier race resolves.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PairingRenotifyCancelThenNewChallenge_OldCommitLeavesNewChallengeUntouched()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterNotifier = new FakePairingAdapterNotifier { AcceptRedisplay = true, BeforeNotifyRedisplay = () => releaseGate.Task };
        var dispatcher = Fixtures.BuildClientMessageDispatcher(
            codec: Codec, pairingCoordinator: pairingCoordinator, adapterNotifier: adapterNotifier, clock: clock);
        var firstConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRenotify, "msg-1", "session-1", new EmptyPayload());

        Task<ClientDispatchResult> staleDispatch = dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), new PublicConnectionContext(firstConnection), envelope, CancellationToken.None);
        await WaitUntilAsync(() => adapterNotifier.RedisplayedCodes.Count > 0);

        pairingCoordinator.Cancel(clientId);
        PairingStartResult replacement = pairingCoordinator.BeginPairing(clientId);
        pairingCoordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id);
        releaseGate.SetResult();
        await staleDispatch;

        adapterNotifier.BeforeNotifyRedisplay = null;
        var secondConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        await dispatcher.DispatchAsync(
            clientId, SessionId.NewId(), new PublicConnectionContext(secondConnection), envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(secondConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.Renotified, outcome.Outcome);
    }

    // ---- pairing_cancel ----

    /// <summary>Verifies that cancelling an owned active challenge sends cancelled.</summary>
    [Fact]
    public async Task DispatchAsync_PairingCancelOwnedActiveChallenge_SendsCancelled()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        pairingCoordinator.BeginPairing(clientId);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingCancel, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (PublicEnvelope outcomeEnvelope, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicMessageType.PairingOutcome, outcomeEnvelope.MessageType);
        Assert.Equal("msg-1", outcomeEnvelope.CorrelationId);
        Assert.Equal(PairingOutcomeWireValue.Cancelled, outcome.Outcome);
        Assert.Equal(PairingStartOutcome.Started, pairingCoordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that cancelling an owned pending credential also sends cancelled.</summary>
    [Fact]
    public async Task DispatchAsync_PairingCancelOwnedPendingCredential_SendsCancelled()
    {
        var clock = new FakeClock();
        var pairingCoordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec, pairingCoordinator: pairingCoordinator, clock: clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(pairingCoordinator, clientId);
        pairingCoordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingCancel, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(clientId, SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.Cancelled, outcome.Outcome);
    }

    /// <summary>Verifies that cancelling with no owned pairing operation sends already_idle without pretending anything was cleared.</summary>
    [Fact]
    public async Task DispatchAsync_PairingCancelNothingOwned_SendsAlreadyIdle()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingCancel, "msg-1", "session-1", new EmptyPayload());

        await dispatcher.DispatchAsync(ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, PairingOutcomePayload outcome) = DecodeSent<PairingOutcomePayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PairingOutcomeWireValue.AlreadyIdle, outcome.Outcome);
    }

    /// <summary>Verifies that a malformed pairing_cancel sends a malformed_message error and reports a protocol violation.</summary>
    [Fact]
    public async Task DispatchAsync_MalformedPairingCancel_SendsErrorAndReportsViolation()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildRawEnvelope("pairing_cancel", "msg-1", "session-1", """{"unexpectedField":true}""");

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.True(result.IsProtocolViolation);
    }

    // ---- unhandled message types ----

    /// <summary>
    /// Verifies that a server-originated message type -- structurally unable to reach this dispatcher
    /// in production, since the connection handler's per-tier allowlist never authorizes a
    /// server-originated type from a client -- falls into the default case as a safe no-op rather than
    /// throwing or sending anything. Every client-originated message type this dispatcher owns
    /// (ping, every pairing_* message, and rename_request) is mapped as of this step.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ServerOriginatedMessageType_SendsNothing()
    {
        var dispatcher = Fixtures.BuildClientMessageDispatcher(codec: Codec);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        IPublicConnectionContext connection = new PublicConnectionContext(fakeConnection);
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.Pong, "msg-1", "session-1", new EmptyPayload());

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

    /// <summary>
    /// Begins pairing and immediately commits its initial display, for the common case where a test
    /// needs a challenge <see cref="IPairingCoordinator.ConfirmCode"/> will actually accept: a code
    /// cannot be confirmed before its exact challenge's display has been committed.
    /// </summary>
    /// <param name="coordinator">The coordinator to begin pairing on.</param>
    /// <param name="clientId">The client beginning pairing.</param>
    /// <returns>The started-pairing result, with its challenge's display already committed.</returns>
    private static PairingStartResult BeginAndDisplayPairing(IPairingCoordinator coordinator, ClientId clientId)
    {
        PairingStartResult start = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        return start;
    }
}
