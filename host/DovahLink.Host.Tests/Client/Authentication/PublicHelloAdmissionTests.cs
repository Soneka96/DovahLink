using System.Text;
using System.Text.Json;
using DovahLink.Host;
using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Authentication;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Client.Authentication;

/// <summary>Tests for <see cref="PublicHelloAdmissionHandler"/>.</summary>
public class PublicHelloAdmissionTests
{
    /// <summary>A well-formed <c>trusted_device_credential</c> value (exactly <see cref="Constants.PairingCredentialLength"/> hex characters) used wherever a test needs a credential that passes wire-format validation regardless of whether it matches a seeded trust record.</summary>
    private const string ValidCredential = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>A second well-formed credential, distinct from <see cref="ValidCredential"/>, used wherever a test needs a wire-valid credential that does not match a seeded trust record.</summary>
    private const string WrongButValidCredential = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    // ---- Successful admission ----

    /// <summary>Verifies that an unpaired hello admits a restricted session and sends hello_ack then capabilities.</summary>
    [Fact]
    public void HandleMessageAsync_UnpairedHello_AdmitsRestrictedSession()
    {
        var context = new TestContext();
        string clientId = Guid.NewGuid().ToString();
        byte[] hello = BuildHello(context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.Unpaired });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        Assert.Equal(2, context.FakeConnection.SentPayloads.Count);
        (PublicEnvelope ackEnvelope, HelloAckPayload ack) = DecodeSent<HelloAckPayload>(context.Codec, context.FakeConnection.SentPayloads[0]);
        Assert.Equal(PublicMessageType.HelloAck, ackEnvelope.MessageType);
        Assert.Equal("hello-1", ackEnvelope.CorrelationId);
        Assert.Equal(clientId, ackEnvelope.ClientId);
        Assert.NotNull(ackEnvelope.SessionId);
        Assert.Null(ackEnvelope.BridgeInstanceId);
        Assert.Equal(ClientIdentityKind.Unpaired, ack.ClientIdentityKind);
        Assert.False(string.IsNullOrEmpty(ack.BridgeVersion));

        (PublicEnvelope capsEnvelope, CapabilitiesPayload caps) = DecodeSent<CapabilitiesPayload>(context.Codec, context.FakeConnection.SentPayloads[1]);
        Assert.Equal(PublicMessageType.Capabilities, capsEnvelope.MessageType);
        Assert.Null(capsEnvelope.CorrelationId);
        Assert.Null(capsEnvelope.ClientId);
        Assert.Empty(caps.Capabilities);

        Assert.Equal(1, context.SessionRegistry.ActiveCount);
    }

    /// <summary>Verifies that a matching trusted_device_credential hello admits a full (paired) session.</summary>
    [Fact]
    public void HandleMessageAsync_MatchingTrustedDeviceCredentialHello_AdmitsPairedSession()
    {
        var context = new TestContext();
        string clientId = Guid.NewGuid().ToString();
        context.TrustStore.Seed(BuildTrustedRecord(clientId, ValidCredential));
        byte[] hello = BuildHello(
            context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = ValidCredential });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, HelloAckPayload ack) = DecodeSent<HelloAckPayload>(context.Codec, context.FakeConnection.SentPayloads[0]);
        Assert.Equal(ClientIdentityKind.Paired, ack.ClientIdentityKind);
    }

    /// <summary>Verifies that a valid one_time_local_token hello admits a full session identified as unpaired (per schema: developer sessions report "unpaired" identity even though they are unrestricted).</summary>
    [Fact]
    public void HandleMessageAsync_ValidOneTimeLocalTokenHello_AdmitsUnrestrictedUnpairedSession()
    {
        var context = new TestContext();
        string token = context.TokenAuthenticator.IssueToken();
        string clientId = Guid.NewGuid().ToString();
        byte[] hello = BuildHello(
            context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.OneTimeLocalToken, Token = token });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, HelloAckPayload ack) = DecodeSent<HelloAckPayload>(context.Codec, context.FakeConnection.SentPayloads[0]);
        Assert.Equal(ClientIdentityKind.Unpaired, ack.ClientIdentityKind);
        Assert.Equal(1, context.SessionRegistry.ActiveCount);
    }

    /// <summary>Verifies that a successful one-time-token hello consumes the token, so it cannot be reused.</summary>
    [Fact]
    public void HandleMessageAsync_ValidOneTimeLocalTokenHello_ConsumesTheToken()
    {
        var context = new TestContext();
        string token = context.TokenAuthenticator.IssueToken();
        byte[] hello = BuildHello(
            context.Codec, Guid.NewGuid().ToString(), "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.OneTimeLocalToken, Token = token });
        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        Assert.False(context.TokenAuthenticator.TryValidate(token));
    }

    /// <summary>Verifies that a revoked identity remains eligible for an unpaired (re-pair) hello.</summary>
    [Fact]
    public void HandleMessageAsync_RevokedIdentityUnpairedHello_StillAdmits()
    {
        var context = new TestContext();
        string clientId = Guid.NewGuid().ToString();
        context.TrustStore.Seed(new TrustRecord(new ClientId(Guid.Parse(clientId)), "AB12", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow));
        byte[] hello = BuildHello(context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.Unpaired });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        Assert.Equal(1, context.SessionRegistry.ActiveCount);
        Assert.Equal(0, context.FakeConnection.RequestCloseCalls);
    }

    // ---- Rejected hello: malformed ----

    /// <summary>Verifies that malformed hello shapes are rejected as malformed_message and admit no session.</summary>
    [Theory]
    [MemberData(nameof(MalformedHelloCases))]
    public void HandleMessageAsync_MalformedHello_RejectsWithoutAdmitting(HelloPayload malformedHello)
    {
        var context = new TestContext();
        byte[] hello = context.Codec.Encode(PublicMessageType.Hello, "hello-1", null, null, null, null, malformedHello);

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    /// <summary>
    /// Malformed hello shapes: wrong endpoint, non-GUID clientId, unpaired with a token present, a
    /// token-requiring method with no token, an empty trusted-device credential, a trusted-device
    /// credential of the wrong length, and a trusted-device credential of the right length containing
    /// a non-hex character.
    /// </summary>
    public static IEnumerable<object[]> MalformedHelloCases()
    {
        yield return [new HelloPayload { Endpoint = "server", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } }];
        yield return [new HelloPayload { Endpoint = "client", ClientId = "not-a-guid", Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } }];
        yield return [new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired, Token = "should-not-be-present" } }];
        yield return [new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.OneTimeLocalToken } }];
        yield return [new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = "" } }];
        yield return [new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = null } }];
        yield return [new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = "abcd" } }];
        yield return [new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = "not-a-hex-credential-of-length32" } }];
    }

    // ---- Rejected hello: authentication failures ----

    /// <summary>Verifies that a wrong one-time-token is rejected as unauthenticated.</summary>
    [Fact]
    public void HandleMessageAsync_WrongOneTimeLocalToken_RejectsAsUnauthenticated()
    {
        var context = new TestContext();
        context.TokenAuthenticator.IssueToken();
        byte[] hello = BuildHello(
            context.Codec, Guid.NewGuid().ToString(), "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.OneTimeLocalToken, Token = "wrong" });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.Unauthenticated, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    /// <summary>Verifies that a mismatched trusted-device credential is rejected as unauthenticated and recorded against the credential throttle, independent of the developer-token throttle.</summary>
    [Fact]
    public void HandleMessageAsync_MismatchedTrustedDeviceCredential_RejectsAndDoesNotAffectTokenThrottle()
    {
        var context = new TestContext();
        string clientId = Guid.NewGuid().ToString();
        context.TrustStore.Seed(BuildTrustedRecord(clientId, ValidCredential));
        string devToken = context.TokenAuthenticator.IssueToken();
        byte[] hello = BuildHello(
            context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = WrongButValidCredential });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.Unauthenticated, error.Code);

        // The mismatch recorded exactly one failure against the credential throttle: four more
        // exhaust its independent five-attempt budget.
        for (int i = 0; i < 4; i++)
        {
            context.CredentialThrottle.RecordFailure();
        }

        Assert.False(context.CredentialThrottle.IsAllowed());

        // The developer-token throttle is untouched by the credential mismatch above.
        Assert.True(context.TokenAuthenticator.TryValidate(devToken));
    }

    /// <summary>Verifies that a never-paired clientId presenting a trusted_device_credential is rejected as unauthenticated.</summary>
    [Fact]
    public void HandleMessageAsync_NeverPairedTrustedDeviceCredential_RejectsAsUnauthenticated()
    {
        var context = new TestContext();
        byte[] hello = BuildHello(
            context.Codec, Guid.NewGuid().ToString(), "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = WrongButValidCredential });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.Unauthenticated, error.Code);
    }

    /// <summary>Verifies that a blocked identity is rejected the same way for both unpaired and trusted-device hellos.</summary>
    [Theory]
    [InlineData(HelloAuthMethod.Unpaired)]
    [InlineData(HelloAuthMethod.TrustedDeviceCredential)]
    public void HandleMessageAsync_BlockedIdentity_RejectsAsBlocked(HelloAuthMethod method)
    {
        var context = new TestContext();
        string clientId = Guid.NewGuid().ToString();
        context.TrustStore.Seed(new TrustRecord(new ClientId(Guid.Parse(clientId)), "AB12", null, KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var auth = new HelloAuthPayload { Method = method, Token = method == HelloAuthMethod.Unpaired ? null : WrongButValidCredential };
        byte[] hello = BuildHello(context.Codec, clientId, "hello-1", auth);

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.Blocked, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    /// <summary>Verifies that a revoked identity presenting a trusted_device_credential is rejected as revoked, distinct from a never-paired identity's unauthenticated rejection.</summary>
    [Fact]
    public void HandleMessageAsync_RevokedIdentityTrustedDeviceCredential_RejectsAsRevoked()
    {
        var context = new TestContext();
        string clientId = Guid.NewGuid().ToString();
        context.TrustStore.Seed(new TrustRecord(new ClientId(Guid.Parse(clientId)), "AB12", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow));
        byte[] hello = BuildHello(
            context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = WrongButValidCredential });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.Revoked, error.Code);
    }

    /// <summary>
    /// Verifies that a correct trusted-device credential is still rejected as unauthenticated (not a
    /// distinct "rate limited" code, per "do not reveal which secret check failed") once the
    /// credential throttle is already exhausted before the hello arrives.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_CorrectCredentialButThrottleAlreadyExhausted_RejectsAsUnauthenticated()
    {
        var context = new TestContext();
        string clientId = Guid.NewGuid().ToString();
        context.TrustStore.Seed(BuildTrustedRecord(clientId, ValidCredential));
        for (int i = 0; i < 5; i++)
        {
            context.CredentialThrottle.RecordFailure();
        }
        byte[] hello = BuildHello(
            context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = ValidCredential });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.Unauthenticated, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    /// <summary>
    /// Verifies the recheck-after-admission race's Revoked branch specifically (the Blocked branch is
    /// covered by <see cref="HandleMessageAsync_BlockedByAdminBetweenInitialCheckAndRecheck_RollsBackAndRejects"/>):
    /// an identity Revoked between the initial check and the post-reservation recheck has its session
    /// reservation rolled back and is rejected as revoked.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_RevokedByAdminBetweenInitialCheckAndRecheck_RollsBackAndRejects()
    {
        var sessionRegistry = new FakeSessionRegistry();
        var innerTrustStore = new FakeTrustStore();
        string clientId = Guid.NewGuid().ToString();
        innerTrustStore.Seed(BuildTrustedRecord(clientId, ValidCredential));
        var raceTrustStore = new TrustStoreThatChangesOnSecondLookup(
            innerTrustStore, new TrustRecord(new ClientId(Guid.Parse(clientId)), "AB12", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow));
        var codec = new PublicEnvelopeCodec();
        var clock = new FakeClock();
        var handler = new PublicHelloAdmissionHandler(
            codec, sessionRegistry, raceTrustStore, new LocalConnectionTokenAuthenticator(clock),
            new TrustedCredentialFailureThrottle(clock), new FakePlayContextTracker(), clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        var connection = new PublicConnectionContext(fakeConnection);
        byte[] hello = BuildHello(codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = ValidCredential });

        handler.HandleMessageAsync(connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(codec, Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.Revoked, error.Code);
        Assert.Equal(0, sessionRegistry.ActiveCount);
    }

    /// <summary>Verifies that a hello whose payload fails to deserialize at all (a required field missing at the JSON level, not merely a semantically wrong value) is rejected as malformed_message.</summary>
    [Fact]
    public void HandleMessageAsync_HelloPayloadFailsToDeserialize_RejectsAsMalformed()
    {
        var context = new TestContext();
        string json = $$"""
            {"messageType":"hello","messageId":"hello-1","sessionId":null,"correlationId":null,"payload":{"endpoint":"client","clientId":"{{Guid.NewGuid()}}"},"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;

        context.Handler.HandleMessageAsync(context.Connection, Encoding.UTF8.GetBytes(json), CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    // ---- Pre-authentication hello envelope identity ----

    /// <summary>
    /// Verifies that a pre-authentication hello carrying a non-null envelope sessionId is rejected as
    /// malformed_message and admits no session -- a structurally valid JSON string in a field the
    /// schema requires to be null before a session exists, not a wrong JSON type.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_HelloWithNonNullEnvelopeSessionId_RejectsAsMalformed()
    {
        var context = new TestContext();
        byte[] hello = context.Codec.Encode(
            PublicMessageType.Hello, "hello-1", Guid.NewGuid().ToString(), null, null, null,
            new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    /// <summary>Verifies that a pre-authentication hello carrying a non-null envelope correlationId is rejected as malformed_message.</summary>
    [Fact]
    public void HandleMessageAsync_HelloWithNonNullEnvelopeCorrelationId_RejectsAsMalformed()
    {
        var context = new TestContext();
        byte[] hello = context.Codec.Encode(
            PublicMessageType.Hello, "hello-1", null, "some-prior-message-id", null, null,
            new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    /// <summary>Verifies that a pre-authentication hello carrying a non-null envelope playContextId is rejected as malformed_message -- play context is host-authoritative, never a value a client asserts.</summary>
    [Fact]
    public void HandleMessageAsync_HelloWithNonNullEnvelopePlayContextId_RejectsAsMalformed()
    {
        var context = new TestContext();
        byte[] hello = context.Codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, Guid.NewGuid().ToString(), null,
            new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    /// <summary>
    /// Verifies that a pre-authentication hello carrying a non-null envelope-level clientId is
    /// rejected as malformed_message, distinct from the payload-level <c>hello.clientId</c> (which is
    /// required and separately validated) -- the envelope identity is not yet established before a
    /// session exists.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_HelloWithNonNullEnvelopeClientId_RejectsAsMalformed()
    {
        var context = new TestContext();
        byte[] hello = context.Codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, null, Guid.NewGuid().ToString(),
            new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    /// <summary>
    /// Verifies that a pre-authentication hello carrying a non-null envelope bridgeInstanceId is
    /// rejected as malformed_message. Built from raw JSON since <see cref="PublicEnvelopeCodec.Encode"/>
    /// always encodes bridgeInstanceId as null (the approved D1 transition-boundary limitation) and so
    /// cannot itself produce this otherwise-well-formed wire shape.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_HelloWithNonNullEnvelopeBridgeInstanceId_RejectsAsMalformed()
    {
        var context = new TestContext();
        string json = $$$"""
            {"messageType":"hello","messageId":"hello-1","sessionId":null,"correlationId":null,"payload":{"endpoint":"client","clientId":"{{{Guid.NewGuid()}}}","auth":{"method":"unpaired"}},"bridgeInstanceId":"a-bridge-instance-id","playContextId":null,"clientId":null}
            """;

        context.Handler.HandleMessageAsync(context.Connection, Encoding.UTF8.GetBytes(json), CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
        Assert.Equal(0, context.SessionRegistry.ActiveCount);
    }

    // ---- Admission ordering: full slot must not consume a retryable one-time token ----

    /// <summary>
    /// Verifies the exact ordering invariant this concept requires: a valid one-time token whose
    /// session admission fails because the slot is full is not consumed, and remains usable once
    /// the slot frees up.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_ValidTokenButFullSlot_DoesNotConsumeTheToken()
    {
        var context = new TestContext(maxActiveSessions: 1);
        context.SessionRegistry.Create(ClientId.NewId()); // occupies the only slot
        string token = context.TokenAuthenticator.IssueToken();
        byte[] hello = BuildHello(
            context.Codec, Guid.NewGuid().ToString(), "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.OneTimeLocalToken, Token = token });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.RateLimited, error.Code);
        Assert.True(error.Retryable);
        Assert.True(context.TokenAuthenticator.TryValidate(token));
    }

    /// <summary>Verifies the same full-slot rejection for a trusted_device_credential hello, where nothing destructible exists to protect but the rejection itself must still be correct.</summary>
    [Fact]
    public void HandleMessageAsync_ValidCredentialButFullSlot_RejectsWithoutAdmitting()
    {
        var context = new TestContext(maxActiveSessions: 1);
        context.SessionRegistry.Create(ClientId.NewId());
        string clientId = Guid.NewGuid().ToString();
        context.TrustStore.Seed(BuildTrustedRecord(clientId, ValidCredential));
        byte[] hello = BuildHello(
            context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = ValidCredential });

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.RateLimited, error.Code);
        Assert.Equal(1, context.SessionRegistry.ActiveCount);
    }

    /// <summary>
    /// Verifies the invariant the token's reservation lifecycle exists to guarantee at the admission
    /// level: of many concurrent connections presenting one issued one-time token through separate
    /// handler instances sharing the same collaborators (as production composition would), only one is
    /// ever admitted, the rest are rejected as unauthenticated, and the token is unusable afterward.
    /// Session capacity is configured well above the concurrent attempt count specifically so this
    /// bound cannot be accidentally enforced by session capacity instead of the token's own single-use
    /// guarantee.
    /// </summary>
    [Fact]
    public async Task HandleMessageAsync_ConcurrentOneTimeLocalTokenHellosWithSameToken_OnlyOneAdmitted()
    {
        const int concurrentAttempts = 10;
        var sessionRegistry = new FakeSessionRegistry(concurrentAttempts);
        var tokenAuthenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = tokenAuthenticator.IssueToken();
        var trustStore = new FakeTrustStore();
        var credentialThrottle = new TrustedCredentialFailureThrottle(new FakeClock());
        var playContextTracker = new FakePlayContextTracker();
        var codec = new PublicEnvelopeCodec();
        var clock = new FakeClock();

        var fakeConnections = new FakePublicWebSocketConnection[concurrentAttempts];
        var tasks = new Task[concurrentAttempts];
        for (int i = 0; i < concurrentAttempts; i++)
        {
            var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
            fakeConnections[i] = fakeConnection;
            var connection = new PublicConnectionContext(fakeConnection);
            var handler = new PublicHelloAdmissionHandler(
                codec, sessionRegistry, trustStore, tokenAuthenticator, credentialThrottle, playContextTracker, clock);
            byte[] hello = BuildHello(
                codec, Guid.NewGuid().ToString(), $"hello-{i}", new HelloAuthPayload { Method = HelloAuthMethod.OneTimeLocalToken, Token = token });

            tasks[i] = Task.Run(() => handler.HandleMessageAsync(connection, hello, CancellationToken.None));
        }

        await Task.WhenAll(tasks);

        int admittedCount = fakeConnections.Count(connection =>
            connection.SentPayloads.Count > 0 &&
            codec.TryDecode(connection.SentPayloads[0], out PublicEnvelope? envelope) &&
            envelope.MessageType == PublicMessageType.HelloAck);
        Assert.Equal(1, admittedCount);
        Assert.Equal(1, sessionRegistry.ActiveCount);
        Assert.False(tokenAuthenticator.TryValidate(token));
    }

    /// <summary>
    /// Verifies the credential throttle's atomic check-verify-record fix holds through the full
    /// admission wiring, not merely at the throttle's own unit level: of many concurrent
    /// trusted_device_credential hellos all presenting the same wrong (but wire-valid) credential
    /// against one seeded trusted identity, no more than the configured five-failure budget is ever
    /// consumed, and the credential throttle ends up exhausted rather than having let more attempts
    /// through than the bound allows.
    /// </summary>
    [Fact]
    public async Task HandleMessageAsync_ConcurrentMismatchedTrustedDeviceCredentialHellos_NeverExceedsTheThrottleBound()
    {
        const int concurrentAttempts = 10;
        var sessionRegistry = new FakeSessionRegistry(concurrentAttempts);
        var tokenAuthenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        var trustStore = new FakeTrustStore();
        string clientId = Guid.NewGuid().ToString();
        trustStore.Seed(BuildTrustedRecord(clientId, ValidCredential));
        var credentialThrottle = new TrustedCredentialFailureThrottle(new FakeClock());
        var playContextTracker = new FakePlayContextTracker();
        var codec = new PublicEnvelopeCodec();
        var clock = new FakeClock();

        var tasks = new Task[concurrentAttempts];
        for (int i = 0; i < concurrentAttempts; i++)
        {
            var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
            var connection = new PublicConnectionContext(fakeConnection);
            var handler = new PublicHelloAdmissionHandler(
                codec, sessionRegistry, trustStore, tokenAuthenticator, credentialThrottle, playContextTracker, clock);
            byte[] hello = BuildHello(
                codec, clientId, $"hello-{i}", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = WrongButValidCredential });

            tasks[i] = Task.Run(() => handler.HandleMessageAsync(connection, hello, CancellationToken.None));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(0, sessionRegistry.ActiveCount);
        Assert.False(credentialThrottle.IsAllowed());
    }

    // ---- Trust recheck race after admission ----

    /// <summary>
    /// Verifies the recheck-after-admission race: an identity that passed the initial trust check but
    /// is Blocked by the time the post-reservation recheck runs has its session reservation rolled
    /// back and is rejected as blocked, never left admitted.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_BlockedByAdminBetweenInitialCheckAndRecheck_RollsBackAndRejects()
    {
        var sessionRegistry = new FakeSessionRegistry();
        var innerTrustStore = new FakeTrustStore();
        string clientId = Guid.NewGuid().ToString();
        innerTrustStore.Seed(BuildTrustedRecord(clientId, ValidCredential));
        var raceTrustStore = new TrustStoreThatChangesOnSecondLookup(
            innerTrustStore, new TrustRecord(new ClientId(Guid.Parse(clientId)), "AB12", null, KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var codec = new PublicEnvelopeCodec();
        var clock = new FakeClock();
        var handler = new PublicHelloAdmissionHandler(
            codec, sessionRegistry, raceTrustStore, new LocalConnectionTokenAuthenticator(clock),
            new TrustedCredentialFailureThrottle(clock), new FakePlayContextTracker(), clock);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        var connection = new PublicConnectionContext(fakeConnection);
        byte[] hello = BuildHello(codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = ValidCredential });

        handler.HandleMessageAsync(connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(codec, Assert.Single(fakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.Blocked, error.Code);
        Assert.Equal(0, sessionRegistry.ActiveCount);
    }

    // ---- Pre-admission allowlist ----

    /// <summary>Verifies that any non-hello message before admission is rejected as malformed_message.</summary>
    [Fact]
    public void HandleMessageAsync_NonHelloBeforeAdmission_RejectsAsMalformed()
    {
        var context = new TestContext();
        byte[] ping = context.Codec.Encode(PublicMessageType.Ping, "msg-1", null, null, null, null, new object());

        context.Handler.HandleMessageAsync(context.Connection, ping, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, Assert.Single(context.FakeConnection.SentPayloads));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    // ---- Post-admission session-id validation ----

    /// <summary>Verifies that a post-admission message carrying a foreign sessionId is rejected as stale_session.</summary>
    [Fact]
    public void HandleMessageAsync_PostAdmissionForeignSessionId_RejectsAsStaleSession()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out _);

        byte[] foreignSessionMessage = context.Codec.Encode(PublicMessageType.Ping, "msg-2", Guid.NewGuid().ToString(), null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, foreignSessionMessage, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[2]);
        Assert.Equal(PublicProtocolErrorCode.StaleSession, error.Code);
    }

    /// <summary>Verifies that a post-admission message carrying the correct sessionId is accepted (not rejected).</summary>
    [Fact]
    public void HandleMessageAsync_PostAdmissionCorrectSessionId_IsNotRejected()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string admittedSessionId);

        byte[] message = context.Codec.Encode(PublicMessageType.Ping, "msg-2", admittedSessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        Assert.Equal(2, context.FakeConnection.SentPayloads.Count); // only hello_ack + capabilities; no rejection sent
    }

    // ---- Post-admission envelope identity ----

    /// <summary>
    /// Verifies that a post-admission message carrying a foreign envelope-level clientId -- a
    /// structurally valid GUID that simply is not this connection's own admitted identity -- is
    /// rejected as malformed_message rather than silently accepted.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_PostAdmissionForeignClientId_RejectsAsMalformed()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string admittedSessionId);

        byte[] message = context.Codec.Encode(PublicMessageType.Ping, "msg-2", admittedSessionId, null, null, Guid.NewGuid().ToString(), new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[2]);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    /// <summary>Verifies that a post-admission message repeating this connection's own admitted clientId in the envelope is accepted (not rejected).</summary>
    [Fact]
    public void HandleMessageAsync_PostAdmissionMatchingClientId_IsNotRejected()
    {
        var context = new TestContext();
        string clientId = Guid.NewGuid().ToString();
        byte[] hello = BuildHello(context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.Unpaired });
        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);
        (PublicEnvelope ackEnvelope, _) = DecodeSent<HelloAckPayload>(context.Codec, context.FakeConnection.SentPayloads[0]);
        string admittedSessionId = ackEnvelope.SessionId!;

        byte[] message = context.Codec.Encode(PublicMessageType.Ping, "msg-2", admittedSessionId, null, null, clientId, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        Assert.Equal(2, context.FakeConnection.SentPayloads.Count); // only hello_ack + capabilities; no rejection sent
    }

    /// <summary>Verifies that a post-admission message carrying a non-null envelope bridgeInstanceId is rejected as malformed_message. Built from raw JSON for the same reason as the pre-authentication equivalent.</summary>
    [Fact]
    public void HandleMessageAsync_PostAdmissionNonNullBridgeInstanceId_RejectsAsMalformed()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string admittedSessionId);
        string json = $$"""
            {"messageType":"ping","messageId":"msg-2","sessionId":"{{admittedSessionId}}","correlationId":null,"payload":{},"bridgeInstanceId":"a-bridge-instance-id","playContextId":null,"clientId":null}
            """;

        context.Handler.HandleMessageAsync(context.Connection, Encoding.UTF8.GetBytes(json), CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[2]);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    /// <summary>Verifies that a post-admission message carrying a non-null envelope playContextId is rejected as malformed_message -- play context is host-authoritative, never a value a client asserts.</summary>
    [Fact]
    public void HandleMessageAsync_PostAdmissionNonNullPlayContextId_RejectsAsMalformed()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string admittedSessionId);

        byte[] message = context.Codec.Encode(PublicMessageType.Ping, "msg-2", admittedSessionId, null, Guid.NewGuid().ToString(), null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[2]);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    // ---- Post-admission dispatch: capabilities ----

    /// <summary>Verifies that an empty post-admission capabilities advertisement gets no response.</summary>
    [Fact]
    public void HandleMessageAsync_EmptyCapabilitiesPostAdmission_NoResponse()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionId);
        int sentBeforeMessage = context.FakeConnection.SentPayloads.Count;

        byte[] message = context.Codec.Encode(
            PublicMessageType.Capabilities, "msg-2", sessionId, null, null, null, new CapabilitiesPayload { Capabilities = [] });
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        Assert.Equal(sentBeforeMessage, context.FakeConnection.SentPayloads.Count);
    }

    /// <summary>Verifies that a non-empty post-admission capabilities advertisement is rejected as unsupported_capability.</summary>
    [Fact]
    public void HandleMessageAsync_NonEmptyCapabilitiesPostAdmission_RejectsAsUnsupported()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionId);

        using JsonDocument document = JsonDocument.Parse("{}");
        byte[] message = context.Codec.Encode(
            PublicMessageType.Capabilities, "msg-2", sessionId, null, null, null,
            new CapabilitiesPayload { Capabilities = [document.RootElement.Clone()] });
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.UnsupportedCapability, error.Code);
    }

    /// <summary>Verifies that capabilities dispatch also works on a full (paired) session, not only a restricted one.</summary>
    [Fact]
    public void HandleMessageAsync_EmptyCapabilitiesPostAdmission_FullSession_NoResponse()
    {
        var context = new TestContext();
        AdmitViaTrustedDeviceCredentialHello(context, out string sessionId);
        int sentBeforeMessage = context.FakeConnection.SentPayloads.Count;

        byte[] message = context.Codec.Encode(
            PublicMessageType.Capabilities, "msg-2", sessionId, null, null, null, new CapabilitiesPayload { Capabilities = [] });
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        Assert.Equal(sentBeforeMessage, context.FakeConnection.SentPayloads.Count);
    }

    /// <summary>Verifies that a malformed post-admission capabilities message is rejected as malformed_message.</summary>
    [Fact]
    public void HandleMessageAsync_MalformedCapabilitiesPostAdmission_RejectsAsMalformed()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionId);
        string json = $$"""
            {"messageType":"capabilities","messageId":"msg-2","sessionId":"{{sessionId}}","correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;

        context.Handler.HandleMessageAsync(context.Connection, Encoding.UTF8.GetBytes(json), CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    // ---- Post-admission dispatch: subscribe and snapshot_request ----

    /// <summary>Verifies that subscribe rejects every requested area, since no state area is currently registered.</summary>
    [Fact]
    public void HandleMessageAsync_SubscribePostAdmission_RejectsEveryRequestedArea()
    {
        var context = new TestContext();
        AdmitViaTrustedDeviceCredentialHello(context, out string sessionId);

        byte[] message = context.Codec.Encode(
            PublicMessageType.Subscribe, "msg-2", sessionId, null, null, null,
            new SubscribePayload { StateAreas = ["area_one", "area_two"] });
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (PublicEnvelope ackEnvelope, SubscriptionAckPayload ack) = DecodeSent<SubscriptionAckPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicMessageType.SubscriptionAck, ackEnvelope.MessageType);
        Assert.Equal("msg-2", ackEnvelope.CorrelationId);
        Assert.Empty(ack.AcceptedStateAreas);
        Assert.Equal(["area_one", "area_two"], ack.RejectedStateAreas);
    }

    /// <summary>Verifies that a malformed post-admission subscribe message is rejected as malformed_message.</summary>
    [Fact]
    public void HandleMessageAsync_MalformedSubscribePostAdmission_RejectsAsMalformed()
    {
        var context = new TestContext();
        AdmitViaTrustedDeviceCredentialHello(context, out string sessionId);
        string json = $$"""
            {"messageType":"subscribe","messageId":"msg-2","sessionId":"{{sessionId}}","correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;

        context.Handler.HandleMessageAsync(context.Connection, Encoding.UTF8.GetBytes(json), CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    /// <summary>Verifies that snapshot_request is always rejected as unsupported_capability, since no state area is currently registered.</summary>
    [Fact]
    public void HandleMessageAsync_SnapshotRequestPostAdmission_RejectsAsUnsupported()
    {
        var context = new TestContext();
        AdmitViaTrustedDeviceCredentialHello(context, out string sessionId);

        byte[] message = context.Codec.Encode(
            PublicMessageType.SnapshotRequest, "msg-2", sessionId, null, null, null,
            new SnapshotRequestPayload { StateArea = "area_one" });
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.UnsupportedCapability, error.Code);
    }

    /// <summary>Verifies that a malformed post-admission snapshot_request message is rejected as malformed_message.</summary>
    [Fact]
    public void HandleMessageAsync_MalformedSnapshotRequestPostAdmission_RejectsAsMalformed()
    {
        var context = new TestContext();
        AdmitViaTrustedDeviceCredentialHello(context, out string sessionId);
        string json = $$"""
            {"messageType":"snapshot_request","messageId":"msg-2","sessionId":"{{sessionId}}","correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}
            """;

        context.Handler.HandleMessageAsync(context.Connection, Encoding.UTF8.GetBytes(json), CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    // ---- Post-admission dispatch: per-tier allowlist ----

    /// <summary>Verifies that a restricted session's approved pairing/liveness messages pass the allowlist and produce no response yet (dispatch is a later concept's scope).</summary>
    [Theory]
    [InlineData(PublicMessageType.Ping)]
    [InlineData(PublicMessageType.PairingRequest)]
    [InlineData(PublicMessageType.PairingConfirm)]
    [InlineData(PublicMessageType.PairingAck)]
    [InlineData(PublicMessageType.PairingRenotify)]
    [InlineData(PublicMessageType.PairingCancel)]
    public void HandleMessageAsync_RestrictedSessionAllowedType_PassesAllowlistWithNoResponse(PublicMessageType messageType)
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionId);
        int sentBeforeMessage = context.FakeConnection.SentPayloads.Count;

        byte[] message = context.Codec.Encode(messageType, "msg-2", sessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        Assert.Equal(sentBeforeMessage, context.FakeConnection.SentPayloads.Count);
    }

    /// <summary>
    /// Verifies that a restricted session's full-session-only messages -- structurally valid,
    /// post-admission client vocabulary the current tier simply does not authorize -- are rejected as
    /// unauthorized, distinct from a protocol shape/direction violation.
    /// </summary>
    [Theory]
    [InlineData(PublicMessageType.RenameRequest)]
    [InlineData(PublicMessageType.Subscribe)]
    [InlineData(PublicMessageType.SnapshotRequest)]
    public void HandleMessageAsync_RestrictedSessionDisallowedClientMessageType_RejectsAsUnauthorized(PublicMessageType messageType)
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionId);

        byte[] message = context.Codec.Encode(messageType, "msg-2", sessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.Unauthorized, error.Code);
    }

    /// <summary>
    /// Verifies that a restricted session's server-originated message types are rejected as
    /// malformed_message rather than unauthorized -- no trust tier could ever authorize a client to
    /// send a message only the host originates, so this is a protocol shape/direction violation, not a
    /// trust-tier authorization failure.
    /// </summary>
    [Theory]
    [InlineData(PublicMessageType.HelloAck)]
    [InlineData(PublicMessageType.Error)]
    [InlineData(PublicMessageType.Pong)]
    [InlineData(PublicMessageType.StateSnapshot)]
    public void HandleMessageAsync_RestrictedSessionServerOnlyType_RejectsAsMalformed(PublicMessageType messageType)
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionId);

        byte[] message = context.Codec.Encode(messageType, "msg-2", sessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    /// <summary>Verifies that a full session's approved liveness/rename messages pass the allowlist, and rename_request produces no response yet (dispatch is a later concept's scope).</summary>
    [Theory]
    [InlineData(PublicMessageType.Ping)]
    [InlineData(PublicMessageType.RenameRequest)]
    public void HandleMessageAsync_FullSessionAllowedType_PassesAllowlistWithNoResponse(PublicMessageType messageType)
    {
        var context = new TestContext();
        AdmitViaTrustedDeviceCredentialHello(context, out string sessionId);
        int sentBeforeMessage = context.FakeConnection.SentPayloads.Count;

        byte[] message = context.Codec.Encode(messageType, "msg-2", sessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        Assert.Equal(sentBeforeMessage, context.FakeConnection.SentPayloads.Count);
    }

    /// <summary>
    /// Verifies that a full (already-paired) session's restricted-only pairing messages -- structurally
    /// valid, post-admission client vocabulary this tier does not authorize since it has no reason to
    /// re-pair -- are rejected as unauthorized.
    /// </summary>
    [Theory]
    [InlineData(PublicMessageType.PairingRequest)]
    [InlineData(PublicMessageType.PairingConfirm)]
    [InlineData(PublicMessageType.PairingAck)]
    [InlineData(PublicMessageType.PairingRenotify)]
    [InlineData(PublicMessageType.PairingCancel)]
    public void HandleMessageAsync_FullSessionDisallowedClientMessageType_RejectsAsUnauthorized(PublicMessageType messageType)
    {
        var context = new TestContext();
        AdmitViaTrustedDeviceCredentialHello(context, out string sessionId);

        byte[] message = context.Codec.Encode(messageType, "msg-2", sessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.Unauthorized, error.Code);
    }

    /// <summary>
    /// Verifies that a full session's server-originated message types are rejected as
    /// malformed_message rather than unauthorized, for symmetry with the restricted-session case: no
    /// trust tier could ever authorize a client to send a host-originated type.
    /// </summary>
    [Theory]
    [InlineData(PublicMessageType.HelloAck)]
    [InlineData(PublicMessageType.Error)]
    [InlineData(PublicMessageType.Pong)]
    [InlineData(PublicMessageType.StateSnapshot)]
    public void HandleMessageAsync_FullSessionServerOnlyType_RejectsAsMalformed(PublicMessageType messageType)
    {
        var context = new TestContext();
        AdmitViaTrustedDeviceCredentialHello(context, out string sessionId);

        byte[] message = context.Codec.Encode(messageType, "msg-2", sessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, error.Code);
    }

    // ---- Replay protection ----

    /// <summary>Verifies that a repeated messageId is rejected as replayed_message.</summary>
    [Fact]
    public void HandleMessageAsync_RepeatedMessageId_RejectsAsReplayed()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionId);

        byte[] first = context.Codec.Encode(PublicMessageType.Ping, "repeat-id", sessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, first, CancellationToken.None);
        byte[] second = context.Codec.Encode(PublicMessageType.Ping, "repeat-id", sessionId, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, second, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.ReplayedMessage, error.Code);
    }

    /// <summary>Verifies that a hello's own messageId is itself tracked, so a client cannot resend an identical hello frame verbatim.</summary>
    [Fact]
    public void HandleMessageAsync_HelloMessageIdReused_SecondAttemptRejectedAsReplayed()
    {
        var context = new TestContext(maxActiveSessions: 2);
        byte[] hello = BuildHello(context.Codec, Guid.NewGuid().ToString(), "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.Unpaired });
        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        (_, ErrorPayload error) = DecodeSent<ErrorPayload>(context.Codec, context.FakeConnection.SentPayloads[^1]);
        Assert.Equal(PublicProtocolErrorCode.ReplayedMessage, error.Code);
    }

    /// <summary>
    /// Verifies that the session is closed once it reaches the 10,000-distinct-message bound, per
    /// <c>ai/context/protocol/security.md</c>'s "the bridge closes the session before this bound is
    /// exceeded" -- the message that reaches the bound is itself still accepted (no replay rejection),
    /// but the connection is asked to close.
    /// </summary>
    [Fact]
    public void HandleMessageAsync_SessionReachesMessageBound_ClosesConnection()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionId);
        int sentBeforeLoop = context.FakeConnection.SentPayloads.Count;

        // The admitting hello already counts as message 1 of the bound; send the remaining 9,999.
        for (int i = 0; i < Constants.PublicProtocolMaxSessionMessages - 1; i++)
        {
            byte[] message = context.Codec.Encode(PublicMessageType.Ping, $"msg-{i}", sessionId, null, null, null, new object());
            context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);
        }

        Assert.Equal(1, context.FakeConnection.RequestCloseCalls);
        Assert.Equal(sentBeforeLoop, context.FakeConnection.SentPayloads.Count); // none of the bound-filling messages were rejected
    }

    // ---- Protocol-violation close policy ----

    /// <summary>Verifies that the third protocol violation within the close-policy window closes the connection.</summary>
    [Fact]
    public void HandleMessageAsync_ThirdViolationWithinWindow_ClosesConnection()
    {
        var context = new TestContext();

        SendMalformedPreHelloMessage(context, "v1");
        SendMalformedPreHelloMessage(context, "v2");
        Assert.Equal(0, context.FakeConnection.RequestCloseCalls);
        SendMalformedPreHelloMessage(context, "v3");

        Assert.Equal(1, context.FakeConnection.RequestCloseCalls);
    }

    /// <summary>Verifies that violations outside the close-policy window are pruned and do not accumulate toward closure.</summary>
    [Fact]
    public void HandleMessageAsync_ViolationsOutsideWindow_DoNotAccumulate()
    {
        var context = new TestContext();

        SendMalformedPreHelloMessage(context, "v1");
        SendMalformedPreHelloMessage(context, "v2");
        context.Clock.Advance(TimeSpan.FromSeconds(31));
        SendMalformedPreHelloMessage(context, "v3");

        Assert.Equal(0, context.FakeConnection.RequestCloseCalls);
    }

    /// <summary>Sends a malformed pre-hello message (any non-hello type) with the given messageId.</summary>
    private static void SendMalformedPreHelloMessage(TestContext context, string messageId)
    {
        byte[] message = context.Codec.Encode(PublicMessageType.Ping, messageId, null, null, null, null, new object());
        context.Handler.HandleMessageAsync(context.Connection, message, CancellationToken.None);
    }

    // ---- Pre-authentication admission deadline ----

    /// <summary>Verifies that a connection which never sends a valid hello is closed once the admission deadline elapses.</summary>
    [Fact]
    public async Task HandleConnectionEstablished_NoHelloWithinDeadline_ClosesConnection()
    {
        var context = new TestContext(admissionDeadline: TimeSpan.FromMilliseconds(50));

        context.Handler.HandleConnectionEstablished(context.Connection);
        await WaitUntilAsync(() => context.FakeConnection.RequestCloseCalls > 0);

        Assert.Equal(1, context.FakeConnection.RequestCloseCalls);
    }

    /// <summary>Verifies that a hello admitted before the deadline cancels it, so the connection is never later closed by that former deadline.</summary>
    [Fact]
    public async Task HandleConnectionEstablished_HelloAdmittedBeforeDeadline_DeadlineNeverFires()
    {
        var context = new TestContext(admissionDeadline: TimeSpan.FromMilliseconds(50));
        context.Handler.HandleConnectionEstablished(context.Connection);

        byte[] hello = BuildHello(context.Codec, Guid.NewGuid().ToString(), "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.Unpaired });
        await context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Equal(0, context.FakeConnection.RequestCloseCalls);
        Assert.Equal(1, context.SessionRegistry.ActiveCount);
    }

    /// <summary>Verifies two handler instances' admission deadlines are entirely independent -- one firing never affects the other's connection.</summary>
    [Fact]
    public async Task HandleConnectionEstablished_TwoIndependentConnections_DeadlinesDoNotCrossAffect()
    {
        var contextA = new TestContext(admissionDeadline: TimeSpan.FromMilliseconds(50));
        var contextB = new TestContext(admissionDeadline: TimeSpan.FromSeconds(30));
        contextA.Handler.HandleConnectionEstablished(contextA.Connection);
        contextB.Handler.HandleConnectionEstablished(contextB.Connection);

        await WaitUntilAsync(() => contextA.FakeConnection.RequestCloseCalls > 0);

        Assert.Equal(1, contextA.FakeConnection.RequestCloseCalls);
        Assert.Equal(0, contextB.FakeConnection.RequestCloseCalls);
    }

    // ---- HandleConnectionEnded / HandleDisconnectedAsync ----

    /// <summary>Verifies that ending a connection before any admission does not invalidate any session, including an unrelated one already active in the same registry.</summary>
    [Fact]
    public void HandleConnectionEnded_BeforeAdmission_DoesNotInvalidateAnything()
    {
        var context = new TestContext(maxActiveSessions: 2);
        SessionId unrelatedSession = context.SessionRegistry.Create(ClientId.NewId());

        context.Handler.HandleConnectionEnded();

        Assert.Equal(1, context.SessionRegistry.ActiveCount);
        Assert.True(context.SessionRegistry.IsActive(unrelatedSession, context.SessionRegistry.ConnectionIdFor(unrelatedSession)));
    }

    /// <summary>Verifies that ending a connection after admission invalidates its session.</summary>
    [Fact]
    public void HandleConnectionEnded_AfterAdmission_InvalidatesTheSession()
    {
        var context = new TestContext();
        AdmitViaUnpairedHello(context, out string sessionIdText);

        context.Handler.HandleConnectionEnded();

        Assert.False(context.SessionRegistry.IsActive(new SessionId(Guid.Parse(sessionIdText)), context.SessionRegistry.ConnectionIdFor(new SessionId(Guid.Parse(sessionIdText)))));
    }

    /// <summary>Verifies that HandleDisconnectedAsync completes immediately without throwing.</summary>
    [Fact]
    public async Task HandleDisconnectedAsync_CompletesImmediately()
    {
        var context = new TestContext();

        await context.Handler.HandleDisconnectedAsync(CancellationToken.None);
    }

    // ---- Helpers ----

    /// <summary>Builds a complete wire-encoded <c>hello</c> message with the given clientId, messageId, and auth payload.</summary>
    private static byte[] BuildHello(PublicEnvelopeCodec codec, string clientId, string messageId, HelloAuthPayload auth) =>
        codec.Encode(PublicMessageType.Hello, messageId, null, null, null, null,
            new HelloPayload { Endpoint = "client", ClientId = clientId, Auth = auth });

    /// <summary>Builds a Trusted trust record for <paramref name="clientId"/> whose verifier matches <paramref name="credential"/>.</summary>
    private static TrustRecord BuildTrustedRecord(string clientId, string credential) =>
        new(new ClientId(Guid.Parse(clientId)), "AB12", null, KnownDeviceState.Trusted, CredentialHasher.Hash(credential), DateTimeOffset.UtcNow);

    /// <summary>Decodes a sent wire message's envelope and typed payload, failing the test if either step does not succeed.</summary>
    private static (PublicEnvelope Envelope, TPayload Payload) DecodeSent<TPayload>(PublicEnvelopeCodec codec, byte[] bytes) where TPayload : class
    {
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.True(codec.TryDecodePayload(envelope, out TPayload? payload));
        return (envelope, payload);
    }

    /// <summary>Admits a session via an unpaired hello and returns the wire session id assigned.</summary>
    private static void AdmitViaUnpairedHello(TestContext context, out string sessionId)
    {
        byte[] hello = BuildHello(context.Codec, Guid.NewGuid().ToString(), "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.Unpaired });
        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);
        (PublicEnvelope ackEnvelope, _) = DecodeSent<HelloAckPayload>(context.Codec, context.FakeConnection.SentPayloads[0]);
        sessionId = ackEnvelope.SessionId!;
    }

    /// <summary>Admits a full (paired) session via a matching trusted_device_credential hello, seeding the trust store first, and returns the wire session id assigned.</summary>
    private static void AdmitViaTrustedDeviceCredentialHello(TestContext context, out string sessionId)
    {
        string clientId = Guid.NewGuid().ToString();
        context.TrustStore.Seed(BuildTrustedRecord(clientId, ValidCredential));
        byte[] hello = BuildHello(
            context.Codec, clientId, "hello-1", new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = ValidCredential });
        context.Handler.HandleMessageAsync(context.Connection, hello, CancellationToken.None);
        (PublicEnvelope ackEnvelope, _) = DecodeSent<HelloAckPayload>(context.Codec, context.FakeConnection.SentPayloads[0]);
        sessionId = ackEnvelope.SessionId!;
    }

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

    /// <summary>Bundles a fresh handler and its collaborators for one test.</summary>
    private sealed class TestContext
    {
        /// <summary>The session registry the handler under test admits and invalidates sessions through.</summary>
        public FakeSessionRegistry SessionRegistry { get; }

        /// <summary>The trust store the handler under test looks up persisted trust through.</summary>
        public FakeTrustStore TrustStore { get; } = new();

        /// <summary>The clock shared by every collaborator that needs one, so a test can advance it deterministically.</summary>
        public FakeClock Clock { get; } = new();

        /// <summary>The developer-token authenticator the handler under test validates <c>one_time_local_token</c> hellos through.</summary>
        public LocalConnectionTokenAuthenticator TokenAuthenticator { get; }

        /// <summary>The throttle the handler under test records failed <c>trusted_device_credential</c> attempts through.</summary>
        public TrustedCredentialFailureThrottle CredentialThrottle { get; }

        /// <summary>The play-context tracker the handler under test reads its outbound <c>playContextId</c> snapshot from.</summary>
        public FakePlayContextTracker PlayContextTracker { get; } = new();

        /// <summary>The real, stateless codec used both by the handler under test and by this test to build inbound bytes and decode outbound ones.</summary>
        public PublicEnvelopeCodec Codec { get; } = new();

        /// <summary>The underlying fake connection <see cref="Connection"/> wraps, exposing sent payloads and close-request calls for assertions.</summary>
        public FakePublicWebSocketConnection FakeConnection { get; } = new(Stream.Null) { TrySendResult = true };

        /// <summary>The connection capability passed to the handler under test, wrapping <see cref="FakeConnection"/> the same way production composition would.</summary>
        public IPublicConnectionContext Connection { get; }

        /// <summary>The handler under test.</summary>
        public PublicHelloAdmissionHandler Handler { get; }

        /// <summary>Creates a fresh handler and its collaborators.</summary>
        /// <param name="maxActiveSessions">The session registry's admission bound.</param>
        /// <param name="admissionDeadline">The handler's own pre-authentication admission deadline.</param>
        public TestContext(int maxActiveSessions = int.MaxValue, TimeSpan? admissionDeadline = null)
        {
            SessionRegistry = new FakeSessionRegistry(maxActiveSessions);
            TokenAuthenticator = new LocalConnectionTokenAuthenticator(Clock);
            CredentialThrottle = new TrustedCredentialFailureThrottle(Clock);
            Connection = new PublicConnectionContext(FakeConnection);
            Handler = new PublicHelloAdmissionHandler(
                Codec, SessionRegistry, TrustStore, TokenAuthenticator, CredentialThrottle, PlayContextTracker, Clock, admissionDeadline);
        }
    }

    /// <summary>
    /// A narrow <see cref="ITrustStore"/> decorator used only to simulate the recheck-after-admission
    /// race: returns the wrapped store's real value on the first <see cref="TryGet"/> call and a
    /// fixed replacement record on every call after, as if an administrative mutation landed between
    /// this handler's initial check and its post-reservation recheck. The handler under test never
    /// calls any other <see cref="ITrustStore"/> member, so those are not implemented.
    /// </summary>
    private sealed class TrustStoreThatChangesOnSecondLookup : ITrustStore
    {
        /// <summary>The real store backing the first <see cref="TryGet"/> call.</summary>
        private readonly ITrustStore inner;

        /// <summary>The record returned by every <see cref="TryGet"/> call after the first.</summary>
        private readonly TrustRecord recordAfterFirstLookup;

        /// <summary>The number of <see cref="TryGet"/> calls made so far.</summary>
        private int callCount;

        /// <summary>Creates a decorator that changes its answer starting from the second lookup.</summary>
        /// <param name="inner">The real store backing the first call.</param>
        /// <param name="recordAfterFirstLookup">The record returned by every call after the first.</param>
        public TrustStoreThatChangesOnSecondLookup(ITrustStore inner, TrustRecord recordAfterFirstLookup)
        {
            this.inner = inner;
            this.recordAfterFirstLookup = recordAfterFirstLookup;
        }

        /// <inheritdoc/>
        public TrustRecord? TryGet(ClientId clientId)
        {
            callCount++;
            return callCount == 1 ? inner.TryGet(clientId) : recordAfterFirstLookup;
        }

        /// <summary>Not called by the handler under test.</summary>
        public IReadOnlyList<TrustRecord> List() => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public Task UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public Task ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public long MutationGeneration => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public Task<bool> TryUpsertIfGenerationAsync(TrustRecord record, long expectedGeneration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public TrustRecord? TryGetByShortId(string shortId) => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public Task<TrustMutationOutcome> RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public Task<TrustMutationOutcome> BlockAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public Task<TrustMutationOutcome> UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public Task<TrustMutationOutcome> ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Not called by the handler under test.</summary>
        public Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
