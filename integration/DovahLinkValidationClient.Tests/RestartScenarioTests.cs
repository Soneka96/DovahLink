using System.Text.Json.Nodes;

using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises the bridge-restart identity guarantee and ROADMAP.md's full bridge-restart
/// acceptance criterion.</summary>
/// <remarks>
/// <see cref="RelaunchingTheHarnessReportsADifferentBridgeInstanceId"/> proves the harness-only half:
/// bridgeInstanceId itself changes across a kill-and-relaunch cycle.
/// <see cref="RestartingTheBridgeMakesTheOldBridgeInstanceAndPlayContextPairStaleEvenWithACoincidentalPlayContextIdMatch"/>
/// completes the wire-level half over real protocol v2 envelopes, including the case where the two
/// instances' playContextId and first-pull revision both coincidentally match.
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

    /// <summary>
    /// Completes the bridge-restart acceptance criterion's wire-level half: even when a forced
    /// coincidence gives two harness instances the same playContextId AND the same first-pull
    /// revision, their bridgeInstanceId still differs, so the full identity a real client compares
    /// against its cache is genuinely stale across the restart. Proves bridgeInstanceId -- not
    /// playContextId or revision alone -- is load-bearing in that comparison, per ROADMAP.md's
    /// bridge-restart acceptance criterion ("...including when its play context and revision match
    /// the new bridge's values").
    /// </summary>
    [Fact]
    public async Task RestartingTheBridgeMakesTheOldBridgeInstanceAndPlayContextPairStaleEvenWithACoincidentalPlayContextIdMatch()
    {
        (string firstBridgeInstanceId, string firstPlayContextId, int firstRevision) =
            await LaunchNewGameHelloAndSubscribeAsync(extraEnvironmentVariables: null);

        var forcedPlayContext = new Dictionary<string, string>
        {
            ["DOVAHLINK_HARNESS_PLAY_CONTEXT_ID_OVERRIDE"] = firstPlayContextId,
        };
        (string secondBridgeInstanceId, string secondPlayContextId, int secondRevision) =
            await LaunchNewGameHelloAndSubscribeAsync(forcedPlayContext);

        // The forced coincidence: the second instance's playContextId equals the first's, deliberately.
        Assert.Equal(firstPlayContextId, secondPlayContextId);
        // No forcing needed here: every fresh play context's first pull naturally lands on revision 1,
        // so this coincidence holds for free -- exactly the "revision matches too" case
        // ROADMAP.md's acceptance wording calls out.
        Assert.Equal(firstRevision, secondRevision);
        // Despite both coincidences, bridgeInstanceId differs -- a bridge restart always mints a fresh
        // one -- so the full identity pair a client compares against its cache is still correctly
        // detected as stale.
        Assert.NotEqual(firstBridgeInstanceId, secondBridgeInstanceId);
        Assert.NotEqual((firstBridgeInstanceId, firstPlayContextId), (secondBridgeInstanceId, secondPlayContextId));
    }

    /// <summary>
    /// Launches one harness instance, drives it into an active play context via <c>new_game</c>,
    /// negotiates protocol v2 over a fresh connection to capture the bridge's own reported
    /// (bridgeInstanceId, playContextId) pair from the wire, then subscribes to the character state
    /// area to capture its first-pull revision.
    /// </summary>
    /// <param name="extraEnvironmentVariables">Extra environment variables for the harness process,
    /// such as the harness-only playContextId override used to force a coincidental collision.</param>
    /// <returns>The (bridgeInstanceId, playContextId, revision) the bridge stamped onto its hello_ack
    /// and initial character-state snapshot.</returns>
    private static async Task<(string BridgeInstanceId, string PlayContextId, int Revision)> LaunchNewGameHelloAndSubscribeAsync(
        IReadOnlyDictionary<string, string>? extraEnvironmentVariables)
    {
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken, extraEnvironmentVariables);
        string bridgeInstanceId = await harness.WaitForReadyAsync();

        await harness.WriteLineAsync("new_game");
        const string playContextPrefix = "PLAY_CONTEXT ";
        string? playContextLine = await harness.ReadLineAsync();
        if (playContextLine is null || !playContextLine.StartsWith(playContextPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Harness did not report a play context for 'new_game': {playContextLine}. Stderr: {harness.StandardError}");
        }
        string playContextIdFromHarness = playContextLine[playContextPrefix.Length..];

        await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await connection.SendAsync(BridgeScenario.HelloEnvelope(
            BridgeScenario.ValidHexToken, supportedProtocolVersions: [1, 2], clientId: ClientIdentity.Current.ToString()));
        Envelope helloAck = await connection.ReceiveAsync();
        if (helloAck.MessageType != "hello_ack" || helloAck.SessionId is null || helloAck.BridgeInstanceId is null ||
            helloAck.PlayContextId is null)
        {
            throw new InvalidOperationException(
                $"Expected a v2 hello_ack carrying a sessionId, bridgeInstanceId, and playContextId, got " +
                $"{helloAck.MessageType}: {helloAck.Payload}");
        }
        // The bridge's own stdout report and its wire-level hello_ack must agree on the same
        // playContextId -- the wire is the value under test, the stdout line only locates it.
        if (helloAck.PlayContextId != playContextIdFromHarness)
        {
            throw new InvalidOperationException(
                $"hello_ack's playContextId ({helloAck.PlayContextId}) did not match the harness's own " +
                $"'new_game' report ({playContextIdFromHarness}).");
        }

        await connection.ReceiveAsync();  // capabilities (unsolicited, sent right after hello_ack)

        await connection.SendAsync(new Envelope(2, "subscribe", "message-sub-1", helloAck.SessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        await connection.ReceiveAsync();  // subscription_ack
        Envelope snapshot = await connection.ReceiveAsync();
        if (snapshot.MessageType != "state_snapshot")
        {
            throw new InvalidOperationException(
                $"Expected a state_snapshot after subscribing, got {snapshot.MessageType}: {snapshot.Payload}");
        }
        int revision = snapshot.Payload["revision"]!.GetValue<int>();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));

        return (helloAck.BridgeInstanceId, helloAck.PlayContextId, revision);
    }
}
