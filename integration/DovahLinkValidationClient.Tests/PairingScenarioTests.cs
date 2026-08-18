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
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope helloAck, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
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
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
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
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
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
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
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
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
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
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
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

    /// <summary>
    /// Verifies the 10-second reconnect grace period: an owning client that disconnects mid-challenge
    /// and reconnects quickly gets "in_progress" with the existing code re-displayed, not "other_device_pairing",
    /// and can complete the pairing with that same code. Session upgrade lands on the reconnected socket.
    /// </summary>
    [Fact]
    public async Task ResumeAfterReconnectWithinGracePeriodReturnsInProgressWithExpirySeconds()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());
        int? expiresInSecondsFirst = status.Payload["expiresInSeconds"]?.GetValue<int>();
        Assert.NotNull(expiresInSecondsFirst);
        Assert.True(expiresInSecondsFirst > 0);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Disconnect mid-challenge without sending pairing_confirm, simulating a network drop.
        connection.Abort();

        // Rapid reconnect within the 10-second grace period.
        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-1"));
        Envelope reconnectHelloAck = await reconnect.ReceiveAsync();
        Assert.Equal("hello_ack", reconnectHelloAck.MessageType);
        string newSessionId = reconnectHelloAck.SessionId!;
        Assert.NotNull(newSessionId);

        // Query pairing status on the reconnected session: should resume the existing challenge, not start fresh.
        await reconnect.SendAsync(new Envelope("pairing_request", "message-request-2", newSessionId, null, new JsonObject()));
        Envelope resumeStatus = await reconnect.ReceiveAsync();
        Assert.Equal("pairing_status", resumeStatus.MessageType);
        // "in_progress" means same client already owns an active challenge; no new code, same code re-displayed.
        Assert.Equal("in_progress", resumeStatus.Payload["state"]!.GetValue<string>());
        int? expiresInSecondsResumed = resumeStatus.Payload["expiresInSeconds"]?.GetValue<int>();
        Assert.NotNull(expiresInSecondsResumed);
        Assert.True(expiresInSecondsResumed > 0);
        Assert.True(expiresInSecondsResumed <= 287, $"Resume expiry {expiresInSecondsResumed} exceeds 287s (5min initial minus reconnect latency)");

        // Confirm with the original code on the reconnected session: should succeed.
        await reconnect.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", newSessionId, null,
            new JsonObject { ["code"] = code, ["displayName"] = "Resume Test" }));
        Envelope confirmOutcome = await reconnect.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());
        string credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

        // Complete the pairing handshake on the reconnected socket.
        await reconnect.SendAsync(new Envelope("pairing_ack", "message-ack-1", newSessionId, null,
            new JsonObject { ["credential"] = credential }));
        Envelope ackOutcome = await reconnect.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", ackOutcome.Payload["outcome"]!.GetValue<string>());

        // Session upgrade lands on the reconnected socket; subscribe succeeds immediately without a fresh reconnect.
        await reconnect.SendAsync(new Envelope("subscribe", "message-sub-1", newSessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        Envelope subscriptionAck = await reconnect.ReceiveAsync();
        Assert.Equal("subscription_ack", subscriptionAck.MessageType);

        // Verify the credential issued during grace-period pairing is actually persisted and works on a new connection,
        // not just an in-memory upgrade of this connection's session (matching FullPairingRoundTripUpgradesTheSessionAndAllowsSubscribe).
        await reconnect.CloseAsync();
        await using BridgeConnection credentialVerify = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await credentialVerify.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential, clientId: "client-1"));
        Envelope verifyHelloAck = await credentialVerify.ReceiveAsync();
        Assert.Equal("hello_ack", verifyHelloAck.MessageType);
        Assert.Equal("paired", verifyHelloAck.Payload["clientIdentityKind"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, credentialVerify);
    }

    /// <summary>
    /// Verifies the single-connected-client model (Phase 9 deferred): when client-1 owns an active
    /// challenge, client-2's pairing_request returns "other_device_pairing" state, disclosing nothing
    /// about the owning device or code. Client-2 cannot proceed; client-1 completes pairing and upgrades.
    /// </summary>
    [Fact]
    public async Task OtherDeviceBusyReturnsOtherDevicePairingState()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection1, string sessionId1, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection1 = connection1;

        // Client-1 initiates pairing.
        await connection1.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId1, null, new JsonObject()));
        Envelope status1 = await connection1.ReceiveAsync();
        Assert.Equal("pairing_status", status1.MessageType);
        Assert.Equal("message-request-1", status1.CorrelationId);
        Assert.Equal("available", status1.Payload["state"]!.GetValue<string>());

        // Synchronization point: the harness has printed the code to stdout; challenge is now stable.
        // Client-2's request below will find the active challenge still owned by client-1.
        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Client-2 attempts pairing while client-1 owns the active challenge.
        await using BridgeConnection connection2 = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await connection2.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-2"));
        Envelope helloAck2 = await connection2.ReceiveAsync();
        Assert.Equal("hello_ack", helloAck2.MessageType);
        string sessionId2 = helloAck2.SessionId!;

        await connection2.ReceiveAsync(); // capabilities (ignored)

        await connection2.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId2, null, new JsonObject()));
        Envelope status2 = await connection2.ReceiveAsync();
        Assert.Equal("pairing_status", status2.MessageType);
        Assert.Equal("message-request-2", status2.CorrelationId);
        // "other_device_pairing" means a different clientId owns the active challenge; expiresInSeconds is never present.
        Assert.Equal("other_device_pairing", status2.Payload["state"]!.GetValue<string>());
        Assert.False(status2.Payload.ContainsKey("expiresInSeconds"), "expiresInSeconds must not be present for other_device_pairing state");

        // Client-2 cannot proceed without a code; any confirm attempt should fail (no code to send).
        // Rather than speculate on the bridge's behavior (confirm with empty/fake code), we simply verify
        // client-1 can complete pairing unimpeded while client-2 observes the blocked state.

        // Client-1 confirms the original code and completes pairing.
        await connection1.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId1, null,
            new JsonObject { ["code"] = code, ["displayName"] = "Device 1" }));
        Envelope confirmOutcome = await connection1.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());
        string credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

        await connection1.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId1, null,
            new JsonObject { ["credential"] = credential }));
        Envelope ackOutcome = await connection1.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", ackOutcome.Payload["outcome"]!.GetValue<string>());

        // Client-1's session is now Full (paired); subscription succeeds.
        await connection1.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId1, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        Envelope subscriptionAck = await connection1.ReceiveAsync();
        Assert.Equal("subscription_ack", subscriptionAck.MessageType);

        // Client-2 remains Restricted (unpaired): subscription is rejected.
        await connection2.SendAsync(new Envelope("subscribe", "message-sub-2", sessionId2, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        Envelope error2 = await connection2.ReceiveAsync();
        Assert.Equal("error", error2.MessageType);
        Assert.Equal("malformed_message", error2.Payload["code"]!.GetValue<string>());

        await connection1.CloseAsync();
        await connection2.CloseAsync();
        await harness.WriteLineAsync("quit");
        await harness.WaitForExitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies the 10-second grace-period expiry: after a client disconnects mid-challenge
    /// and the grace period elapses without reconnect, the challenge slot is freed. A new client
    /// can then start a fresh pairing with a newly generated code, not "other_device_pairing".
    /// </summary>
    [Fact]
    public async Task GracePeriodExpiryFreesSlotForNewPairing()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection1, string sessionId1, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection1 = connection1;

        // Client-1 initiates pairing.
        await connection1.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId1, null, new JsonObject()));
        Envelope status1 = await connection1.ReceiveAsync();
        Assert.Equal("pairing_status", status1.MessageType);
        Assert.Equal("message-request-1", status1.CorrelationId);
        Assert.Equal("available", status1.Payload["state"]!.GetValue<string>());
        Assert.True(status1.Payload["expiresInSeconds"]!.GetValue<int>() > 0);

        string code1 = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Client-1 disconnects mid-challenge (no confirm).
        connection1.Abort();

        // Wait for the grace period to elapse (kPairingReconnectGracePeriod = 10 seconds).
        // This is a genuine, full-duration timeout proof (per ai/context/integration/testing.md
        // and integration/README.md, we only use these for periods that aren't proven elsewhere
        // at the C++ unit-test level). The hard-limit and other decision points are already
        // covered by pairing_session_test.cpp; this integration layer proves the full
        // end-to-end wire behavior, including the real socket delays.
        await Task.Delay(TimeSpan.FromSeconds(11));

        // Client-2 connects and requests pairing: should get "available" with a fresh code
        // (slot was freed by grace-period expiry), not "other_device_pairing".
        await using BridgeConnection connection2 = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await connection2.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-2"));
        Envelope helloAck2 = await connection2.ReceiveAsync();
        Assert.Equal("hello_ack", helloAck2.MessageType);
        string sessionId2 = helloAck2.SessionId!;

        await connection2.ReceiveAsync(); // capabilities (ignored)

        await connection2.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId2, null, new JsonObject()));
        Envelope status2 = await connection2.ReceiveAsync();
        Assert.Equal("pairing_status", status2.MessageType);
        Assert.Equal("message-request-2", status2.CorrelationId);
        // Slot is freed: client-2 gets "available" (not "other_device_pairing") with fresh expiry.
        Assert.Equal("available", status2.Payload["state"]!.GetValue<string>());
        Assert.True(status2.Payload["expiresInSeconds"]!.GetValue<int>() > 0);

        string code2 = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Codes must be different (fresh challenge generated for client-2, not client-1's expired code).
        Assert.NotEqual(code1, code2);

        // Client-2 can complete pairing with the fresh code.
        await connection2.SendAsync(new Envelope("pairing_confirm", "message-confirm-2", sessionId2, null,
            new JsonObject { ["code"] = code2, ["displayName"] = "Device 2" }));
        Envelope confirmOutcome = await connection2.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());
        string credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

        await connection2.SendAsync(new Envelope("pairing_ack", "message-ack-2", sessionId2, null,
            new JsonObject { ["credential"] = credential }));
        Envelope ackOutcome = await connection2.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", ackOutcome.Payload["outcome"]!.GetValue<string>());

        // Session upgrade lands on the same connection; subscription succeeds.
        await connection2.SendAsync(new Envelope("subscribe", "message-sub-2", sessionId2, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        Envelope subscriptionAck = await connection2.ReceiveAsync();
        Assert.Equal("subscription_ack", subscriptionAck.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection2);
    }

    /// <summary>
    /// Verifies the renotify mechanics (PLAN.md Stage D): the owning client can manually
    /// redisplay the active challenge's code via pairing_renotify, up to a per-renotify cooldown.
    /// Successful renotify returns updated expiresInSeconds. Immediate repeat hits cooldown
    /// with retryAfterSeconds; after cooldown elapses, renotify succeeds again.
    /// </summary>
    [Fact]
    public async Task RenotifyRedisplaysCodeAndEnforcesCooldown()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Start pairing.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());
        int initialExpiry = status.Payload["expiresInSeconds"]!.GetValue<int>();
        Assert.True(initialExpiry > 0);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // First renotify: should succeed. The code is redisplayed via in-game notification,
        // not sent over the wire; expiresInSeconds is not in renotified outcome (protocol/schema/README.md line 277).
        await connection.SendAsync(BridgeScenario.RenotifyEnvelope(sessionId, "message-renotify-1"));
        Envelope renotifyOutcome1 = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", renotifyOutcome1.MessageType);
        Assert.Equal("message-renotify-1", renotifyOutcome1.CorrelationId);
        Assert.Equal("renotified", renotifyOutcome1.Payload["outcome"]!.GetValue<string>());
        // renotified outcome carries no expiry, code, or error info (code is redisplayed in-game, not over wire).
        Assert.Null(renotifyOutcome1.Payload["retryAfterSeconds"]?.GetValue<int?>());
        Assert.False(renotifyOutcome1.Payload.ContainsKey("expiresInSeconds"));

        // Immediate second renotify: hits the 5-second cooldown, returns remaining wait.
        // renotify_cooldown carries retryAfterSeconds but not expiresInSeconds (protocol/schema/README.md lines 277-285).
        await connection.SendAsync(BridgeScenario.RenotifyEnvelope(sessionId, "message-renotify-2"));
        Envelope renotifyCooldown = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", renotifyCooldown.MessageType);
        Assert.Equal("message-renotify-2", renotifyCooldown.CorrelationId);
        Assert.Equal("renotify_cooldown", renotifyCooldown.Payload["outcome"]!.GetValue<string>());
        int retryAfter = renotifyCooldown.Payload["retryAfterSeconds"]!.GetValue<int>();
        Assert.True(retryAfter > 0);
        Assert.True(retryAfter <= 5, $"Cooldown {retryAfter}s should not exceed kPairingRenotifyCooldown (5s)");
        Assert.False(renotifyCooldown.Payload.ContainsKey("expiresInSeconds"), "expiresInSeconds should not be in renotify_cooldown outcome");

        // Wait for the cooldown to elapse.
        await Task.Delay(TimeSpan.FromSeconds(retryAfter + 1));

        // Third renotify (after cooldown expires): should succeed again.
        await connection.SendAsync(BridgeScenario.RenotifyEnvelope(sessionId, "message-renotify-3"));
        Envelope renotifyOutcome3 = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", renotifyOutcome3.MessageType);
        Assert.Equal("message-renotify-3", renotifyOutcome3.CorrelationId);
        Assert.Equal("renotified", renotifyOutcome3.Payload["outcome"]!.GetValue<string>());
        Assert.Null(renotifyOutcome3.Payload["retryAfterSeconds"]?.GetValue<int?>());
        Assert.False(renotifyOutcome3.Payload.ContainsKey("expiresInSeconds"));

        // Confirm the original code: should succeed (code remained valid through renotifies).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new JsonObject { ["code"] = code }));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies the auto-renotify path (PLAN.md Stage D): when a client submits an incorrect code,
    /// the outcome is "invalid" (challenge persists), the bridge auto-redisplays the code via
    /// in-game notification (harness signals this with PAIRING_CODE_INCORRECT line), and the expiry
    /// is refreshed. The owning client may continue attempting with the original code.
    /// </summary>
    [Fact]
    public async Task WrongCodeTriggersAutoRenotifyWithoutConsumingAttempt()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Start pairing.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());
        int initialExpiry = status.Payload["expiresInSeconds"]!.GetValue<int>();

        string correctCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        string wrongCode = DifferentCode(correctCode);

        // Send wrong code: challenge persists (not hard-limit yet), returns "invalid" outcome.
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-wrong", sessionId, null,
            new JsonObject { ["code"] = wrongCode }));
        Envelope invalidOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", invalidOutcome.MessageType);
        Assert.Equal("message-confirm-wrong", invalidOutcome.CorrelationId);
        Assert.Equal("invalid", invalidOutcome.Payload["outcome"]!.GetValue<string>());
        // Invalid outcome carries no credential or error info (challenge is still active).
        Assert.Null(invalidOutcome.Payload["credential"]?.GetValue<string>());
        Assert.Null(invalidOutcome.Payload["retryAfterSeconds"]?.GetValue<int?>());

        // Bridge auto-renotifies the code via in-game notification; harness signals this.
        string autoRenotifySignal = await harness.ReadLineAsync();
        Assert.NotNull(autoRenotifySignal);
        Assert.StartsWith("PAIRING_CODE_INCORRECT ", autoRenotifySignal);
        Assert.Equal(correctCode, autoRenotifySignal.AsSpan()["PAIRING_CODE_INCORRECT ".Length..].ToString());

        // Query pairing status: should be "in_progress" (same client owns active challenge)
        // with refreshed expiry (auto-renotify extended the deadline).
        await connection.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId, null, new JsonObject()));
        Envelope statusAfterWrongCode = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", statusAfterWrongCode.MessageType);
        Assert.Equal("in_progress", statusAfterWrongCode.Payload["state"]!.GetValue<string>());
        int refreshedExpiry = statusAfterWrongCode.Payload["expiresInSeconds"]!.GetValue<int>();
        Assert.True(refreshedExpiry > 0);
        // Expiry should be refreshed by auto-renotify (roughly restored to full duration, minus latency).
        Assert.True(refreshedExpiry >= initialExpiry - 5,
            $"Refreshed expiry {refreshedExpiry}s should be close to initial {initialExpiry}s (within 5s latency)");

        // Confirm with the original correct code: should succeed (wrong attempt didn't consume it).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-correct", sessionId, null,
            new JsonObject { ["code"] = correctCode }));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies the hard limit on wrong pairing attempts (PLAN.md Stage B): after 5 incorrect codes,
    /// the challenge is cancelled with outcome "hard_limit_reached". The bridge signals challenge
    /// exhaustion via PAIRING_ATTEMPTS_EXHAUSTED to the notification sink (distinct from per-attempt
    /// PAIRING_CODE_INCORRECT). A fresh pairing_request generates a new challenge.
    /// </summary>
    [Fact]
    public async Task FiveWrongAttemptsReachesHardLimitAndCancelsChallenge()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Start pairing.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());

        string correctCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Submit 4 wrong codes: each should yield "invalid" and auto-renotify.
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            string wrongCode = DifferentCode(correctCode);
            await connection.SendAsync(new Envelope("pairing_confirm", $"message-confirm-wrong-{attempt}", sessionId, null,
                new JsonObject { ["code"] = wrongCode }));
            Envelope outcome = await connection.ReceiveAsync();
            Assert.Equal("pairing_outcome", outcome.MessageType);
            Assert.Equal($"message-confirm-wrong-{attempt}", outcome.CorrelationId);
            Assert.Equal("invalid", outcome.Payload["outcome"]!.GetValue<string>());

            // Each wrong attempt triggers auto-renotify: harness signals with PAIRING_CODE_INCORRECT.
            string signal = await harness.ReadLineAsync();
            Assert.StartsWith("PAIRING_CODE_INCORRECT ", signal);
        }

        // After 4 attempts, challenge still exists: query status to prove boundary.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-boundary", sessionId, null, new JsonObject()));
        Envelope boundaryStatus = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", boundaryStatus.MessageType);
        Assert.Equal("in_progress", boundaryStatus.Payload["state"]!.GetValue<string>());

        // 5th wrong attempt: hits hard limit, challenge is cancelled.
        string fifthWrongCode = DifferentCode(correctCode);
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-wrong-5", sessionId, null,
            new JsonObject { ["code"] = fifthWrongCode }));
        Envelope hardLimitOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", hardLimitOutcome.MessageType);
        Assert.Equal("message-confirm-wrong-5", hardLimitOutcome.CorrelationId);
        Assert.Equal("hard_limit_reached", hardLimitOutcome.Payload["outcome"]!.GetValue<string>());
        // hard_limit_reached carries no credential or retry info (challenge is immediately cancelled, not retryable).
        Assert.Null(hardLimitOutcome.Payload["credential"]?.GetValue<string>());
        Assert.Null(hardLimitOutcome.Payload["retryAfterSeconds"]?.GetValue<int?>());

        // Hard limit is signalled distinctly from per-attempt auto-renotify: harness prints PAIRING_ATTEMPTS_EXHAUSTED.
        string exhaustedSignal = await harness.ReadLineAsync();
        Assert.Equal("PAIRING_ATTEMPTS_EXHAUSTED", exhaustedSignal);

        // Challenge is cleared by the hard limit: fresh pairing_request starts a new one with a fresh code.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId, null, new JsonObject()));
        Envelope freshStatus = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", freshStatus.MessageType);
        Assert.Equal("available", freshStatus.Payload["state"]!.GetValue<string>());
        Assert.True(freshStatus.Payload["expiresInSeconds"]!.GetValue<int>() > 0, "Fresh challenge must carry expiresInSeconds");

        string freshCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        // The new challenge has a different code (fresh attempt, not retry of the exhausted one).
        Assert.NotEqual(correctCode, freshCode);

        // Fresh code works (confirms the challenge was genuinely cleared and restarted).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-fresh", sessionId, null,
            new JsonObject { ["code"] = freshCode }));
        Envelope freshConfirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", freshConfirmOutcome.MessageType);
        Assert.Equal("credential_issued", freshConfirmOutcome.Payload["outcome"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies pairing_cancel on an active challenge (PLAN.md Stage D): the owning client can
    /// cancel an active pairing challenge, clearing it immediately. A fresh pairing_request then
    /// starts a new challenge, proving the slot was freed and not merely marked invalid.
    /// </summary>
    [Fact]
    public async Task CancelActiveChallengeClears​And​AllowsFreshPairing()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Start pairing (active challenge).
        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());

        string code1 = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Cancel the active challenge.
        await connection.SendAsync(BridgeScenario.CancelEnvelope(sessionId, "message-cancel-1"));
        Envelope cancelOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", cancelOutcome.MessageType);
        Assert.Equal("message-cancel-1", cancelOutcome.CorrelationId);
        Assert.Equal("cancelled", cancelOutcome.Payload["outcome"]!.GetValue<string>());
        // cancelled outcome carries no credential or retry info (challenge is cleared, not retryable).
        Assert.Null(cancelOutcome.Payload["credential"]?.GetValue<string>());
        Assert.Null(cancelOutcome.Payload["retryAfterSeconds"]?.GetValue<int?>());

        // Fresh pairing_request: should start a new challenge with a fresh code.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId, null, new JsonObject()));
        Envelope freshStatus = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", freshStatus.MessageType);
        Assert.Equal("available", freshStatus.Payload["state"]!.GetValue<string>());
        Assert.True(freshStatus.Payload["expiresInSeconds"]!.GetValue<int>() > 0);

        string code2 = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        Assert.NotEqual(code1, code2, "Fresh challenge must have a new code, not the cancelled one");

        // Fresh code works (proves challenge was genuinely cleared).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-fresh", sessionId, null,
            new JsonObject { ["code"] = code2 }));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies pairing_cancel on a pending credential (PLAN.md Stage D): the owning client can
    /// cancel a credential awaiting final acknowledgment (pairing_ack), clearing it without committing
    /// it to persistent trust. A fresh pairing_request then starts a new challenge.
    /// </summary>
    [Fact]
    public async Task CancelPendingCredentialClears​And​AllowsFreshPairing()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Start pairing and confirm code (credential becomes pending, awaiting ack).
        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", status.Payload["state"]!.GetValue<string>());

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new JsonObject { ["code"] = code }));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", confirmOutcome.Payload["outcome"]!.GetValue<string>());
        string pendingCredential = confirmOutcome.Payload["credential"]!.GetValue<string>();

        // Before ack: cancel the pending credential.
        await connection.SendAsync(BridgeScenario.CancelEnvelope(sessionId, "message-cancel-pending"));
        Envelope cancelOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", cancelOutcome.MessageType);
        Assert.Equal("message-cancel-pending", cancelOutcome.CorrelationId);
        Assert.Equal("cancelled", cancelOutcome.Payload["outcome"]!.GetValue<string>());
        // cancelled outcome carries no credential or retry info.
        Assert.Null(cancelOutcome.Payload["credential"]?.GetValue<string>());
        Assert.Null(cancelOutcome.Payload["retryAfterSeconds"]?.GetValue<int?>());

        // The pending credential is not persisted: fresh pairing_request starts a new challenge
        // (not a resume of the cancelled pending state).
        await connection.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId, null, new JsonObject()));
        Envelope freshStatus = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", freshStatus.MessageType);
        Assert.Equal("available", freshStatus.Payload["state"]!.GetValue<string>());

        string freshCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        Assert.NotEqual(code, freshCode);

        // Fresh code works (proves the cancelled credential was not persisted and challenge was cleared).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-fresh", sessionId, null,
            new JsonObject { ["code"] = freshCode }));
        Envelope freshConfirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", freshConfirmOutcome.MessageType);
        Assert.Equal("credential_issued", freshConfirmOutcome.Payload["outcome"]!.GetValue<string>());
        string freshCredential = freshConfirmOutcome.Payload["credential"]!.GetValue<string>();
        Assert.NotEqual(pendingCredential, freshCredential, "Fresh pairing must issue a new credential, not re-use the cancelled pending one");

        // Complete fresh pairing to confirm it works.
        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-fresh", sessionId, null,
            new JsonObject { ["credential"] = freshCredential }));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", ackOutcome.Payload["outcome"]!.GetValue<string>());

        // Verify the cancelled credential was truly not persisted: attempting to reconnect with it fails.
        await connection.CloseAsync();
        await using BridgeConnection reconnectWithCancelled = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnectWithCancelled.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(pendingCredential));
        Envelope rejectionError = await reconnectWithCancelled.ReceiveAsync();
        Assert.Equal("error", rejectionError.MessageType);
        Assert.Equal("unauthenticated", rejectionError.Payload["code"]!.GetValue<string>(),
            "Cancelled pending credential must be rejected as unauthenticated, not persisted");

        // Fresh credential works on a new connection (contrasting proof).
        await reconnectWithCancelled.CloseAsync();
        await using BridgeConnection reconnectWithFresh = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnectWithFresh.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(freshCredential));
        Envelope freshHelloAck = await reconnectWithFresh.ReceiveAsync();
        Assert.Equal("hello_ack", freshHelloAck.MessageType);
        Assert.Equal("paired", freshHelloAck.Payload["clientIdentityKind"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, reconnectWithFresh);
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
