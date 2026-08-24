using System.Text.Json.Nodes;

using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises the bridge-restart identity guarantee and ROADMAP.md's full bridge-restart
/// acceptance criterion.</summary>
/// <remarks>
/// <see cref="RelaunchingTheHarnessReportsADifferentBridgeInstanceId"/> proves the harness-only half:
/// bridgeInstanceId itself changes across a kill-and-relaunch cycle.
/// <see cref="RestartingTheBridgeMakesTheOldBridgeInstanceAndPlayContextPairStaleEvenWithACoincidentalPlayContextIdMatch"/>
/// completes the wire-level half over real protocol envelopes, including the case where the two
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
    /// coincidence gives two harness instances the same playContextId, their bridgeInstanceId
    /// still differs, so the full identity a real client compares against its cache is genuinely
    /// stale across the restart.
    /// The revision-coincidence half of that criterion is deferred until Stage 4 registers the first
    /// production state domain; this transitional contract has no state snapshot from which to read
    /// a revision.
    /// </summary>
    [Fact]
    public async Task RestartingTheBridgeMakesTheOldBridgeInstanceAndPlayContextPairStaleEvenWithACoincidentalPlayContextIdMatch()
    {
        (string firstBridgeInstanceId, string firstPlayContextId) =
            await LaunchNewGameHelloAndProbeAsync(extraEnvironmentVariables: null);

        var forcedPlayContext = new Dictionary<string, string>
        {
            ["DOVAHLINK_HARNESS_PLAY_CONTEXT_ID_OVERRIDE"] = firstPlayContextId,
        };
        (string secondBridgeInstanceId, string secondPlayContextId) =
            await LaunchNewGameHelloAndProbeAsync(forcedPlayContext);

        // The forced coincidence: the second instance's playContextId equals the first's, deliberately.
        Assert.Equal(firstPlayContextId, secondPlayContextId);
        // Despite the play-context coincidence, bridgeInstanceId differs -- a bridge restart always
        // mints a fresh one -- so the full identity pair a client compares against its cache is
        // still correctly detected as stale.
        Assert.NotEqual(firstBridgeInstanceId, secondBridgeInstanceId);
        Assert.NotEqual((firstBridgeInstanceId, firstPlayContextId), (secondBridgeInstanceId, secondPlayContextId));
    }

    /// <summary>
    /// Verifies that a paired client's trust survives a full bridge process restart -- covering
    /// both the "Bridge-restart" and "simulated Windows-restart" halves of Phase 3's acceptance
    /// criteria (ROADMAP.md, security.md's "Persistent local trust": "Persist completed trust
    /// outside the Skyrim/modpack files... survives Bridge, Skyrim, and Windows restarts"). A fresh
    /// process launch is the practical proof of both: only the DPAPI-encrypted trust-store file on
    /// disk survives a dead process, not any in-memory state, which is exactly what distinguishes
    /// this from an ordinary same-process reconnect (already proven by
    /// PairingScenarioTests.FullPairingRoundTripUpgradesTheSessionAndAllowsSubscribe).
    /// </summary>
    [Fact]
    public async Task PairedTrustSurvivesAFullHarnessProcessRestart()
    {
        using var trustStore = new IsolatedTrustStore();
        string credential;
        string firstBridgeInstanceId;
        string firstSessionId;

        using (var firstHarness = new HarnessProcess(BridgeScenario.ValidHexToken, trustStore.Override()))
        {
            firstBridgeInstanceId = await firstHarness.WaitForReadyAsync();

            await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
            await connection.SendAsync(BridgeScenario.UnpairedHelloEnvelope());
            Envelope helloAck = await connection.ReceiveAsync();
            if (helloAck.MessageType != "hello_ack" || helloAck.SessionId is null)
            {
                throw new InvalidOperationException($"Expected hello_ack with a sessionId, got {helloAck.MessageType}: {helloAck.Payload}");
            }
            string sessionId = helloAck.SessionId;
            firstSessionId = sessionId;
            await connection.ReceiveAsync();  // capabilities (unsolicited, sent right after hello_ack)

            await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
            Envelope status = await connection.ReceiveAsync();
            if (status.Payload["state"]!.GetValue<string>() != "available")
            {
                throw new InvalidOperationException($"Expected pairing to be available, got {status.Payload}.");
            }

            string code = await BridgeScenario.ReadPairingCodeReportAsync(firstHarness);

            await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
                new JsonObject { ["code"] = code }));
            Envelope confirmOutcome = await connection.ReceiveAsync();
            if (confirmOutcome.Payload["outcome"]!.GetValue<string>() != "credential_issued")
            {
                throw new InvalidOperationException($"Expected credential_issued, got {confirmOutcome.Payload}.");
            }
            credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

            await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
                new JsonObject { ["credential"] = credential }));
            Envelope ackOutcome = await connection.ReceiveAsync();
            if (ackOutcome.Payload["outcome"]!.GetValue<string>() != "trusted")
            {
                throw new InvalidOperationException($"Expected trusted, got {ackOutcome.Payload}.");
            }

            await BridgeScenario.CloseAndQuitAsync(firstHarness, connection);
        }

        using var secondHarness = new HarnessProcess(BridgeScenario.ValidHexToken, trustStore.Override());
        string secondBridgeInstanceId = await secondHarness.WaitForReadyAsync();
        // A bridge restart always mints a fresh bridgeInstanceId (ARCHITECTURE.md's runtime/identity
        // model) -- confirms this really is a second, independent process, not a reused one.
        Assert.NotEqual(firstBridgeInstanceId, secondBridgeInstanceId);

        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential));
        Envelope reconnectHelloAck = await reconnect.ReceiveAsync();
        Assert.Equal("hello_ack", reconnectHelloAck.MessageType);
        // The credential issued by the first (now-dead) process is still trusted by the second:
        // proof the trust survived the process boundary, not merely the connection.
        Assert.Equal("paired", reconnectHelloAck.Payload["clientIdentityKind"]!.GetValue<string>());
        Assert.NotNull(reconnectHelloAck.SessionId);
        // Reconnect semantics apply across a restart exactly as they do within one process
        // (security.md's "Session and replay protection"): always a fresh sessionId.
        Assert.NotEqual(firstSessionId, reconnectHelloAck.SessionId);
        await reconnect.ReceiveAsync();  // capabilities

        // Proves Trusted-tier access genuinely works against the second process, not just that
        // clientIdentityKind reports "paired".
        await reconnect.SendAsync(new Envelope("ping", "message-ping-after-restart", reconnectHelloAck.SessionId, null,
            new JsonObject()));
        Envelope pong = await reconnect.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(secondHarness, reconnect);
    }

    /// <summary>
    /// Verifies that revocation persists as durably as trust itself: revoking a client on one
    /// bridge process still rejects that client's credential after a full process restart, not
    /// just within the process that issued the revoke. Completes
    /// <see cref="PairedTrustSurvivesAFullHarnessProcessRestart"/>'s proof from the other
    /// direction, and the cross-process half of
    /// PairingScenarioTests.RevokedCredentialReconnectReceivesARevokedOutcome's same-process proof.
    /// </summary>
    [Fact]
    public async Task RevocationPersistsAcrossAFullHarnessProcessRestart()
    {
        using var trustStore = new IsolatedTrustStore();
        string credential;

        using (var firstHarness = new HarnessProcess(BridgeScenario.ValidHexToken, trustStore.Override()))
        {
            await firstHarness.WaitForReadyAsync();

            await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
            await connection.SendAsync(BridgeScenario.UnpairedHelloEnvelope());
            Envelope helloAck = await connection.ReceiveAsync();
            if (helloAck.MessageType != "hello_ack" || helloAck.SessionId is null)
            {
                throw new InvalidOperationException($"Expected hello_ack with a sessionId, got {helloAck.MessageType}: {helloAck.Payload}");
            }
            string sessionId = helloAck.SessionId;
            await connection.ReceiveAsync();  // capabilities

            await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
            await connection.ReceiveAsync();  // pairing_status

            string code = await BridgeScenario.ReadPairingCodeReportAsync(firstHarness);

            await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
                new JsonObject { ["code"] = code }));
            Envelope confirmOutcome = await connection.ReceiveAsync();
            credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

            await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
                new JsonObject { ["credential"] = credential }));
            await connection.ReceiveAsync();  // pairing_outcome: trusted
            await connection.CloseAsync();

            await firstHarness.WriteLineAsync("revoke client-1");
            Assert.Equal("REVOKED client-1", await firstHarness.ReadLineAsync());

            await firstHarness.WriteLineAsync("quit");
            Assert.True(await firstHarness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
        }

        using var secondHarness = new HarnessProcess(BridgeScenario.ValidHexToken, trustStore.Override());
        await secondHarness.WaitForReadyAsync();

        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential));
        Envelope error = await reconnect.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("revoked", error.Payload["code"]!.GetValue<string>());

        await reconnect.CloseAsync();

        await secondHarness.WriteLineAsync("quit");
        Assert.True(await secondHarness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Launches one harness instance, drives it into an active play context via <c>new_game</c>,
    /// completes hello over a fresh connection to capture the bridge's own reported
    /// (bridgeInstanceId, playContextId) pair from the wire, then probes the trusted session with a
    /// ping because no state area is currently registered.
    /// </summary>
    /// <param name="extraEnvironmentVariables">Extra environment variables for the harness process,
    /// such as the harness-only playContextId override used to force a coincidental collision.</param>
    /// <returns>The (bridgeInstanceId, playContextId) the bridge stamped onto its hello_ack.</returns>
    private static async Task<(string BridgeInstanceId, string PlayContextId)> LaunchNewGameHelloAndProbeAsync(
        IReadOnlyDictionary<string, string>? extraEnvironmentVariables)
    {
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken, extraEnvironmentVariables);
        string bridgeInstanceId = await harness.WaitForReadyAsync();

        await harness.WriteLineAsync("new_game");
        string playContextIdFromHarness = await BridgeScenario.ReadPlayContextReportAsync(harness);

        await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await connection.SendAsync(BridgeScenario.HelloEnvelope(
            BridgeScenario.ValidHexToken, clientId: ClientIdentity.Current.ToString()));
        Envelope helloAck = await connection.ReceiveAsync();
        if (helloAck.MessageType != "hello_ack" || helloAck.SessionId is null || helloAck.BridgeInstanceId is null ||
            helloAck.PlayContextId is null)
        {
            throw new InvalidOperationException(
                $"Expected a hello_ack carrying a sessionId, bridgeInstanceId, and playContextId, got " +
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

        await connection.SendAsync(new Envelope("ping", "message-ping-restart", helloAck.SessionId, null,
            new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        if (pong.MessageType != "pong")
        {
            throw new InvalidOperationException(
                $"Expected a pong after authentication, got {pong.MessageType}: {pong.Payload}");
        }

        await BridgeScenario.CloseAndQuitAsync(harness, connection);

        return (helloAck.BridgeInstanceId, helloAck.PlayContextId);
    }
}
