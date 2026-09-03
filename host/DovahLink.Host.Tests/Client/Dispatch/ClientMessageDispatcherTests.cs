using System.Text;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
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
        PublicEnvelope envelope = BuildEnvelope(PublicMessageType.PairingRequest, "msg-1", "session-1", new EmptyPayload());

        ClientDispatchResult result = await dispatcher.DispatchAsync(
            ClientId.NewId(), SessionId.NewId(), connection, envelope, CancellationToken.None);

        Assert.Empty(fakeConnection.SentPayloads);
        Assert.False(result.IsProtocolViolation);
        Assert.False(result.UpgradeToFullTrust);
    }

    // ---- Helpers ----

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
