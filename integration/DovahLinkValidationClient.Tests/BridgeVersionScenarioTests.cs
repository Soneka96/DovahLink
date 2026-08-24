using System.Net.WebSockets;
using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises the hello/hello_ack bootstrap and its Bridge-version compatibility fields.
/// Most fixtures here build the Bridge's side of the exchange (hello_ack, error, capabilities,
/// subscription_ack, state_snapshot) for a <see cref="FakeWebSocket"/> to feed into
/// <see cref="ValidationRun.ExecuteAsync"/>, or read back the client's own already-sent hello. The
/// corresponding payload DTOs are one-directional to match this client's real usage (e.g.
/// <c>HelloAckPayload</c> is decode-only because the real client never sends one, and
/// <c>HelloPayload</c> is encode-only because it never decodes its own), so they cover neither the
/// fake side nor the sent-message readback and those sites stay as raw JsonObject.</summary>
public class BridgeVersionScenarioTests
{
    /// <summary>A valid one-time token for the bootstrap harness.</summary>
    private const string ValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

    /// <summary>The loopback endpoint exposed by the bootstrap harness.</summary>
    private static readonly Uri BridgeUri = new("ws://127.0.0.1:58231/");

    /// <summary>Verifies that hello receives an acknowledged session and bridge version.</summary>
    [Fact]
    public async Task HelloReceivesHelloAckWithSessionIdAndBridgeVersion()
    {
        using var harness = new HarnessProcess(ValidHexToken);
        await harness.WaitForReadyAsync();

        Envelope helloAck;
        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri))
        {
            var helloPayload = new HelloPayload("client-1", new HelloAuthPayload("one_time_local_token", ValidHexToken));
            await connection.SendAsync(new Envelope("hello", "message-hello-1", null, null, helloPayload.Encode()));

            helloAck = await connection.ReceiveAsync();
            // Closed here, before "quit": Coordinator::Shutdown() waits for
            // any still-connected session's thread to exit, which otherwise
            // only happens after the 60s idle timeout (bridge/README.md).
        }

        Assert.Equal("hello_ack", helloAck.MessageType);
        Assert.False(string.IsNullOrEmpty(helloAck.SessionId));
        Assert.Equal("message-hello-1", helloAck.CorrelationId);
        Assert.False(string.IsNullOrEmpty(HelloAckPayload.Decode(helloAck.Payload).BridgeVersion));

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, harness.ExitCode);
    }

    /// <summary>Verifies that hello_ack carries the bridge's identity and echoes the client's clientId.</summary>
    [Fact]
    public async Task HelloAckCarriesBridgeIdentityAndClientId()
    {
        using var harness = new HarnessProcess(ValidHexToken);
        string bridgeInstanceId = await harness.WaitForReadyAsync();
        string clientId = ClientIdentity.Current.ToString();

        Envelope helloAck;
        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri))
        {
            await connection.SendAsync(BridgeScenario.HelloEnvelope(ValidHexToken, clientId: clientId));
            helloAck = await connection.ReceiveAsync();
        }

        Assert.Equal("hello_ack", helloAck.MessageType);
        Assert.Equal("unpaired", HelloAckPayload.Decode(helloAck.Payload).ClientIdentityKind);
        Assert.Equal(bridgeInstanceId, helloAck.BridgeInstanceId);
        Assert.Equal(clientId, helloAck.ClientId);

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when
    /// hello_ack accepts a different client identity than the one requested.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenHelloAckClientIdIsMismatched()
    {
        string helloAckWithMismatchedClientId = new Envelope(
            "hello_ack", "message-ack-1", "session-1", "message-hello-1",
            new JsonObject { ["bridgeVersion"] = "0.2.0", ["clientIdentityKind"] = "unpaired" },
            ClientId: "some-other-client").Encode();
        // Only the hello_ack is queued: if ValidationRun incorrectly continued past the identity
        // check, its next ReceiveAsync (for capabilities) would dequeue from an empty queue and
        // throw, failing this test.
        var socket = new FakeWebSocket([helloAckWithMismatchedClientId]);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("different client identity", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when
    /// hello_ack carries no client identity at all.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenHelloAckClientIdIsMissing()
    {
        string helloAckWithoutClientId = new Envelope(
            "hello_ack", "message-ack-1", "session-1", "message-hello-1",
            new JsonObject { ["bridgeVersion"] = "0.2.0", ["clientIdentityKind"] = "unpaired" }).Encode();
        var socket = new FakeWebSocket([helloAckWithoutClientId]);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("got null", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies that ValidationRun sends its stable process-lifetime identity as the hello
    /// clientId, rather than a fresh identity per call.</summary>
    [Fact]
    public async Task ExecuteAsyncSendsTheStableClientIdentityInHello()
    {
        var socket = new FakeWebSocket(receiveException: new WebSocketException("peer reset"));
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Envelope hello = Envelope.Decode(Assert.Single(socket.SentMessages));
        Assert.Equal(ClientIdentity.Current.ToString(), hello.Payload["clientId"]!.GetValue<string>());
    }

    /// <summary>Verifies that ValidationRun rejects an unaccepted client identity before it would
    /// otherwise reject an incompatible Bridge version, proving the identity check runs first.</summary>
    [Fact]
    public async Task ExecuteAsyncRejectsIdentityMismatchBeforeCheckingBridgeVersionCompatibility()
    {
        string helloAckWithMismatchedClientIdAndUnsupportedVersion = new Envelope(
            "hello_ack", "message-ack-1", "session-1", "message-hello-1",
            new JsonObject { ["bridgeVersion"] = "99.0.0", ["clientIdentityKind"] = "unpaired" },
            ClientId: "some-other-client").Encode();
        var socket = new FakeWebSocket([helloAckWithMismatchedClientIdAndUnsupportedVersion]);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("different client identity", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("incompatible", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when the
    /// Bridge reports a version outside this client's supported range.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenTheBridgeVersionIsUnsupported()
    {
        string helloAckWithUnsupportedVersion = new Envelope(
            "hello_ack", "message-ack-1", "session-1", "message-hello-1",
            new JsonObject { ["bridgeVersion"] = "99.0.0", ["clientIdentityKind"] = "unpaired" },
            ClientId: ClientIdentity.Current.ToString()).Encode();
        // Only the hello_ack is queued: if ValidationRun incorrectly continued past the
        // compatibility check, its next ReceiveAsync (for capabilities) would dequeue from an
        // empty queue and throw, failing this test.
        var socket = new FakeWebSocket([helloAckWithUnsupportedVersion]);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("incompatible", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when
    /// hello_ack is missing its bridgeVersion field entirely.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenTheBridgeVersionIsMissing()
    {
        string helloAckWithoutVersion = new Envelope(
            "hello_ack", "message-ack-1", "session-1", "message-hello-1", new JsonObject(),
            ClientId: ClientIdentity.Current.ToString()).Encode();
        var socket = new FakeWebSocket([helloAckWithoutVersion]);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("incompatible", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies that ValidationRun reports failure without a compatibility check when the
    /// Bridge rejects the hello itself.</summary>
    [Fact]
    public async Task ExecuteAsyncFailsWithoutCheckingCompatibilityWhenTheBridgeRejectsHello()
    {
        string errorEnvelope = new Envelope(
            "error", "message-error-1", null, "message-hello-1",
            new JsonObject { ["code"] = "unauthenticated", ["message"] = "Invalid token", ["retryable"] = false })
            .Encode();
        var socket = new FakeWebSocket([errorEnvelope]);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bridge rejected hello", output.ToString());
    }

    /// <summary>Verifies that ValidationRun reports failure instead of throwing when the connection
    /// ends before a complete hello_ack is received.</summary>
    [Fact]
    public async Task ExecuteAsyncReturnsFailureWhenTheConnectionEndsBeforeHelloAck()
    {
        var socket = new FakeWebSocket(receiveException: new WebSocketException("peer reset"));
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bridge connection failed", output.ToString());
    }

    /// <summary>Verifies that ValidationRun reports failure instead of throwing when hello_ack is
    /// missing a required envelope field.</summary>
    [Fact]
    public async Task ExecuteAsyncReturnsFailureWhenHelloAckIsMalformed()
    {
        const string malformedHelloAck =
            """{"sessionId":"session-1","correlationId":"message-hello-1","payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}""";
        var socket = new FakeWebSocket([malformedHelloAck]);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bridge connection failed", output.ToString());
    }

    /// <summary>Verifies that ValidationRun reports failure instead of throwing when sending hello
    /// itself fails.</summary>
    [Fact]
    public async Task ExecuteAsyncReturnsFailureWhenSendingHelloFails()
    {
        var socket = new FakeWebSocket(sendException: new WebSocketException("connection reset"));
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bridge connection failed", output.ToString());
    }

    /// <summary>Verifies that ValidationRun reports failure instead of throwing when receiving
    /// hello_ack times out.</summary>
    [Fact]
    public async Task ExecuteAsyncReturnsFailureWhenReceivingHelloAckTimesOut()
    {
        var socket = new FakeWebSocket(receiveException: new OperationCanceledException("receive timed out"));
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bridge connection failed", output.ToString());
    }

    /// <summary>Verifies that ValidationRun reports failure instead of throwing when hello_ack
    /// carries no sessionId, violating the connection wrapper's own protocol contract.</summary>
    [Fact]
    public async Task ExecuteAsyncReturnsFailureWhenHelloAckCarriesNoSessionId()
    {
        string helloAckWithoutSessionId = new Envelope(
            "hello_ack", "message-ack-1", null, "message-hello-1",
            new JsonObject { ["bridgeVersion"] = "0.2.0", ["clientIdentityKind"] = "unpaired" },
            ClientId: ClientIdentity.Current.ToString()).Encode();
        var socket = new FakeWebSocket([helloAckWithoutSessionId]);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bridge connection failed", output.ToString());
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when the
    /// Bridge sends an unexpected message instead of capabilities.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenCapabilitiesMessageTypeIsUnexpected()
    {
        const string sessionId = "session-1";
        string supportedVersion = BridgeVersionCompatibility.MinimumSupportedVersion.ToString();
        string[] responses =
        [
            new Envelope("hello_ack", "message-ack-1", sessionId, "message-hello-1",
                new JsonObject { ["bridgeVersion"] = supportedVersion, ["clientIdentityKind"] = "unpaired" },
                ClientId: ClientIdentity.Current.ToString()).Encode(),
            new Envelope("error", "message-error-1", sessionId, null,
                new JsonObject { ["code"] = "rate_limited", ["message"] = "Too many requests", ["retryable"] = true })
                .Encode(),
        ];
        var socket = new FakeWebSocket(responses);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("instead of capabilities", output.ToString(), StringComparison.OrdinalIgnoreCase);
        // Only "hello" was sent: proves ExecuteAsync stopped at the capabilities check rather
        // than also sending "subscribe" before returning.
        Assert.Single(socket.SentMessages);
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when the
    /// Bridge sends an unexpected message instead of subscription_ack.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenSubscriptionAckMessageTypeIsUnexpected()
    {
        const string sessionId = "session-1";
        string supportedVersion = BridgeVersionCompatibility.MinimumSupportedVersion.ToString();
        string[] responses =
        [
            new Envelope("hello_ack", "message-ack-1", sessionId, "message-hello-1",
                new JsonObject { ["bridgeVersion"] = supportedVersion, ["clientIdentityKind"] = "unpaired" },
                ClientId: ClientIdentity.Current.ToString()).Encode(),
            new Envelope("capabilities", "message-cap-1", sessionId, null, new CapabilitiesPayload([]).Encode()).Encode(),
            new Envelope("error", "message-error-1", sessionId, null,
                new JsonObject { ["code"] = "rate_limited", ["message"] = "Too many requests", ["retryable"] = true })
                .Encode(),
        ];
        var socket = new FakeWebSocket(responses);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("instead of subscription_ack", output.ToString(), StringComparison.OrdinalIgnoreCase);
        // "hello" and "subscribe" were sent, and nothing after: ExecuteAsync never sends a third
        // message, so this count alone proves no further request followed the failed check.
        Assert.Equal(2, socket.SentMessages.Count);
        // The next check down (acceptedStateAreas) would independently reject this same fixture
        // and produce the same exit code and send count: only this negative assertion proves
        // execution didn't fall through into it after a dropped return.
        Assert.DoesNotContain("did not accept", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when the
    /// Bridge rejects the character state subscription.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenCharacterSubscriptionIsRejected()
    {
        const string sessionId = "session-1";
        string supportedVersion = BridgeVersionCompatibility.MinimumSupportedVersion.ToString();
        string[] responses =
        [
            new Envelope("hello_ack", "message-ack-1", sessionId, "message-hello-1",
                new JsonObject { ["bridgeVersion"] = supportedVersion, ["clientIdentityKind"] = "unpaired" },
                ClientId: ClientIdentity.Current.ToString()).Encode(),
            new Envelope("capabilities", "message-cap-1", sessionId, null, new CapabilitiesPayload([]).Encode()).Encode(),
            new Envelope("subscription_ack", "message-sub-ack-1", sessionId, null,
                new JsonObject { ["acceptedStateAreas"] = new JsonArray(), ["rejectedStateAreas"] = new JsonArray("character") })
                .Encode(),
        ];
        var socket = new FakeWebSocket(responses);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("did not accept the character state subscription", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, socket.SentMessages.Count);
        // A dropped return here falls through to a 4th ReceiveAsync on an exhausted queue, which
        // the generic catch also turns into exit code 1 with the same send count: only this
        // negative assertion proves execution didn't fall through into that catch.
        Assert.DoesNotContain("Bridge connection failed", output.ToString());
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when
    /// subscription_ack omits acceptedStateAreas entirely.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenAcceptedStateAreasIsMissing()
    {
        const string sessionId = "session-1";
        string supportedVersion = BridgeVersionCompatibility.MinimumSupportedVersion.ToString();
        string[] responses =
        [
            new Envelope("hello_ack", "message-ack-1", sessionId, "message-hello-1",
                new JsonObject { ["bridgeVersion"] = supportedVersion, ["clientIdentityKind"] = "unpaired" },
                ClientId: ClientIdentity.Current.ToString()).Encode(),
            new Envelope("capabilities", "message-cap-1", sessionId, null, new CapabilitiesPayload([]).Encode()).Encode(),
            new Envelope("subscription_ack", "message-sub-ack-1", sessionId, null, new JsonObject()).Encode(),
        ];
        var socket = new FakeWebSocket(responses);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("did not accept the character state subscription", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, socket.SentMessages.Count);
        Assert.DoesNotContain("Bridge connection failed", output.ToString());
    }

    /// <summary>Verifies that ValidationRun disconnects without continuing the exchange when the
    /// Bridge sends an unexpected message instead of state_snapshot.</summary>
    [Fact]
    public async Task ExecuteAsyncDisconnectsWithoutContinuingWhenStateSnapshotMessageTypeIsUnexpected()
    {
        const string sessionId = "session-1";
        string supportedVersion = BridgeVersionCompatibility.MinimumSupportedVersion.ToString();
        string[] responses =
        [
            new Envelope("hello_ack", "message-ack-1", sessionId, "message-hello-1",
                new JsonObject { ["bridgeVersion"] = supportedVersion, ["clientIdentityKind"] = "unpaired" },
                ClientId: ClientIdentity.Current.ToString()).Encode(),
            new Envelope("capabilities", "message-cap-1", sessionId, null, new CapabilitiesPayload([]).Encode()).Encode(),
            new Envelope("subscription_ack", "message-sub-ack-1", sessionId, null,
                new JsonObject { ["acceptedStateAreas"] = new JsonArray("character"), ["rejectedStateAreas"] = new JsonArray() })
                .Encode(),
            new Envelope("error", "message-error-1", sessionId, null,
                new JsonObject { ["code"] = "rate_limited", ["message"] = "Too many requests", ["retryable"] = true })
                .Encode(),
        ];
        var socket = new FakeWebSocket(responses);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(1, exitCode);
        Assert.True(socket.CloseCalled);
        Assert.Contains("instead of state_snapshot", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, socket.SentMessages.Count);
    }

    /// <summary>Verifies that ValidationRun completes the full exchange when the Bridge reports a
    /// supported version.</summary>
    [Fact]
    public async Task ExecuteAsyncCompletesTheExchangeWhenTheBridgeVersionIsSupported()
    {
        const string sessionId = "session-1";
        string supportedVersion = BridgeVersionCompatibility.MinimumSupportedVersion.ToString();
        string[] responses =
        [
            new Envelope("hello_ack", "message-ack-1", sessionId, "message-hello-1",
                new JsonObject { ["bridgeVersion"] = supportedVersion, ["clientIdentityKind"] = "unpaired" },
                ClientId: ClientIdentity.Current.ToString()).Encode(),
            new Envelope("capabilities", "message-cap-1", sessionId, null, new CapabilitiesPayload([]).Encode()).Encode(),
            new Envelope("subscription_ack", "message-sub-ack-1", sessionId, null,
                new JsonObject { ["acceptedStateAreas"] = new JsonArray("character"), ["rejectedStateAreas"] = new JsonArray() })
                .Encode(),
            new Envelope("state_snapshot", "message-snap-1", sessionId, null, new JsonObject { ["revision"] = 1 }).Encode(),
        ];
        var socket = new FakeWebSocket(responses);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));
        var output = new StringWriter();

        int exitCode = await ValidationRun.ExecuteAsync(connection, ValidHexToken, output);

        Assert.Equal(0, exitCode);
        Assert.Contains("state_snapshot", output.ToString());
    }
}
