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

    /// <summary>Verifies that a well-formed play-context report yields its ID.</summary>
    [Fact]
    public async Task ReadPlayContextReportAsyncReturnsTheReportedPlayContextId()
    {
        using var harness = new HarnessProcess(HarnessProcess.CreateEchoingStartInfo("PLAY_CONTEXT context-1"));

        string playContextId = await BridgeScenario.ReadPlayContextReportAsync(harness);

        Assert.Equal("context-1", playContextId);
    }

    /// <summary>Verifies that a report line missing its prefix fails clearly.</summary>
    [Fact]
    public async Task ReadPlayContextReportAsyncThrowsWhenTheLineIsMissingItsPrefix()
    {
        using var harness = new HarnessProcess(HarnessProcess.CreateEchoingStartInfo("NOT_THE_RIGHT_PREFIX"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BridgeScenario.ReadPlayContextReportAsync(harness));
        Assert.Contains("did not report a play context", exception.Message);
    }

    /// <summary>Verifies that a report line carrying only whitespace after its prefix fails clearly
    /// instead of yielding an empty play context ID.</summary>
    [Fact]
    public async Task ReadPlayContextReportAsyncThrowsWhenThePlayContextIdIsBlank()
    {
        using var harness = new HarnessProcess(HarnessProcess.CreateEchoingStartInfo("PLAY_CONTEXT    "));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BridgeScenario.ReadPlayContextReportAsync(harness));
        Assert.Contains("reported an empty play context ID", exception.Message);
    }

    /// <summary>Verifies that reaching end-of-output before any report line fails clearly.</summary>
    [Fact]
    public async Task ReadPlayContextReportAsyncThrowsWhenTheOutputStreamEndsFirst()
    {
        using var harness = new HarnessProcess(HarnessProcess.CreateEchoingStartInfo());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BridgeScenario.ReadPlayContextReportAsync(harness));
        Assert.Contains("did not report a play context", exception.Message);
    }
}
