using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises the pairing handshake: unpaired bootstrap, code confirmation, and trust upgrade.</summary>
public class PairingScenarioTests
{
    /// <summary>Verifies that an unpaired session authenticates but cannot subscribe.</summary>
    [Fact]
    public async Task UnpairedHelloSucceedsAndSubscribeIsRejectedOnThatSession()
    {
        string trustStorePath = CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope helloAck, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(TrustStoreOverride(trustStorePath));
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        Assert.Equal("unpaired", helloAck.Payload["clientIdentityKind"]!.GetValue<string>());

        await connection.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies the full pairing round trip upgrades the session in place, with no reconnect.</summary>
    [Fact]
    public async Task FullPairingRoundTripUpgradesTheSessionAndAllowsSubscribe()
    {
        string trustStorePath = CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(TrustStoreOverride(trustStorePath));
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new JsonObject { ["code"] = code, ["displayName"] = "Integration Test" }));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());
        string credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new JsonObject { ["credential"] = credential }));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", ackOutcome.Payload["outcome"]!.GetValue<string>());

        // The upgrade lands in place on the same connection: subscribe succeeds on the very next
        // message, with no reconnect and no fresh hello_ack.
        await connection.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        Envelope subscriptionAck = await connection.ReceiveAsync();
        Assert.Equal("subscription_ack", subscriptionAck.MessageType);

        // The issued credential must actually work on its own, not just as an in-memory upgrade of
        // this connection's session: close this connection and reconnect with
        // trusted_device_credential, proving TrustStore::Persist's write survived.
        await connection.CloseAsync();
        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential));
        Envelope reconnectHelloAck = await reconnect.ReceiveAsync();
        Assert.Equal("hello_ack", reconnectHelloAck.MessageType);
        Assert.Equal("paired", reconnectHelloAck.Payload["clientIdentityKind"]!.GetValue<string>());
        // Reconnect semantics (security.md's "Session and replay protection"): a credentialed
        // reconnect always gets a fresh server-issued sessionId, never the pre-pairing one.
        Assert.NotNull(reconnectHelloAck.SessionId);
        Assert.NotEqual(sessionId, reconnectHelloAck.SessionId);

        await BridgeScenario.CloseAndQuitAsync(harness, reconnect);
    }

    /// <summary>Verifies that an incorrect code is reported as invalid, not silently accepted.</summary>
    [Fact]
    public async Task WrongCodeYieldsAnInvalidOutcome()
    {
        string trustStorePath = CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(TrustStoreOverride(trustStorePath));
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());

        string realCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        string wrongCode = DifferentCode(realCode);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new JsonObject { ["code"] = wrongCode }));
        Envelope outcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", outcome.MessageType);
        Assert.Equal("invalid", outcome.Payload["outcome"]!.GetValue<string>());

        // An invalid code must not upgrade the session -- still Restricted, symmetric with the
        // first scenario's rejection.
        await connection.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        Envelope subscribeError = await connection.ReceiveAsync();
        Assert.Equal("error", subscribeError.MessageType);
        Assert.Equal("malformed_message", subscribeError.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Builds a harness environment override isolating the trust store at <paramref name="path"/>.</summary>
    /// <param name="path">The isolated trust-store file path.</param>
    private static Dictionary<string, string> TrustStoreOverride(string path) =>
        new() { ["DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE"] = path };

    /// <summary>
    /// Creates a fresh, per-test trust-store file path under the OS temp directory so a pairing
    /// scenario never touches the developer's real trust store.
    /// </summary>
    private static string CreateIsolatedTrustStorePath() =>
        Path.Combine(Path.GetTempPath(), $"dovahlink-trust-{Guid.NewGuid():N}.json");

    /// <summary>Builds a six-digit code guaranteed to differ from <paramref name="realCode"/>.</summary>
    /// <param name="realCode">The genuine pairing code to avoid.</param>
    private static string DifferentCode(string realCode)
    {
        char lastDigit = realCode[^1];
        char differentDigit = lastDigit == '0' ? '1' : '0';
        return realCode[..^1] + differentDigit;
    }
}
