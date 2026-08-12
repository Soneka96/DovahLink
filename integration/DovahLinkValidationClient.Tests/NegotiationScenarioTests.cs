using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

public class NegotiationScenarioTests
{
    // Matches the token used by bridge/harness/dovahlink_bridge_harness_test.cpp
    // and the other C++ real-socket tests; any 64-hex-character value works,
    // since the harness accepts whatever it was launched with.
    private const string ValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";
    private static readonly Uri BridgeUri = new("ws://127.0.0.1:58231/");

    [Fact]
    public async Task HelloReceivesHelloAckWithSessionIdAndSelectedVersion()
    {
        using var harness = new HarnessProcess(ValidHexToken);
        Assert.Equal("READY", await harness.ReadLineAsync());

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
}
