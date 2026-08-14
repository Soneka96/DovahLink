namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises the bridge-restart identity guarantee: a fresh bridgeInstanceId per harness launch.</summary>
/// <remarks>
/// This is the harness-only half of ROADMAP.md's bridge-restart acceptance criterion: it proves
/// bridgeInstanceId itself changes across a kill-and-relaunch cycle. Proving that a stale
/// (bridgeInstanceId, playContextId) pair is rejected over the actual wire requires protocol v2's
/// envelope fields and dispatch rejection, added later in this phase.
/// </remarks>
public class RestartScenarioTests
{
    /// <summary>Verifies that relaunching the harness reports a different bridge instance ID.</summary>
    [Fact]
    public async Task RelaunchingTheHarnessReportsADifferentBridgeInstanceId()
    {
        string firstInstanceId;
        using (var harness = new HarnessProcess(BridgeScenario.ValidHexToken))
        {
            firstInstanceId = await harness.WaitForReadyAsync();
            await harness.WriteLineAsync("quit");
            Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
        }

        string secondInstanceId;
        using (var harness = new HarnessProcess(BridgeScenario.ValidHexToken))
        {
            secondInstanceId = await harness.WaitForReadyAsync();
            await harness.WriteLineAsync("quit");
            Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
        }

        Assert.False(string.IsNullOrEmpty(firstInstanceId));
        Assert.False(string.IsNullOrEmpty(secondInstanceId));
        Assert.NotEqual(firstInstanceId, secondInstanceId);
    }
}
