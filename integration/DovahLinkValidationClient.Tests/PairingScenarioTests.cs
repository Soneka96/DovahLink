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
        string trustStorePath = BridgeScenario.CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope helloAck, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(BridgeScenario.TrustStoreOverride(trustStorePath));
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
        string trustStorePath = BridgeScenario.CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(BridgeScenario.TrustStoreOverride(trustStorePath));
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

    /// <summary>
    /// Verifies that a revoked client's next reconnect attempt with its old credential receives
    /// the distinct "revoked" wire outcome (protocol/schema/README.md), not the generic
    /// "unauthenticated" a never-paired or wrong-credential client gets, and that the connection
    /// closes immediately like every other non-retryable handshake rejection.
    /// </summary>
    [Fact]
    public async Task RevokedCredentialReconnectReceivesARevokedOutcome()
    {
        string trustStorePath = BridgeScenario.CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(BridgeScenario.TrustStoreOverride(trustStorePath));
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new JsonObject { ["code"] = code }));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());
        string credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new JsonObject { ["credential"] = credential }));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", ackOutcome.Payload["outcome"]!.GetValue<string>());
        await connection.CloseAsync();

        // Revokes the just-paired clientId through the harness's test-only revoke command
        // (bridge/harness/dovahlink_bridge_harness.cpp) rather than the real TrustAdminService
        // console-admin surface, which this headless harness has no Papyrus layer to drive.
        await harness.WriteLineAsync("revoke client-1");
        Assert.Equal("REVOKED client-1", await harness.ReadLineAsync());

        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential));
        Envelope error = await reconnect.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("revoked", error.Payload["code"]!.GetValue<string>());
        Assert.False(error.Payload["retryable"]!.GetValue<bool>());

        // A revoked outcome closes the connection immediately, matching every other non-retryable
        // handshake rejection (AuthScenarioTests.cs's invalid-token case).
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconnect.ReceiveAsync());
        await reconnect.CloseAsync();

        // The revoked rejection must not wedge the connection slot: a fresh, unrelated connection
        // still succeeds afterward, mirroring AuthScenarioTests.cs's real-token-still-works proof
        // (InvalidTokenIsRejectedAndTheRealTokenStaysAvailable).
        await using BridgeConnection healthyConnection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await healthyConnection.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-2"));
        Envelope healthyHelloAck = await healthyConnection.ReceiveAsync();
        Assert.Equal("hello_ack", healthyHelloAck.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, healthyConnection);
    }

    /// <summary>
    /// Verifies that revoking a client's trust force-closes its already-open, already-authenticated
    /// session immediately -- not just the next reconnect attempt (security.md's "Persistent local
    /// trust": "invalidates its current authenticated session, closes that connection"). This is
    /// the stage J gap security.md's "Trust administration surface" named explicitly: revoking only
    /// removed persisted trust and left an already-connected session on the old credential running.
    /// </summary>
    [Fact]
    public async Task RevokeWhileConnectedClosesTheLiveSessionImmediately()
    {
        string trustStorePath = BridgeScenario.CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(BridgeScenario.TrustStoreOverride(trustStorePath));
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new JsonObject { ["code"] = code }));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());
        string credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new JsonObject { ["credential"] = credential }));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", ackOutcome.Payload["outcome"]!.GetValue<string>());

        // Unlike RevokedCredentialReconnectReceivesARevokedOutcome, this connection stays open
        // through the revoke below -- proving the live session itself is force-closed, not merely
        // that a later reconnect attempt would be rejected.
        await harness.WriteLineAsync("revoke client-1");
        Assert.Equal("REVOKED client-1", await harness.ReadLineAsync());

        // The still-open connection closes on its own, without the client ever sending anything new.
        // Asserts the specific message BridgeConnection.ReceiveAsync's WebSocketException branch
        // produces, distinct from its graceful-close-frame branch's wording -- confirming this was
        // genuinely the silent, raw TCP-level cancel-and-close DisconnectIfClientActive performs
        // (no application-level explanation message; deliberately out of this stage's scope), not an
        // accidental graceful close.
        InvalidOperationException closeException =
            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        Assert.Contains("ended before a complete protocol message was received", closeException.Message);
        await connection.CloseAsync();

        // The connection slot isn't wedged: a fresh, unrelated connection still succeeds right
        // after, mirroring RevokedCredentialReconnectReceivesARevokedOutcome's same proof.
        await using BridgeConnection healthyConnection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await healthyConnection.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-2"));
        Envelope healthyHelloAck = await healthyConnection.ReceiveAsync();
        Assert.Equal("hello_ack", healthyHelloAck.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, healthyConnection);
    }

    /// <summary>
    /// Verifies the reconnect-semantics policy in security.md's "Connection liveness" (bounded
    /// short retry/backoff over same-client takeover) for a real crash, not just a graceful close:
    /// after pairing, an abrupt disconnect immediately followed by a rapid reconnect using the
    /// issued credential must eventually succeed once the crashed connection's teardown finishes
    /// releasing the connection slot, rather than being rejected outright or handed a stale/reused
    /// session.
    /// </summary>
    [Fact]
    public async Task AbruptDisconnectAfterPairingRecoversViaBoundedRetryWithAFreshSession()
    {
        string trustStorePath = BridgeScenario.CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(BridgeScenario.TrustStoreOverride(trustStorePath));
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new JsonObject { ["code"] = code }));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());
        string credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new JsonObject { ["credential"] = credential }));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", ackOutcome.Payload["outcome"]!.GetValue<string>());

        // A crash, not a graceful close: no close frame, just an immediate abrupt teardown --
        // the previous connection's slot release races the very next accept below.
        connection.Abort();

        // Rapid restart: reconnect immediately, relying on ConnectWithRetryAsync's bounded
        // short retry/backoff to ride out the window where the slot is still busy completing
        // the crashed connection's teardown, rather than the bridge ever taking over the slot
        // for a same-client reconnect.
        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential));
        Envelope reconnectHelloAck = await reconnect.ReceiveAsync();
        Assert.Equal("hello_ack", reconnectHelloAck.MessageType);
        Assert.Equal("paired", reconnectHelloAck.Payload["clientIdentityKind"]!.GetValue<string>());
        Assert.NotNull(reconnectHelloAck.SessionId);
        Assert.NotEqual(sessionId, reconnectHelloAck.SessionId);

        await BridgeScenario.CloseAndQuitAsync(harness, reconnect);
    }

    /// <summary>Verifies that an incorrect code is reported as invalid, not silently accepted.</summary>
    [Fact]
    public async Task WrongCodeYieldsAnInvalidOutcome()
    {
        string trustStorePath = BridgeScenario.CreateIsolatedTrustStorePath();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(BridgeScenario.TrustStoreOverride(trustStorePath));
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

    /// <summary>Builds a six-digit code guaranteed to differ from <paramref name="realCode"/>.</summary>
    /// <param name="realCode">The genuine pairing code to avoid.</param>
    private static string DifferentCode(string realCode)
    {
        char lastDigit = realCode[^1];
        char differentDigit = lastDigit == '0' ? '1' : '0';
        return realCode[..^1] + differentDigit;
    }
}
