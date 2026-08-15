using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises initial protocol negotiation and session establishment.</summary>
public class NegotiationScenarioTests
{
    /// <summary>A valid one-time token for the negotiation harness.</summary>
    private const string ValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

    /// <summary>The loopback endpoint exposed by the negotiation harness.</summary>
    private static readonly Uri BridgeUri = new("ws://127.0.0.1:58231/");

    /// <summary>Verifies that hello receives an acknowledged session and version.</summary>
    [Fact]
    public async Task HelloReceivesHelloAckWithSessionIdAndSelectedVersion()
    {
        using var harness = new HarnessProcess(ValidHexToken);
        await harness.WaitForReadyAsync();

        Envelope helloAck;
        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri))
        {
            var helloPayload = new JsonObject
            {
                ["endpoint"] = "client",
                ["supportedProtocolVersions"] = new JsonArray(1),
                ["auth"] = new JsonObject
                {
                    ["method"] = "one_time_local_token",
                    ["token"] = ValidHexToken,
                },
            };
            await connection.SendAsync(new Envelope(0, "hello", "message-hello-1", null, null, helloPayload));

            helloAck = await connection.ReceiveAsync();
            // Closed here, before "quit": Coordinator::Shutdown() waits for
            // any still-connected session's thread to exit, which otherwise
            // only happens after the 60s idle timeout (bridge/README.md).
        }

        Assert.Equal("hello_ack", helloAck.MessageType);
        Assert.False(string.IsNullOrEmpty(helloAck.SessionId));
        Assert.Equal("message-hello-1", helloAck.CorrelationId);
        Assert.Equal(1, helloAck.Payload["selectedProtocolVersion"]!.GetValue<int>());

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, harness.ExitCode);
    }

    /// <summary>Verifies that a v1-only client's negotiation carries no v2 identity fields.</summary>
    [Fact]
    public async Task V1OnlyClientNegotiationCarriesNoV2IdentityFields()
    {
        using var harness = new HarnessProcess(ValidHexToken);
        await harness.WaitForReadyAsync();

        Envelope helloAck;
        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri))
        {
            await connection.SendAsync(BridgeScenario.HelloEnvelope(ValidHexToken, supportedProtocolVersions: [1]));
            helloAck = await connection.ReceiveAsync();
        }

        Assert.Equal("hello_ack", helloAck.MessageType);
        Assert.Equal(0, helloAck.ProtocolVersion);
        Assert.Equal(1, helloAck.Payload["selectedProtocolVersion"]!.GetValue<int>());
        Assert.Null(helloAck.Payload["clientIdentityKind"]);
        Assert.Null(helloAck.BridgeInstanceId);
        Assert.Null(helloAck.PlayContextId);
        Assert.Null(helloAck.ClientId);

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that a v2-capable client negotiates version 2 and receives bridge identity.</summary>
    [Fact]
    public async Task V2CapableClientNegotiatesVersion2AndReceivesBridgeIdentity()
    {
        using var harness = new HarnessProcess(ValidHexToken);
        string bridgeInstanceId = await harness.WaitForReadyAsync();
        string clientId = ClientIdentity.Current.ToString();

        Envelope helloAck;
        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri))
        {
            await connection.SendAsync(BridgeScenario.HelloEnvelope(ValidHexToken, supportedProtocolVersions: [1, 2], clientId: clientId));
            helloAck = await connection.ReceiveAsync();
        }

        Assert.Equal("hello_ack", helloAck.MessageType);
        Assert.Equal(2, helloAck.ProtocolVersion);
        Assert.Equal(2, helloAck.Payload["selectedProtocolVersion"]!.GetValue<int>());
        Assert.Equal("unpaired", helloAck.Payload["clientIdentityKind"]!.GetValue<string>());
        Assert.Equal(bridgeInstanceId, helloAck.BridgeInstanceId);
        Assert.Equal(clientId, helloAck.ClientId);

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that offering only version 2 still negotiates version 2, not a fallback.</summary>
    [Fact]
    public async Task ClientOfferingOnlyVersion2NegotiatesVersion2()
    {
        using var harness = new HarnessProcess(ValidHexToken);
        await harness.WaitForReadyAsync();
        string clientId = ClientIdentity.Current.ToString();

        Envelope helloAck;
        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri))
        {
            await connection.SendAsync(BridgeScenario.HelloEnvelope(ValidHexToken, supportedProtocolVersions: [2], clientId: clientId));
            helloAck = await connection.ReceiveAsync();
        }

        Assert.Equal("hello_ack", helloAck.MessageType);
        Assert.Equal(2, helloAck.Payload["selectedProtocolVersion"]!.GetValue<int>());

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }
}
