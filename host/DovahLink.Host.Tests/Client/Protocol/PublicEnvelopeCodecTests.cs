using System.Text;
using System.Text.Json;
using DovahLink.Host.Client.Protocol;

namespace DovahLink.Host.Tests.Client.Protocol;

/// <summary>Tests for <see cref="PublicEnvelopeCodec"/>.</summary>
public class PublicEnvelopeCodecTests
{
    private static readonly PublicEnvelopeCodec Codec = new();

    // ---- Round trips ----

    /// <summary>Verifies that a hello payload round-trips through encode/decode with every field intact.</summary>
    [Fact]
    public void EncodeThenDecode_HelloPayload_RoundTrips()
    {
        var payload = new HelloPayload
        {
            Endpoint = "client",
            ClientId = "the-client-id",
            Auth = new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = "deadbeef" },
        };
        byte[] encoded = Codec.Encode(PublicMessageType.Hello, "msg-1", null, null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.Hello, envelope!.MessageType);
        Assert.True(Codec.TryDecodePayload(envelope, out HelloPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that an unpaired hello (no token) round-trips with a null token.</summary>
    [Fact]
    public void EncodeThenDecode_UnpairedHelloPayload_TokenStaysNull()
    {
        var payload = new HelloPayload
        {
            Endpoint = "client",
            ClientId = "the-client-id",
            Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired },
        };
        byte[] encoded = Codec.Encode(PublicMessageType.Hello, "msg-1", null, null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out HelloPayload? decoded));
        Assert.Null(decoded!.Auth.Token);
    }

    /// <summary>Verifies that a hello_ack payload round-trips, including its enum fields' snake_case wire form.</summary>
    [Fact]
    public void EncodeThenDecode_HelloAckPayload_RoundTrips()
    {
        var payload = new HelloAckPayload { BridgeVersion = "0.3.3", ClientIdentityKind = ClientIdentityKind.Paired };
        byte[] encoded = Codec.Encode(PublicMessageType.HelloAck, "msg-2", "session-1", "msg-1", null, "client-1", payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.Equal("session-1", envelope!.SessionId);
        Assert.Equal("msg-1", envelope.CorrelationId);
        Assert.Equal("client-1", envelope.ClientId);
        Assert.Null(envelope.BridgeInstanceId);
        Assert.True(Codec.TryDecodePayload(envelope, out HelloAckPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that an empty capabilities payload round-trips.</summary>
    [Fact]
    public void EncodeThenDecode_EmptyCapabilitiesPayload_RoundTrips()
    {
        var payload = new CapabilitiesPayload { Capabilities = [] };
        byte[] encoded = Codec.Encode(PublicMessageType.Capabilities, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out CapabilitiesPayload? decoded));
        Assert.Empty(decoded!.Capabilities);
    }

    /// <summary>Verifies that a subscribe payload's requested areas round-trip.</summary>
    [Fact]
    public void EncodeThenDecode_SubscribePayload_RoundTrips()
    {
        var payload = new SubscribePayload { StateAreas = ["area_one", "area_two"] };
        byte[] encoded = Codec.Encode(PublicMessageType.Subscribe, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out SubscribePayload? decoded));
        Assert.Equal(payload.StateAreas, decoded!.StateAreas);
    }

    /// <summary>Verifies that a subscription_ack payload's accepted/rejected areas round-trip.</summary>
    [Fact]
    public void EncodeThenDecode_SubscriptionAckPayload_RoundTrips()
    {
        var payload = new SubscriptionAckPayload { AcceptedStateAreas = [], RejectedStateAreas = ["area_one"] };
        byte[] encoded = Codec.Encode(PublicMessageType.SubscriptionAck, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out SubscriptionAckPayload? decoded));
        Assert.Equal(payload.AcceptedStateAreas, decoded!.AcceptedStateAreas);
        Assert.Equal(payload.RejectedStateAreas, decoded.RejectedStateAreas);
    }

    /// <summary>Verifies that a snapshot_request payload round-trips, including its optional known revision.</summary>
    [Fact]
    public void EncodeThenDecode_SnapshotRequestPayload_RoundTrips()
    {
        var payload = new SnapshotRequestPayload { StateArea = "area_one", KnownRevision = 41 };
        byte[] encoded = Codec.Encode(PublicMessageType.SnapshotRequest, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out SnapshotRequestPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that a snapshot_request payload without an optional known revision round-trips with a null value rather than a default.</summary>
    [Fact]
    public void EncodeThenDecode_SnapshotRequestPayloadWithoutKnownRevision_RoundTripsAsNull()
    {
        var payload = new SnapshotRequestPayload { StateArea = "area_one" };
        byte[] encoded = Codec.Encode(PublicMessageType.SnapshotRequest, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out SnapshotRequestPayload? decoded));
        Assert.Null(decoded!.KnownRevision);
    }

    /// <summary>Verifies that an error payload round-trips, including a null details field.</summary>
    [Fact]
    public void EncodeThenDecode_ErrorPayload_RoundTrips()
    {
        var payload = new ErrorPayload
        {
            Code = PublicProtocolErrorCode.MalformedMessage,
            Message = "Token validation failed",
            Retryable = false,
        };
        byte[] encoded = Codec.Encode(PublicMessageType.Error, "msg-1", null, null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out ErrorPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that an error payload with a populated details field round-trips it intact.</summary>
    [Fact]
    public void EncodeThenDecode_ErrorPayloadWithDetails_RoundTrips()
    {
        var payload = new ErrorPayload
        {
            Code = PublicProtocolErrorCode.RateLimited,
            Message = "Too many attempts",
            Retryable = true,
            Details = "retry-after-60s",
        };
        byte[] encoded = Codec.Encode(PublicMessageType.Error, "msg-1", null, null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out ErrorPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that hello_ack round-trips the Unpaired identity kind, not only Paired.</summary>
    [Fact]
    public void EncodeThenDecode_HelloAckPayloadUnpairedKind_RoundTrips()
    {
        var payload = new HelloAckPayload { BridgeVersion = "0.3.3", ClientIdentityKind = ClientIdentityKind.Unpaired };
        byte[] encoded = Codec.Encode(PublicMessageType.HelloAck, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out HelloAckPayload? decoded));
        Assert.Equal(ClientIdentityKind.Unpaired, decoded!.ClientIdentityKind);
    }

    /// <summary>
    /// Verifies every canonical message type round-trips through Encode/Decode's messageType mapping
    /// -- guarding against a missed or mismatched case in either direction's large hand-written switch.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllMessageTypes))]
    public void EncodeThenDecode_EveryMessageType_RoundTrips(PublicMessageType messageType)
    {
        byte[] encoded = Codec.Encode(messageType, "msg-1", null, null, null, null, new { });

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.Equal(messageType, envelope!.MessageType);
    }

    /// <summary>Every canonical <see cref="PublicMessageType"/> value, for the exhaustive round-trip theory above.</summary>
    public static IEnumerable<object[]> AllMessageTypes() =>
        Enum.GetValues<PublicMessageType>().Select(value => new object[] { value });

    /// <summary>Verifies that Encode always writes a null bridgeInstanceId, per the approved D1 transition-boundary limitation, regardless of caller intent.</summary>
    [Fact]
    public void Encode_AlwaysWritesNullBridgeInstanceId()
    {
        byte[] encoded = Codec.Encode(PublicMessageType.Ping, "msg-1", "session-1", null, null, null, new object());

        using JsonDocument document = JsonDocument.Parse(encoded);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("bridgeInstanceId").ValueKind);
    }

    /// <summary>Verifies that Encode rejects a null payload rather than emitting a wire message its own decoder would reject.</summary>
    [Fact]
    public void Encode_NullPayload_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Codec.Encode<object?>(PublicMessageType.Ping, "msg-1", null, null, null, null, null));
    }

    /// <summary>Verifies that Encode rejects a string (scalar) payload.</summary>
    [Fact]
    public void Encode_StringPayload_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Codec.Encode(PublicMessageType.Ping, "msg-1", null, null, null, null, "not-an-object"));
    }

    /// <summary>Verifies that Encode rejects a numeric (scalar) payload.</summary>
    [Fact]
    public void Encode_NumericPayload_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Codec.Encode(PublicMessageType.Ping, "msg-1", null, null, null, null, 123));
    }

    /// <summary>Verifies that Encode rejects an array payload.</summary>
    [Fact]
    public void Encode_ArrayPayload_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Codec.Encode(PublicMessageType.Ping, "msg-1", null, null, null, null, new[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that the empty payload shared by ping, pairing_request, pairing_renotify, and pairing_cancel round-trips.</summary>
    [Fact]
    public void EncodeThenDecode_EmptyPayload_RoundTrips()
    {
        byte[] encoded = Codec.Encode(PublicMessageType.Ping, "msg-1", "session-1", null, null, null, new EmptyPayload());

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out EmptyPayload? decoded));
        Assert.Equal(new EmptyPayload(), decoded);
    }

    /// <summary>Verifies that a pairing_confirm payload with a display name round-trips.</summary>
    [Fact]
    public void EncodeThenDecode_PairingConfirmPayload_RoundTrips()
    {
        var payload = new PairingConfirmPayload { Code = "123456", DisplayName = "Living Room PC" };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingConfirm, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingConfirmPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that a pairing_confirm payload without a display name round-trips with a null value.</summary>
    [Fact]
    public void EncodeThenDecode_PairingConfirmPayloadWithoutDisplayName_RoundTripsAsNull()
    {
        var payload = new PairingConfirmPayload { Code = "123456" };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingConfirm, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingConfirmPayload? decoded));
        Assert.Null(decoded!.DisplayName);
    }

    /// <summary>
    /// Verifies that a present empty display name round-trips distinctly from an absent one: per
    /// <c>protocol/schema/README.md</c>, an empty string clears the name, while an absent/null value
    /// preserves an existing one.
    /// </summary>
    [Fact]
    public void EncodeThenDecode_PairingConfirmPayloadWithEmptyDisplayName_RoundTripsAsEmptyNotNull()
    {
        var payload = new PairingConfirmPayload { Code = "123456", DisplayName = string.Empty };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingConfirm, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingConfirmPayload? decoded));
        Assert.Equal(string.Empty, decoded!.DisplayName);
    }

    /// <summary>Verifies that a pairing_ack payload round-trips.</summary>
    [Fact]
    public void EncodeThenDecode_PairingAckPayload_RoundTrips()
    {
        var payload = new PairingAckPayload { Credential = "deadbeef" };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingAck, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingAckPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that a rename_request payload round-trips, including an empty display name that clears the name.</summary>
    [Fact]
    public void EncodeThenDecode_RenameRequestPayloadWithEmptyDisplayName_RoundTrips()
    {
        var payload = new RenameRequestPayload { DisplayName = string.Empty };
        byte[] encoded = Codec.Encode(PublicMessageType.RenameRequest, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out RenameRequestPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that a pairing_status payload round-trips, including a numeric expiresInSeconds.</summary>
    [Fact]
    public void EncodeThenDecode_PairingStatusPayload_RoundTrips()
    {
        var payload = new PairingStatusPayload { State = PairingStatusWireState.Available, ExpiresInSeconds = 287 };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingStatus, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingStatusPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>
    /// Verifies that an unavailable pairing_status writes expiresInSeconds as an explicit JSON null
    /// key, not merely a C#-round-trippable null: <c>protocol/schema/README.md</c> requires the key
    /// present for every state except other_device_pairing.
    /// </summary>
    [Fact]
    public void Encode_PairingStatusPayloadUnavailable_WritesExplicitNullExpiresInSeconds()
    {
        var payload = new PairingStatusPayload { State = PairingStatusWireState.Unavailable, ExpiresInSeconds = null };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingStatus, "msg-1", "session-1", "msg-0", null, null, payload);

        using JsonDocument document = JsonDocument.Parse(encoded);
        JsonElement payloadElement = document.RootElement.GetProperty("payload");
        Assert.True(payloadElement.TryGetProperty("expiresInSeconds", out JsonElement expiresInSeconds));
        Assert.Equal(JsonValueKind.Null, expiresInSeconds.ValueKind);
    }

    /// <summary>
    /// Verifies that other_device_pairing omits expiresInSeconds entirely, distinct from every other
    /// state's explicit null.
    /// </summary>
    [Fact]
    public void Encode_PairingStatusOtherDevicePayload_OmitsExpiresInSecondsKey()
    {
        var payload = new PairingStatusOtherDevicePayload { State = PairingStatusWireState.OtherDevicePairing };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingStatus, "msg-1", "session-1", "msg-0", null, null, payload);

        using JsonDocument document = JsonDocument.Parse(encoded);
        JsonElement payloadElement = document.RootElement.GetProperty("payload");
        Assert.False(payloadElement.TryGetProperty("expiresInSeconds", out _));
    }

    /// <summary>Verifies that an other_device_pairing pairing_status payload round-trips through decode as well as encode.</summary>
    [Fact]
    public void EncodeThenDecode_PairingStatusOtherDevicePayload_RoundTrips()
    {
        var payload = new PairingStatusOtherDevicePayload { State = PairingStatusWireState.OtherDevicePairing };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingStatus, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingStatusOtherDevicePayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that an in-progress pairing_status payload round-trips with its own client's remaining code seconds.</summary>
    [Fact]
    public void EncodeThenDecode_PairingStatusPayloadInProgress_RoundTrips()
    {
        var payload = new PairingStatusPayload { State = PairingStatusWireState.InProgress, ExpiresInSeconds = 42 };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingStatus, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingStatusPayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that a fully populated pairing_outcome payload round-trips.</summary>
    [Fact]
    public void EncodeThenDecode_PairingOutcomePayload_RoundTrips()
    {
        var payload = new PairingOutcomePayload
        {
            Outcome = PairingOutcomeWireValue.Trusted,
            Credential = "deadbeef",
            ShortId = "12345",
            DisplayName = "Living Room PC",
        };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingOutcome, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingOutcomePayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that a minimal pairing_outcome payload round-trips with every optional field null.</summary>
    [Fact]
    public void EncodeThenDecode_PairingOutcomePayloadMinimal_RoundTripsWithNullOptionalFields()
    {
        var payload = new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.Invalid };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingOutcome, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingOutcomePayload? decoded));
        Assert.Null(decoded!.Credential);
        Assert.Null(decoded.ShortId);
        Assert.Null(decoded.DisplayName);
        Assert.Null(decoded.RetryAfterSeconds);
    }

    /// <summary>Verifies that a pacing_limited pairing_outcome payload round-trips its retryAfterSeconds value.</summary>
    [Fact]
    public void EncodeThenDecode_PairingOutcomePayloadPacingLimited_RoundTripsRetryAfterSeconds()
    {
        var payload = new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.PacingLimited, RetryAfterSeconds = 1 };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingOutcome, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingOutcomePayload? decoded));
        Assert.Equal(1, decoded!.RetryAfterSeconds);
    }

    /// <summary>
    /// Verifies every canonical <see cref="PairingOutcomeWireValue"/> round-trips through its
    /// snake_case wire form -- guarding against a typo in this hand-written 13-member enum.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPairingOutcomeWireValues))]
    public void EncodeThenDecode_EveryPairingOutcomeWireValue_RoundTrips(PairingOutcomeWireValue outcome)
    {
        var payload = new PairingOutcomePayload { Outcome = outcome };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingOutcome, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingOutcomePayload? decoded));
        Assert.Equal(outcome, decoded!.Outcome);
    }

    /// <summary>Every canonical <see cref="PairingOutcomeWireValue"/> value, for the exhaustive round-trip theory above.</summary>
    public static IEnumerable<object[]> AllPairingOutcomeWireValues() =>
        Enum.GetValues<PairingOutcomeWireValue>().Select(value => new object[] { value });

    /// <summary>Verifies every canonical <see cref="PairingStatusWireState"/> round-trips through its snake_case wire form.</summary>
    [Theory]
    [MemberData(nameof(AllPairingStatusWireStates))]
    public void EncodeThenDecode_EveryPairingStatusWireState_RoundTrips(PairingStatusWireState state)
    {
        var payload = new PairingStatusPayload { State = state, ExpiresInSeconds = null };
        byte[] encoded = Codec.Encode(PublicMessageType.PairingStatus, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out PairingStatusPayload? decoded));
        Assert.Equal(state, decoded!.State);
    }

    /// <summary>Every canonical <see cref="PairingStatusWireState"/> value, for the exhaustive round-trip theory above.</summary>
    public static IEnumerable<object[]> AllPairingStatusWireStates() =>
        Enum.GetValues<PairingStatusWireState>().Select(value => new object[] { value });

    /// <summary>Verifies every canonical <see cref="RenameOutcomeWireValue"/> round-trips through its snake_case wire form.</summary>
    [Theory]
    [MemberData(nameof(AllRenameOutcomeWireValues))]
    public void EncodeThenDecode_EveryRenameOutcomeWireValue_RoundTrips(RenameOutcomeWireValue outcome)
    {
        var payload = new RenameOutcomePayload { Outcome = outcome };
        byte[] encoded = Codec.Encode(PublicMessageType.RenameOutcome, "msg-1", "session-1", null, null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out RenameOutcomePayload? decoded));
        Assert.Equal(outcome, decoded!.Outcome);
    }

    /// <summary>Every canonical <see cref="RenameOutcomeWireValue"/> value, for the exhaustive round-trip theory above.</summary>
    public static IEnumerable<object[]> AllRenameOutcomeWireValues() =>
        Enum.GetValues<RenameOutcomeWireValue>().Select(value => new object[] { value });

    /// <summary>Verifies that a renamed rename_outcome payload round-trips with its display name.</summary>
    [Fact]
    public void EncodeThenDecode_RenameOutcomePayload_RoundTrips()
    {
        var payload = new RenameOutcomePayload { Outcome = RenameOutcomeWireValue.Renamed, DisplayName = "New Name" };
        byte[] encoded = Codec.Encode(PublicMessageType.RenameOutcome, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out RenameOutcomePayload? decoded));
        Assert.Equal(payload, decoded);
    }

    /// <summary>Verifies that a non-renamed rename_outcome payload round-trips with a null display name.</summary>
    [Fact]
    public void EncodeThenDecode_RenameOutcomePayloadWithoutDisplayName_RoundTripsAsNull()
    {
        var payload = new RenameOutcomePayload { Outcome = RenameOutcomeWireValue.InvalidDisplayName };
        byte[] encoded = Codec.Encode(PublicMessageType.RenameOutcome, "msg-1", "session-1", "msg-0", null, null, payload);

        Assert.True(Codec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.True(Codec.TryDecodePayload(envelope!, out RenameOutcomePayload? decoded));
        Assert.Null(decoded!.DisplayName);
    }

    // ---- Envelope field validation ----

    /// <summary>Verifies that a missing required envelope field (messageId) is rejected.</summary>
    [Fact]
    public void TryDecode_MissingMessageId_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("hello", "{}", includeMessageId: false);

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that a wrong-typed required envelope field (sessionId as a number) is rejected.</summary>
    [Fact]
    public void TryDecode_SessionIdWrongType_ReturnsFalse()
    {
        string json = """
            {"messageType":"ping","messageId":"m1","sessionId":42,"correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that an explicit JSON null is accepted for a nullable envelope field.</summary>
    [Fact]
    public void TryDecode_SessionIdExplicitNull_IsAccepted()
    {
        string json = BuildEnvelopeJson("ping", "{}", sessionId: "null");

        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));
        Assert.Null(envelope!.SessionId);
    }

    /// <summary>Verifies that an unrecognized messageType is rejected as malformed rather than accepted as forward-compatible.</summary>
    [Fact]
    public void TryDecode_UnrecognizedMessageType_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("not_a_real_message_type", "{}");

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that a missing payload object is rejected.</summary>
    [Fact]
    public void TryDecode_MissingPayload_ReturnsFalse()
    {
        string json = """
            {"messageType":"ping","messageId":"m1","sessionId":null,"correlationId":null,"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that a non-object payload value is rejected.</summary>
    [Fact]
    public void TryDecode_PayloadNotAnObject_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("ping", "\"not-an-object\"");

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that a non-object JSON root is rejected.</summary>
    [Fact]
    public void TryDecode_RootIsNotAnObject_ReturnsFalse()
    {
        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes("[]"), out _));
    }

    /// <summary>Verifies that syntactically invalid JSON is rejected rather than throwing.</summary>
    [Fact]
    public void TryDecode_NotValidJson_ReturnsFalse()
    {
        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes("{not valid json"), out _));
    }

    /// <summary>Verifies that an unknown top-level envelope field is ignored rather than rejected.</summary>
    [Fact]
    public void TryDecode_UnknownTopLevelField_IsIgnored()
    {
        string json = """
            {"messageType":"ping","messageId":"m1","sessionId":null,"correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null,"futureField":"future-value"}
            """;

        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.Ping, envelope!.MessageType);
    }

    /// <summary>Verifies that a wrong-typed messageType (a number instead of a string) is rejected, exercising that field's own dedicated check rather than the shared optional-string helper.</summary>
    [Fact]
    public void TryDecode_MessageTypeWrongType_ReturnsFalse()
    {
        string json = """
            {"messageType":42,"messageId":"m1","sessionId":null,"correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that an explicit JSON null for the required payload object is rejected the same way a non-object payload is.</summary>
    [Fact]
    public void TryDecode_PayloadExplicitNull_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("ping", "null");

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    // ---- Payload field validation ----

    /// <summary>Verifies that a hello payload missing a required field (clientId) fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_HelloMissingClientId_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("hello", """{"endpoint":"client","auth":{"method":"unpaired"}}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out HelloPayload? _));
    }

    /// <summary>Verifies that a hello payload whose auth.method is an unrecognized value fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_HelloUnrecognizedAuthMethod_ReturnsFalse()
    {
        string json = BuildEnvelopeJson(
            "hello", """{"endpoint":"client","clientId":"c1","auth":{"method":"not_a_real_method"}}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out HelloPayload? _));
    }

    /// <summary>Verifies that a wrong-typed payload field (a string where a boolean is required) fails payload decoding rather than throwing out of the codec.</summary>
    [Fact]
    public void TryDecodePayload_WrongFieldType_ReturnsFalse()
    {
        string json = BuildEnvelopeJson(
            "error", """{"code":"malformed_message","message":"m","retryable":"not-a-boolean"}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out ErrorPayload? _));
    }

    /// <summary>Verifies that a missing required non-nullable value-type field (a bool, not a reference type) still fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_MissingRequiredValueTypeField_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("error", """{"code":"malformed_message","message":"m"}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out ErrorPayload? _));
    }

    /// <summary>
    /// Verifies that a required reference-type field explicitly present as JSON <c>null</c> fails
    /// payload decoding rather than satisfying the C# <c>required</c> keyword's presence-only check:
    /// <c>hello.auth</c> is present in the document, but its value is <c>null</c>.
    /// </summary>
    [Fact]
    public void TryDecodePayload_HelloAuthExplicitNull_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("hello", """{"endpoint":"client","clientId":"c1","auth":null}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out HelloPayload? _));
    }

    /// <summary>Verifies that a required list-typed field explicitly present as JSON <c>null</c> fails payload decoding, for <c>capabilities</c>.</summary>
    [Fact]
    public void TryDecodePayload_CapabilitiesListExplicitNull_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("capabilities", """{"capabilities":null}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out CapabilitiesPayload? _));
    }

    /// <summary>Verifies that a required list-typed field explicitly present as JSON <c>null</c> fails payload decoding, for <c>subscribe</c>'s <c>stateAreas</c>.</summary>
    [Fact]
    public void TryDecodePayload_SubscribeStateAreasExplicitNull_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("subscribe", """{"stateAreas":null}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out SubscribePayload? _));
    }

    /// <summary>
    /// Verifies that an unrecognized nested property fails payload decoding for every message-specific
    /// payload type: no payload object -- unlike the common envelope's own top-level fields -- is
    /// documented as reserving extension fields for forward compatibility.
    /// </summary>
    [Theory]
    [InlineData("hello", """{"endpoint":"client","clientId":"c1","auth":{"method":"unpaired"},"unexpectedField":true}""")]
    [InlineData("hello", """{"endpoint":"client","clientId":"c1","auth":{"method":"unpaired","unexpectedField":true}}""")]
    [InlineData("subscribe", """{"stateAreas":["area_one"],"unexpectedField":true}""")]
    [InlineData("snapshot_request", """{"stateArea":"area_one","unexpectedField":true}""")]
    [InlineData("capabilities", """{"capabilities":[],"unexpectedField":true}""")]
    [InlineData("capabilities", """{"capabilities":[{"id":"x","version":"1","unexpectedField":true}]}""")]
    [InlineData("ping", """{"unexpectedField":true}""")]
    [InlineData("pairing_confirm", """{"code":"123456","unexpectedField":true}""")]
    [InlineData("rename_request", """{"displayName":"n","unexpectedField":true}""")]
    public void TryDecodePayload_UnknownNestedField_ReturnsFalse(string messageType, string payloadJson)
    {
        string json = BuildEnvelopeJson(messageType, payloadJson);
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        bool decoded = messageType switch
        {
            "hello" => Codec.TryDecodePayload(envelope!, out HelloPayload? _),
            "subscribe" => Codec.TryDecodePayload(envelope!, out SubscribePayload? _),
            "snapshot_request" => Codec.TryDecodePayload(envelope!, out SnapshotRequestPayload? _),
            "capabilities" => Codec.TryDecodePayload(envelope!, out CapabilitiesPayload? _),
            "ping" => Codec.TryDecodePayload(envelope!, out EmptyPayload? _),
            "pairing_confirm" => Codec.TryDecodePayload(envelope!, out PairingConfirmPayload? _),
            "rename_request" => Codec.TryDecodePayload(envelope!, out RenameRequestPayload? _),
            _ => throw new InvalidOperationException($"Unhandled messageType '{messageType}' in test data."),
        };
        Assert.False(decoded);
    }

    /// <summary>
    /// Verifies that the common envelope itself still tolerates an unrecognized top-level field,
    /// unaffected by the nested-payload strictness above: <c>protocol/schema/README.md</c> documents
    /// forward-compatible extension for the envelope's own top-level fields specifically, and
    /// <see cref="PublicEnvelopeCodec.TryDecode"/> reads named fields individually rather than
    /// rejecting a document for carrying an extra one.
    /// </summary>
    [Fact]
    public void TryDecode_EnvelopeWithUnknownTopLevelField_StillDecodes()
    {
        string json = """
            {"messageType":"ping","messageId":"m1","sessionId":null,"correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null,"unexpectedTopLevelField":true}
            """;

        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that a pairing_confirm payload missing its required code fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_PairingConfirmMissingCode_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("pairing_confirm", """{"displayName":"n"}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out PairingConfirmPayload? _));
    }

    /// <summary>Verifies that a pairing_ack payload missing its required credential fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_PairingAckMissingCredential_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("pairing_ack", "{}");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out PairingAckPayload? _));
    }

    /// <summary>Verifies that a rename_request payload missing its required display name fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_RenameRequestMissingDisplayName_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("rename_request", "{}");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out RenameRequestPayload? _));
    }

    /// <summary>
    /// Verifies that a rename_request payload whose required display name is explicitly JSON
    /// <c>null</c> fails payload decoding, distinct from a present empty string, which is valid and
    /// clears the name.
    /// </summary>
    [Fact]
    public void TryDecodePayload_RenameRequestDisplayNameExplicitNull_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("rename_request", """{"displayName":null}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out RenameRequestPayload? _));
    }

    /// <summary>Verifies that a pairing_status payload missing its required state fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_PairingStatusMissingState_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("pairing_status", """{"expiresInSeconds":null}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out PairingStatusPayload? _));
    }

    /// <summary>Verifies that a pairing_status payload missing its required expiresInSeconds key fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_PairingStatusMissingExpiresInSeconds_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("pairing_status", """{"state":"available"}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out PairingStatusPayload? _));
    }

    /// <summary>Verifies that a pairing_status other-device payload missing its required state fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_PairingStatusOtherDeviceMissingState_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("pairing_status", "{}");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out PairingStatusOtherDevicePayload? _));
    }

    /// <summary>Verifies that a pairing_outcome payload missing its required outcome fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_PairingOutcomeMissingOutcome_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("pairing_outcome", "{}");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out PairingOutcomePayload? _));
    }

    /// <summary>Verifies that a rename_outcome payload missing its required outcome fails payload decoding.</summary>
    [Fact]
    public void TryDecodePayload_RenameOutcomeMissingOutcome_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("rename_outcome", "{}");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.False(Codec.TryDecodePayload(envelope!, out RenameOutcomePayload? _));
    }

    // ---- Bounds: nesting depth ----

    /// <summary>Verifies that a payload nested exactly to the approved 32-level depth is accepted.</summary>
    [Fact]
    public void TryDecode_PayloadAtExactMaxDepth_IsAccepted()
    {
        string json = BuildEnvelopeJson("ping", BuildNestedObjectJson(31));

        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that a payload nested one level past the approved 32-level depth is rejected.</summary>
    [Fact]
    public void TryDecode_PayloadOneLevelPastMaxDepth_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("ping", BuildNestedObjectJson(32));

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    // ---- Bounds: string length ----

    /// <summary>Verifies that a string exactly at the approved 4 KiB bound is accepted.</summary>
    [Fact]
    public void TryDecode_StringAtExactMaxLength_IsAccepted()
    {
        string value = new('a', 4096);
        string json = BuildEnvelopeJson("ping", $$"""{"value":"{{value}}"}""");

        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>
    /// Verifies the bound is measured in UTF-8 bytes, not .NET UTF-16 char count: a string of
    /// three-byte characters whose char count (1366) is far under the bound but whose UTF-8 byte
    /// count (4098) exceeds it is rejected -- a char-count-based check would have wrongly accepted it.
    /// </summary>
    [Fact]
    public void TryDecode_MultiByteStringOverByteBoundButUnderCharBound_ReturnsFalse()
    {
        string value = new('€', 1366);
        string json = BuildEnvelopeJson("ping", $$"""{"value":"{{value}}"}""");

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that a string one byte past the approved 4 KiB bound is rejected.</summary>
    [Fact]
    public void TryDecode_StringOneBytePastMaxLength_ReturnsFalse()
    {
        string value = new('a', 4097);
        string json = BuildEnvelopeJson("ping", $$"""{"value":"{{value}}"}""");

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that an oversized object member name, not only an oversized value, is rejected.</summary>
    [Fact]
    public void TryDecode_OversizedMemberName_ReturnsFalse()
    {
        string longName = new('k', 4097);
        string json = BuildEnvelopeJson("ping", $$"""{"{{longName}}":"v"}""");

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    // ---- Bounds: array length ----

    /// <summary>Verifies that an array with exactly the approved 128 elements is accepted.</summary>
    [Fact]
    public void TryDecode_ArrayAtExactMaxLength_IsAccepted()
    {
        string array = BuildJsonNumberArray(128);
        string json = BuildEnvelopeJson("ping", $$"""{"values":{{array}}}""");

        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that an array with one element past the approved 128-element bound is rejected.</summary>
    [Fact]
    public void TryDecode_ArrayOneElementPastMaxLength_ReturnsFalse()
    {
        string array = BuildJsonNumberArray(129);
        string json = BuildEnvelopeJson("ping", $$"""{"values":{{array}}}""");

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    // ---- Bounds: object member count ----

    /// <summary>Verifies that an object with exactly the approved 64 members is accepted.</summary>
    [Fact]
    public void TryDecode_ObjectAtExactMaxMembers_IsAccepted()
    {
        string json = BuildEnvelopeJson("ping", BuildJsonObjectWithMembers(64));

        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that an object with one member past the approved 64-member bound is rejected.</summary>
    [Fact]
    public void TryDecode_ObjectOneMemberPastMaxMembers_ReturnsFalse()
    {
        string json = BuildEnvelopeJson("ping", BuildJsonObjectWithMembers(65));

        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    // ---- Bounds: duplicate properties ----

    /// <summary>
    /// Verifies that a duplicate JSON property name is rejected wherever it occurs -- envelope-level
    /// (<c>messageType</c>, <c>messageId</c>, <c>sessionId</c>, <c>clientId</c>) and within a nested
    /// payload object (<c>auth.method</c>, <c>auth.token</c>) alike -- rather than leaving a
    /// security-sensitive field's effective value dependent on parser/property-lookup behavior.
    /// </summary>
    [Theory]
    [InlineData(
        """{"messageType":"ping","messageType":"hello","messageId":"m1","sessionId":null,"correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}""")]
    [InlineData(
        """{"messageType":"ping","messageId":"m1","messageId":"m2","sessionId":null,"correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}""")]
    [InlineData(
        """{"messageType":"ping","messageId":"m1","sessionId":null,"sessionId":"s2","correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}""")]
    [InlineData(
        """{"messageType":"ping","messageId":"m1","sessionId":null,"correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null,"clientId":"c2"}""")]
    [InlineData(
        """{"messageType":"hello","messageId":"m1","sessionId":null,"correlationId":null,"payload":{"endpoint":"client","clientId":"c1","auth":{"method":"unpaired","method":"trusted_device_credential"}},"bridgeInstanceId":null,"playContextId":null,"clientId":null}""")]
    [InlineData(
        """{"messageType":"hello","messageId":"m1","sessionId":null,"correlationId":null,"payload":{"endpoint":"client","clientId":"c1","auth":{"method":"trusted_device_credential","token":"a","token":"b"}},"bridgeInstanceId":null,"playContextId":null,"clientId":null}""")]
    public void TryDecode_DuplicateProperty_ReturnsFalse(string json)
    {
        Assert.False(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>Verifies that an otherwise-identical, non-duplicated envelope still decodes, so the duplicate-rejection cases above are proven against a genuine well-formed baseline.</summary>
    [Fact]
    public void TryDecode_NoDuplicateProperties_StillDecodes()
    {
        string json = """{"messageType":"ping","messageId":"m1","sessionId":null,"correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}""";

        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out _));
    }

    /// <summary>
    /// Verifies that duplicate-property rejection is scoped to one object at a time, not the whole
    /// document: two sibling objects inside the same array may each reuse the same property names (here,
    /// two capability descriptors each carrying their own <c>id</c>/<c>version</c>) without either being
    /// treated as a duplicate of the other's fields.
    /// </summary>
    [Fact]
    public void TryDecodePayload_TwoCapabilityDescriptorsReusingTheSameFieldNames_StillDecodes()
    {
        string json = BuildEnvelopeJson("capabilities", """{"capabilities":[{"id":"a","version":"1"},{"id":"b","version":"1"}]}""");
        Assert.True(Codec.TryDecode(Encoding.UTF8.GetBytes(json), out PublicEnvelope? envelope));

        Assert.True(Codec.TryDecodePayload(envelope!, out CapabilitiesPayload? _));
    }

    // ---- Helpers ----

    /// <summary>Builds a complete, otherwise-valid envelope JSON string wrapping the given raw payload JSON.</summary>
    private static string BuildEnvelopeJson(
        string messageType,
        string payloadJson,
        bool includeMessageId = true,
        string sessionId = "null")
    {
        string messageIdField = includeMessageId ? ""","messageId":"m1" """ : string.Empty;
        return $$"""
            {"messageType":"{{messageType}}"{{messageIdField}},"sessionId":{{sessionId}},"correlationId":null,"payload":{{payloadJson}},"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;
    }

    /// <summary>Builds a JSON object nested <paramref name="additionalLevels"/> levels beyond its own top level, terminated by a scalar.</summary>
    private static string BuildNestedObjectJson(int additionalLevels)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < additionalLevels; i++)
        {
            builder.Append("""{"n":""");
        }

        builder.Append('1');
        for (int i = 0; i < additionalLevels; i++)
        {
            builder.Append('}');
        }

        return builder.ToString();
    }

    /// <summary>Builds a JSON array literal of the given element count.</summary>
    private static string BuildJsonNumberArray(int count) =>
        "[" + string.Join(',', Enumerable.Range(0, count).Select(i => i.ToString())) + "]";

    /// <summary>Builds a JSON object literal with the given number of distinct short members.</summary>
    private static string BuildJsonObjectWithMembers(int count) =>
        "{" + string.Join(',', Enumerable.Range(0, count).Select(i => $"\"k{i}\":{i}")) + "}";
}
