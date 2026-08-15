using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises shared scenario setup failure paths.</summary>
public class BridgeScenarioTests
{
    /// <summary>A correctly formatted token that the harness does not accept.</summary>
    private const string WrongHexToken = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

    /// <summary>Verifies that failed authentication releases both the connection and harness process.</summary>
    [Fact]
    public async Task FailedHandshakeSetupReleasesTheHarnessPort()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BridgeScenario.ConnectAndAuthenticateAsync(WrongHexToken));

        using var replacement = new HarnessProcess(BridgeScenario.ValidHexToken);
        await replacement.WaitForReadyAsync();
        await replacement.WriteLineAsync("quit");
        Assert.True(await replacement.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that capabilities validation failure disposes the established connection and harness.</summary>
    [Fact]
    public async Task InvalidCapabilitiesSetupDisposesTheConnectionAndReleasesTheHarnessPort()
    {
        string sessionId = "session-test-1";
        string[] responses =
        [
            new Envelope("hello_ack", "message-ack-1", sessionId, "message-hello-1", new JsonObject()).Encode(),
            new Envelope("pong", "message-pong-1", sessionId, null, new JsonObject()).Encode(),
        ];
        var socket = new FakeWebSocket(responses);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BridgeScenario.ConnectAndAuthenticateAsync(
                BridgeScenario.ValidHexToken,
                () => Task.FromResult(connection)));

        Assert.True(socket.DisposeCalled);
        using var replacement = new HarnessProcess(BridgeScenario.ValidHexToken);
        await replacement.WaitForReadyAsync();
        await replacement.WriteLineAsync("quit");
        Assert.True(await replacement.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }
}
