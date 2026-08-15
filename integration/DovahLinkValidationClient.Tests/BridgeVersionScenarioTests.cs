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
}
