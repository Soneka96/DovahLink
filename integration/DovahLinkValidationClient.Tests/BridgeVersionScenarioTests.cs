using System.Net.WebSockets;
using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises the hello/hello_ack bootstrap and its Bridge-version compatibility fields.</summary>
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
            var helloPayload = new JsonObject
            {
                ["endpoint"] = "client",
                ["clientId"] = "client-1",
                ["auth"] = new JsonObject
                {
                    ["method"] = "one_time_local_token",
                    ["token"] = ValidHexToken,
                },
            };
            await connection.SendAsync(new Envelope("hello", "message-hello-1", null, null, helloPayload));

            helloAck = await connection.ReceiveAsync();
            // Closed here, before "quit": Coordinator::Shutdown() waits for
            // any still-connected session's thread to exit, which otherwise
            // only happens after the 60s idle timeout (bridge/README.md).
        }

        Assert.Equal("hello_ack", helloAck.MessageType);
        Assert.False(string.IsNullOrEmpty(helloAck.SessionId));
        Assert.Equal("message-hello-1", helloAck.CorrelationId);
        Assert.False(string.IsNullOrEmpty(helloAck.Payload["bridgeVersion"]!.GetValue<string>()));

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
        Assert.Equal("unpaired", helloAck.Payload["clientIdentityKind"]!.GetValue<string>());
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
            new Envelope("capabilities", "message-cap-1", sessionId, null, new JsonObject()).Encode(),
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
