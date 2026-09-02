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

        Assert.Equal("unpaired", HelloAckPayload.Decode(helloAck.Payload).ClientIdentityKind);

        await connection.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId, null,
            new SubscribePayload(["character"]).Encode()));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("malformed_message", ErrorPayload.Decode(error.Payload).Code);

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
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code, "Integration Test").Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // The upgrade lands in place on the same connection: the subscription request is accepted
        // by the Full-session allowlist on the very next message, with no reconnect and no fresh
        // hello_ack; the currently unregistered state area is rejected in the acknowledgement.
        await connection.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId, null,
            new SubscribePayload(["character"]).Encode()));
        Envelope subscriptionAck = await connection.ReceiveAsync();
        Assert.Equal("subscription_ack", subscriptionAck.MessageType);
        AssertUnregisteredSubscriptionAcknowledged(subscriptionAck, "message-sub-1", "character");

        // The issued credential must actually work on its own, not just as an in-memory upgrade of
        // this connection's session: close this connection and reconnect with
        // trusted_device_credential, proving TrustStore::Persist's write survived.
        await connection.CloseAsync();
        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential));
        Envelope reconnectHelloAck = await reconnect.ReceiveAsync();
        Assert.Equal("hello_ack", reconnectHelloAck.MessageType);
        Assert.Equal("paired", HelloAckPayload.Decode(reconnectHelloAck.Payload).ClientIdentityKind);
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
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);
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
        ErrorPayload errorDecoded = ErrorPayload.Decode(error.Payload);
        Assert.Equal("revoked", errorDecoded.Code);
        Assert.False(errorDecoded.Retryable);

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
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // Unlike RevokedCredentialReconnectReceivesARevokedOutcome, this connection stays open
        // through the revoke below -- proving the live session itself is force-closed, not merely
        // that a later reconnect attempt would be rejected.
        await harness.WriteLineAsync("revoke client-1");
        Assert.Equal("REVOKED client-1", await harness.ReadLineAsync());

        // The Bridge sends session_invalidated(reason: "revoked") before force-closing (Stage 7,
        // ai/context/protocol/security.md's "Administrative session invalidation") -- the still-open
        // connection receives it without the client ever sending anything new.
        Envelope invalidated = await connection.ReceiveAsync();
        Assert.Equal("session_invalidated", invalidated.MessageType);
        Assert.Equal(sessionId, invalidated.SessionId);
        Assert.Null(invalidated.CorrelationId);
        Assert.Equal("revoked", SessionInvalidatedPayload.Decode(invalidated.Payload).Reason);

        // The connection then closes on its own. Asserts the specific message
        // BridgeConnection.ReceiveAsync's WebSocketException branch produces, distinct from its
        // graceful-close-frame branch's wording -- confirming this was the raw TCP-level
        // cancel-and-close DisconnectIfClientActive performs after the notification above, not an
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
    /// Verifies the Phase 3.2 Known Device block/unblock lifecycle end-to-end: blocking a
    /// live-connected client force-closes its session immediately (not just the next reconnect,
    /// mirroring <see cref="RevokeWhileConnectedClosesTheLiveSessionImmediately"/>'s proof for
    /// revoke), a reconnect attempt with the old credential receives the distinct "blocked" wire
    /// outcome rather than "revoked" (protocol/schema/README.md, ai/context/protocol/security.md's
    /// "Known device blocking"), and unblocking returns the device to unpaired, requiring and
    /// succeeding at a completely fresh pairing round trip under the same clientId.
    /// </summary>
    [Fact]
    public async Task BlockWhileConnectedClosesTheSessionAndAFreshPairingSucceedsAfterUnblock()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // Blocks the just-paired clientId through the harness's test-only block command
        // (bridge/harness/dovahlink_bridge_harness.cpp), mirroring RevokeWhileConnectedClosesTheLiveSessionImmediately.
        // The connection stays open through this call, proving the live session itself is
        // force-closed, not merely that a later reconnect attempt would be rejected.
        await harness.WriteLineAsync("block client-1");
        Assert.Equal("BLOCKED client-1", await harness.ReadLineAsync());

        // The Bridge sends session_invalidated(reason: "blocked") before force-closing (Stage 7,
        // ai/context/protocol/security.md's "Administrative session invalidation"), mirroring
        // RevokeWhileConnectedClosesTheLiveSessionImmediately's same proof for revoke.
        Envelope invalidated = await connection.ReceiveAsync();
        Assert.Equal("session_invalidated", invalidated.MessageType);
        Assert.Equal(sessionId, invalidated.SessionId);
        Assert.Null(invalidated.CorrelationId);
        Assert.Equal("blocked", SessionInvalidatedPayload.Decode(invalidated.Payload).Reason);

        InvalidOperationException closeException =
            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        Assert.Contains("ended before a complete protocol message was received", closeException.Message);
        await connection.CloseAsync();

        // A reconnect with the old (now-destroyed) credential gets "blocked", distinct from
        // "revoked" -- a revoked device may still re-pair, a blocked one may not until unblocked.
        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential));
        Envelope blockedError = await reconnect.ReceiveAsync();
        Assert.Equal("error", blockedError.MessageType);
        ErrorPayload blockedErrorDecoded = ErrorPayload.Decode(blockedError.Payload);
        Assert.Equal("blocked", blockedErrorDecoded.Code);
        Assert.False(blockedErrorDecoded.Retryable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconnect.ReceiveAsync());
        await reconnect.CloseAsync();

        // A blocked clientId is also rejected on the unpaired/bootstrap path, not just
        // trusted_device_credential -- it cannot even re-enter pairing while still blocked.
        await using BridgeConnection blockedPairingAttempt = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await blockedPairingAttempt.SendAsync(BridgeScenario.UnpairedHelloEnvelope());
        Envelope blockedUnpairedError = await blockedPairingAttempt.ReceiveAsync();
        Assert.Equal("error", blockedUnpairedError.MessageType);
        Assert.Equal("blocked", ErrorPayload.Decode(blockedUnpairedError.Payload).Code);
        await blockedPairingAttempt.CloseAsync();

        await harness.WriteLineAsync("unblock client-1");
        Assert.Equal("UNBLOCKED client-1", await harness.ReadLineAsync());

        // Unblocking requires a completely fresh pairing flow, not a resumed old credential: the
        // same clientId re-runs the full unpaired -> pairing_request -> pairing_confirm ->
        // pairing_ack round trip and succeeds again.
        await using BridgeConnection rePair = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await rePair.SendAsync(BridgeScenario.UnpairedHelloEnvelope());
        Envelope rePairHelloAck = await rePair.ReceiveAsync();
        Assert.Equal("hello_ack", rePairHelloAck.MessageType);
        string rePairSessionId = rePairHelloAck.SessionId!;
        await rePair.ReceiveAsync();  // capabilities

        await rePair.SendAsync(new Envelope("pairing_request", "message-request-2", rePairSessionId, null, new JsonObject()));
        Envelope rePairStatus = await rePair.ReceiveAsync();
        Assert.Equal("available", PairingStatusPayload.Decode(rePairStatus.Payload).State);

        string rePairCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await rePair.SendAsync(new Envelope("pairing_confirm", "message-confirm-2", rePairSessionId, null,
            new PairingConfirmPayload(rePairCode).Encode()));
        Envelope rePairConfirmOutcome = await rePair.ReceiveAsync();
        PairingOutcomePayload rePairConfirmDecoded = PairingOutcomePayload.Decode(rePairConfirmOutcome.Payload);
        Assert.Equal("credential_issued", rePairConfirmDecoded.Outcome);
        string rePairCredential = rePairConfirmDecoded.Credential!;

        await rePair.SendAsync(new Envelope("pairing_ack", "message-ack-2", rePairSessionId, null,
            new PairingAckPayload(rePairCredential).Encode()));
        Envelope rePairAckOutcome = await rePair.ReceiveAsync();
        Assert.Equal("trusted", PairingOutcomePayload.Decode(rePairAckOutcome.Payload).Outcome);

        await BridgeScenario.CloseAndQuitAsync(harness, rePair);
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
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

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
        Assert.Equal("paired", HelloAckPayload.Decode(reconnectHelloAck.Payload).ClientIdentityKind);
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
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string realCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        string wrongCode = DifferentCode(realCode);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(wrongCode).Encode()));
        Envelope outcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", outcome.MessageType);
        Assert.Equal("invalid", PairingOutcomePayload.Decode(outcome.Payload).Outcome);

        // An invalid code must not upgrade the session -- still Restricted, symmetric with the
        // first scenario's rejection.
        await connection.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId, null,
            new SubscribePayload(["character"]).Encode()));
        Envelope subscribeError = await connection.ReceiveAsync();
        Assert.Equal("error", subscribeError.MessageType);
        Assert.Equal("malformed_message", ErrorPayload.Decode(subscribeError.Payload).Code);

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
        PairingStatusPayload statusDecoded = PairingStatusPayload.Decode(status.Payload);
        Assert.Equal("available", statusDecoded.State);
        int? expiresInSecondsFirst = statusDecoded.ExpiresInSeconds;
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
        await reconnect.ReceiveAsync(); // capabilities (ignored)

        // Query pairing status on the reconnected session: should resume the existing challenge, not start fresh.
        await reconnect.SendAsync(new Envelope("pairing_request", "message-request-2", newSessionId, null, new JsonObject()));
        Envelope resumeStatus = await reconnect.ReceiveAsync();
        Assert.Equal("pairing_status", resumeStatus.MessageType);
        // "in_progress" means same client already owns an active challenge; no new code, same code re-displayed.
        PairingStatusPayload resumeStatusDecoded = PairingStatusPayload.Decode(resumeStatus.Payload);
        Assert.Equal("in_progress", resumeStatusDecoded.State);
        int? expiresInSecondsResumed = resumeStatusDecoded.ExpiresInSeconds;
        Assert.NotNull(expiresInSecondsResumed);
        Assert.True(expiresInSecondsResumed > 0);
        // Never larger than the original (the same challenge's countdown keeps running through the
        // disconnect, it isn't reset to a fresh full duration) -- a loose lower bound only guards
        // against an unrelated pathological delay, not the (fast, local) real latency here.
        Assert.True(expiresInSecondsResumed <= expiresInSecondsFirst,
            $"Resume expiry {expiresInSecondsResumed}s must not exceed the original {expiresInSecondsFirst}s");
        Assert.True(expiresInSecondsResumed >= expiresInSecondsFirst - 60,
            $"Resume expiry {expiresInSecondsResumed}s dropped implausibly far below the original {expiresInSecondsFirst}s");

        // Confirm with the original code on the reconnected session: should succeed.
        await reconnect.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", newSessionId, null,
            new PairingConfirmPayload(code, "Resume Test").Encode()));
        Envelope confirmOutcome = await reconnect.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        // Complete the pairing handshake on the reconnected socket.
        await reconnect.SendAsync(new Envelope("pairing_ack", "message-ack-1", newSessionId, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await reconnect.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // Session upgrade lands on the reconnected socket; the subscription request is accepted
        // immediately without a fresh reconnect, while the unregistered area is rejected.
        await reconnect.SendAsync(new Envelope("subscribe", "message-sub-1", newSessionId, null,
            new SubscribePayload(["character"]).Encode()));
        Envelope subscriptionAck = await reconnect.ReceiveAsync();
        Assert.Equal("subscription_ack", subscriptionAck.MessageType);
        AssertUnregisteredSubscriptionAcknowledged(subscriptionAck, "message-sub-1", "character");

        // Verify the credential issued during grace-period pairing is actually persisted and works on a new connection,
        // not just an in-memory upgrade of this connection's session (matching FullPairingRoundTripUpgradesTheSessionAndAllowsSubscribe).
        await reconnect.CloseAsync();
        await using BridgeConnection credentialVerify = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await credentialVerify.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential, clientId: "client-1"));
        Envelope verifyHelloAck = await credentialVerify.ReceiveAsync();
        Assert.Equal("hello_ack", verifyHelloAck.MessageType);
        Assert.Equal("paired", HelloAckPayload.Decode(verifyHelloAck.Payload).ClientIdentityKind);

        await BridgeScenario.CloseAndQuitAsync(harness, credentialVerify);
    }

    /// <summary>
    /// Verifies the single-connected-client model (Phase 9 deferred): "other_device_pairing" is
    /// scoped by that model, not by two simultaneously live connections: a second clientId can
    /// only ever reach pairing_request while the owner's socket is
    /// down and the grace period hasn't elapsed yet." ConnectionSlot admits exactly one connected
    /// client at a time (BridgeWorkerPool rejects a second TCP-level connection attempt outright
    /// while the slot is held, before any WebSocket handshake), so client-1 must disconnect --
    /// while remaining within the 10-second reconnect grace period, where PairingSession still
    /// remembers it owns the active challenge -- before client-2 can even connect. Client-2's
    /// pairing_request then returns "other_device_pairing", disclosing nothing about the owning
    /// device or code. Once client-2 disconnects, client-1 reconnects and resumes its own
    /// still-owned challenge to completion, proving a competing device never displaces the owner.
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
        Assert.Equal("available", PairingStatusPayload.Decode(status1.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Client-1 disconnects mid-challenge, within the 10-second reconnect grace period --
        // ConnectionSlot's exclusivity means client-2 cannot even be accepted until this releases
        // it, and PairingSession's ownership of the challenge survives the disconnect regardless.
        connection1.Abort();

        // Client-2 connects while client-1 is within its grace period: rides out
        // ConnectWithRetryAsync's bounded short retry/backoff over the brief window where the slot
        // is still busy completing client-1's abrupt teardown, the same established pattern
        // AbruptDisconnectAfterPairingRecoversViaBoundedRetryWithAFreshSession proves.
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
        // "other_device_pairing" means a different clientId owns the active challenge; expiresInSeconds
        // is never present -- PairingStatusPayload.Decode itself rejects a present expiresInSeconds
        // for this state, so a successful decode already proves the key's absence.
        PairingStatusPayload status2Decoded = PairingStatusPayload.Decode(status2.Payload);
        Assert.Equal("other_device_pairing", status2Decoded.State);
        Assert.Null(status2Decoded.ExpiresInSeconds);

        // Client-2's own session is still Restricted (unpaired): subscription is rejected, proving
        // "other_device_pairing" grants it no capability the wire contract wouldn't already deny.
        await connection2.SendAsync(new Envelope("subscribe", "message-sub-2", sessionId2, null,
            new SubscribePayload(["character"]).Encode()));
        Envelope error2 = await connection2.ReceiveAsync();
        Assert.Equal("error", error2.MessageType);
        Assert.Equal("malformed_message", ErrorPayload.Decode(error2.Payload).Code);

        // Client-2 cannot proceed without the code (it was never disclosed) -- rather than
        // speculate on a fake-code confirm attempt, it simply disconnects, freeing the slot so the
        // real owner can reconnect and finish what it started.
        await connection2.CloseAsync();

        // Client-1 reconnects, still within the grace period, and resumes its own still-owned
        // challenge -- a competing device observing "other_device_pairing" never displaces it.
        await using BridgeConnection reconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnect.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-1"));
        Envelope reconnectHelloAck = await reconnect.ReceiveAsync();
        Assert.Equal("hello_ack", reconnectHelloAck.MessageType);
        string sessionId1Reconnected = reconnectHelloAck.SessionId!;
        await reconnect.ReceiveAsync(); // capabilities (ignored)

        await reconnect.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId1Reconnected, null,
            new PairingConfirmPayload(code, "Device 1").Encode()));
        Envelope confirmOutcome = await reconnect.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await reconnect.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId1Reconnected, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await reconnect.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // Client-1's session is now Full (paired); the subscription request is allowed, but no state
        // area is currently registered so the acknowledgement rejects the requested area.
        await reconnect.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId1Reconnected, null,
            new SubscribePayload(["character"]).Encode()));
        Envelope subscriptionAck = await reconnect.ReceiveAsync();
        Assert.Equal("subscription_ack", subscriptionAck.MessageType);
        AssertUnregisteredSubscriptionAcknowledged(subscriptionAck, "message-sub-1", "character");

        await BridgeScenario.CloseAndQuitAsync(harness, reconnect);
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
        PairingStatusPayload status1Decoded = PairingStatusPayload.Decode(status1.Payload);
        Assert.Equal("available", status1Decoded.State);
        Assert.True(status1Decoded.ExpiresInSeconds > 0);

        string code1 = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Client-1 disconnects mid-challenge (no confirm).
        connection1.Abort();

        // Wait for the grace period to elapse (kPairingReconnectGracePeriod = 10 seconds).
        // This is a genuine, full-duration timeout proof (per ai/context/integration/testing.md
        // and integration/README.md, we only use these for periods that aren't proven elsewhere
        // at the C++ unit-test level). The hard-limit and other decision points are already
        // covered by pairing_session_test.cpp; this integration layer proves the full
        // end-to-end wire behavior, including the real socket delays. A wider-than-minimal margin
        // (3s, not 1s) absorbs that the grace clock only starts once the bridge's blocking read
        // actually observes the abort -- itself subject to OS/scheduling latency that can vary
        // more on a shared CI runner than on a local machine.
        await Task.Delay(TimeSpan.FromSeconds(13));

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
        PairingStatusPayload status2Decoded = PairingStatusPayload.Decode(status2.Payload);
        Assert.Equal("available", status2Decoded.State);
        Assert.True(status2Decoded.ExpiresInSeconds > 0);

        string code2 = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Codes must be different (fresh challenge generated for client-2, not client-1's expired code).
        Assert.NotEqual(code1, code2);

        // Client-2 can complete pairing with the fresh code.
        await connection2.SendAsync(new Envelope("pairing_confirm", "message-confirm-2", sessionId2, null,
            new PairingConfirmPayload(code2, "Device 2").Encode()));
        Envelope confirmOutcome = await connection2.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await connection2.SendAsync(new Envelope("pairing_ack", "message-ack-2", sessionId2, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await connection2.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // Session upgrade lands on the same connection; the subscription request is allowed, but
        // the currently unregistered state area is rejected.
        await connection2.SendAsync(new Envelope("subscribe", "message-sub-2", sessionId2, null,
            new SubscribePayload(["character"]).Encode()));
        Envelope subscriptionAck = await connection2.ReceiveAsync();
        Assert.Equal("subscription_ack", subscriptionAck.MessageType);
        AssertUnregisteredSubscriptionAcknowledged(subscriptionAck, "message-sub-2", "character");

        await BridgeScenario.CloseAndQuitAsync(harness, connection2);
    }

    /// <summary>Asserts the Full-session response for a currently unregistered state area.</summary>
    private static void AssertUnregisteredSubscriptionAcknowledged(
        Envelope acknowledgement,
        string correlationId,
        string stateArea)
    {
        Assert.Equal(correlationId, acknowledgement.CorrelationId);
        SubscriptionAckPayload decoded = SubscriptionAckPayload.Decode(acknowledgement.Payload);
        Assert.Empty(decoded.AcceptedStateAreas);
        Assert.Equal([stateArea], decoded.RejectedStateAreas);
    }

    /// <summary>
    /// Verifies the renotify mechanics: the owning client can manually
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
        PairingStatusPayload statusDecoded = PairingStatusPayload.Decode(status.Payload);
        Assert.Equal("available", statusDecoded.State);
        int initialExpiry = statusDecoded.ExpiresInSeconds!.Value;
        Assert.True(initialExpiry > 0);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // First renotify: should succeed. The code is redisplayed via in-game notification,
        // not sent over the wire; expiresInSeconds is not in renotified outcome (protocol/schema/README.md line 277).
        await connection.SendAsync(BridgeScenario.RenotifyEnvelope(sessionId, "message-renotify-1"));
        Envelope renotifyOutcome1 = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", renotifyOutcome1.MessageType);
        Assert.Equal("message-renotify-1", renotifyOutcome1.CorrelationId);
        PairingOutcomePayload renotify1Decoded = PairingOutcomePayload.Decode(renotifyOutcome1.Payload);
        Assert.Equal("renotified", renotify1Decoded.Outcome);
        // renotified outcome carries no expiry, code, or error info (code is redisplayed in-game, not over wire).
        Assert.Null(renotify1Decoded.RetryAfterSeconds);
        Assert.False(renotifyOutcome1.Payload.ContainsKey("expiresInSeconds"));
        // The wire outcome alone doesn't prove the code was actually redisplayed in-game -- confirm
        // the harness actually observed a second PAIRING_CODE line (the first was the original
        // display), carrying the same code.
        string renotifiedCode1 = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        Assert.Equal(code, renotifiedCode1);

        // Immediate second renotify: hits the 5-second cooldown, returns remaining wait.
        // renotify_cooldown carries retryAfterSeconds but not expiresInSeconds (protocol/schema/README.md lines 277-285).
        await connection.SendAsync(BridgeScenario.RenotifyEnvelope(sessionId, "message-renotify-2"));
        Envelope renotifyCooldown = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", renotifyCooldown.MessageType);
        Assert.Equal("message-renotify-2", renotifyCooldown.CorrelationId);
        PairingOutcomePayload renotifyCooldownDecoded = PairingOutcomePayload.Decode(renotifyCooldown.Payload);
        Assert.Equal("renotify_cooldown", renotifyCooldownDecoded.Outcome);
        int retryAfter = renotifyCooldownDecoded.RetryAfterSeconds!.Value;
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
        PairingOutcomePayload renotify3Decoded = PairingOutcomePayload.Decode(renotifyOutcome3.Payload);
        Assert.Equal("renotified", renotify3Decoded.Outcome);
        Assert.Null(renotify3Decoded.RetryAfterSeconds);
        Assert.False(renotifyOutcome3.Payload.ContainsKey("expiresInSeconds"));
        string renotifiedCode3 = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        Assert.Equal(code, renotifiedCode3);

        // Confirm the original code: should succeed (code remained valid through renotifies).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", PairingOutcomePayload.Decode(confirmOutcome.Payload).Outcome);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies the auto-renotify path: when a client submits an incorrect code,
    /// the outcome is "invalid" (challenge persists), the bridge auto-redisplays the code via
    /// in-game notification (harness signals this with PAIRING_CODE_INCORRECT line), and the original
    /// expiry deadline is preserved, not extended or reset. The owning client may continue attempting
    /// with the original code.
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
        PairingStatusPayload statusDecoded = PairingStatusPayload.Decode(status.Payload);
        Assert.Equal("available", statusDecoded.State);
        int initialExpiry = statusDecoded.ExpiresInSeconds!.Value;

        string correctCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        string wrongCode = DifferentCode(correctCode);

        // Send wrong code: challenge persists (not hard-limit yet), returns "invalid" outcome.
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-wrong", sessionId, null,
            new PairingConfirmPayload(wrongCode).Encode()));
        Envelope invalidOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", invalidOutcome.MessageType);
        Assert.Equal("message-confirm-wrong", invalidOutcome.CorrelationId);
        PairingOutcomePayload invalidDecoded = PairingOutcomePayload.Decode(invalidOutcome.Payload);
        Assert.Equal("invalid", invalidDecoded.Outcome);
        // Invalid outcome carries no credential or error info (challenge is still active).
        Assert.Null(invalidDecoded.Credential);
        Assert.Null(invalidDecoded.RetryAfterSeconds);

        // Bridge auto-renotifies the code via in-game notification; harness signals this.
        string? autoRenotifySignal = await harness.ReadLineAsync();
        if (autoRenotifySignal is null)
        {
            throw new InvalidOperationException(
                $"Harness ended before the auto-renotify signal. Stderr: {harness.StandardError}");
        }
        Assert.StartsWith("PAIRING_CODE_INCORRECT ", autoRenotifySignal);
        Assert.Equal(correctCode, autoRenotifySignal.AsSpan()["PAIRING_CODE_INCORRECT ".Length..].ToString());

        // Query pairing status: should be "in_progress" (same client owns active challenge).
        // A wrong code leaves the challenge, code, and expiry in place -- it does NOT refresh or
        // extend the deadline, distinct from the code being freshly generated.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId, null, new JsonObject()));
        Envelope statusAfterWrongCode = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", statusAfterWrongCode.MessageType);
        PairingStatusPayload statusAfterWrongCodeDecoded = PairingStatusPayload.Decode(statusAfterWrongCode.Payload);
        Assert.Equal("in_progress", statusAfterWrongCodeDecoded.State);
        int refreshedExpiry = statusAfterWrongCodeDecoded.ExpiresInSeconds!.Value;
        Assert.True(refreshedExpiry > 0);
        // Never larger than the original (a wrong attempt must not extend it), and not further
        // below it than plausible round-trip latency (proving it wasn't reset to a fresh full
        // duration either -- distinguishing "left in place" from both possible regressions).
        Assert.True(refreshedExpiry <= initialExpiry,
            $"Expiry {refreshedExpiry}s must not exceed the original {initialExpiry}s -- a wrong code must not extend it");
        Assert.True(refreshedExpiry >= initialExpiry - 5,
            $"Expiry {refreshedExpiry}s dropped too far below the original {initialExpiry}s for mere round-trip latency");

        // The bridge's 1-second pacing interval applies to evaluated pairing_confirm attempts only
        // (this scenario's own wrong attempt above already started that clock) -- wait it out so
        // this final, correct attempt is genuinely evaluated rather than paced out.
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        // Confirm with the original correct code: should succeed (wrong attempt didn't consume it).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-correct", sessionId, null,
            new PairingConfirmPayload(correctCode).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", PairingOutcomePayload.Decode(confirmOutcome.Payload).Outcome);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies pacing_limited: a pairing_confirm sent too soon after the
    /// previous evaluated attempt is rejected as "pacing_limited" without counting as a wrong
    /// attempt, distinct from the hard-limit's "hard_limit_reached". Once the 1-second pacing
    /// interval elapses, the same correct code is genuinely evaluated and succeeds -- proving the
    /// paced-out attempt was never evaluated at all.
    /// </summary>
    [Fact]
    public async Task PacingLimitedRejectsAnEvaluatedAttemptTooSoonWithoutConsumingIt()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string correctCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        string wrongCode = DifferentCode(correctCode);

        // First attempt (wrong): genuinely evaluated, starts the pacing clock.
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(wrongCode).Encode()));
        Envelope firstOutcome = await connection.ReceiveAsync();
        Assert.Equal("invalid", PairingOutcomePayload.Decode(firstOutcome.Payload).Outcome);
        Assert.StartsWith("PAIRING_CODE_INCORRECT ", await harness.ReadLineAsync());

        // Second attempt, immediately after: too soon to be evaluated, even with the correct code.
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-2", sessionId, null,
            new PairingConfirmPayload(correctCode).Encode()));
        Envelope pacedOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", pacedOutcome.MessageType);
        Assert.Equal("message-confirm-2", pacedOutcome.CorrelationId);
        PairingOutcomePayload pacedDecoded = PairingOutcomePayload.Decode(pacedOutcome.Payload);
        Assert.Equal("pacing_limited", pacedDecoded.Outcome);
        int retryAfter = pacedDecoded.RetryAfterSeconds!.Value;
        // A positive fractional wait rounds up to the minimum safe whole-second wait.
        Assert.Equal(1, retryAfter);
        // pacing_limited carries no credential (never evaluated, so nothing was issued or consumed).
        Assert.Null(pacedDecoded.Credential);

        // Once the pacing interval elapses, the correct code is genuinely evaluated and succeeds.
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-3", sessionId, null,
            new PairingConfirmPayload(correctCode).Encode()));
        Envelope finalOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", finalOutcome.MessageType);
        Assert.Equal("credential_issued", PairingOutcomePayload.Decode(finalOutcome.Payload).Outcome);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies the hard limit on wrong pairing attempts: after 5 incorrect codes,
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
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string correctCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Submit 4 wrong codes: each should yield "invalid" and auto-renotify. Consecutive attempts
        // must be spaced out by more than kPairingRenotifyCooldown (5s) -- not just the shorter
        // kPairingConfirmPacingInterval (1s) -- or they'd either be paced out (pacing_limited,
        // never evaluated) or evaluated but skip auto-renotify (its own independent 5-second
        // cooldown, per pairing_session.cpp's TryConfirmCode), leaving no PAIRING_CODE_INCORRECT
        // line for this test to read.
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(5.1));
            }
            string wrongCode = DifferentCode(correctCode);
            await connection.SendAsync(new Envelope("pairing_confirm", $"message-confirm-wrong-{attempt}", sessionId, null,
                new PairingConfirmPayload(wrongCode).Encode()));
            Envelope outcome = await connection.ReceiveAsync();
            Assert.Equal("pairing_outcome", outcome.MessageType);
            Assert.Equal($"message-confirm-wrong-{attempt}", outcome.CorrelationId);
            Assert.Equal("invalid", PairingOutcomePayload.Decode(outcome.Payload).Outcome);

            // Each wrong attempt triggers auto-renotify: harness signals with PAIRING_CODE_INCORRECT.
            string? signal = await harness.ReadLineAsync();
            if (signal is null)
            {
                throw new InvalidOperationException(
                    $"Harness ended before the auto-renotify signal for attempt {attempt}. Stderr: {harness.StandardError}");
            }
            Assert.StartsWith("PAIRING_CODE_INCORRECT ", signal);
        }

        // After 4 attempts, challenge still exists: query status to prove boundary.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-boundary", sessionId, null, new JsonObject()));
        Envelope boundaryStatus = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", boundaryStatus.MessageType);
        Assert.Equal("in_progress", PairingStatusPayload.Decode(boundaryStatus.Payload).State);

        // 5th wrong attempt: hits hard limit, challenge is cancelled. Spaced out from the 4th
        // attempt's own evaluated confirm, same pacing reason as the loop above.
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        string fifthWrongCode = DifferentCode(correctCode);
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-wrong-5", sessionId, null,
            new PairingConfirmPayload(fifthWrongCode).Encode()));
        Envelope hardLimitOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", hardLimitOutcome.MessageType);
        Assert.Equal("message-confirm-wrong-5", hardLimitOutcome.CorrelationId);
        PairingOutcomePayload hardLimitDecoded = PairingOutcomePayload.Decode(hardLimitOutcome.Payload);
        Assert.Equal("hard_limit_reached", hardLimitDecoded.Outcome);
        // hard_limit_reached carries no credential or retry info (challenge is immediately cancelled, not retryable).
        Assert.Null(hardLimitDecoded.Credential);
        Assert.Null(hardLimitDecoded.RetryAfterSeconds);

        // Hard limit is signalled distinctly from per-attempt auto-renotify: harness prints PAIRING_ATTEMPTS_EXHAUSTED.
        string? exhaustedSignal = await harness.ReadLineAsync();
        if (exhaustedSignal is null)
        {
            throw new InvalidOperationException(
                $"Harness ended before the attempts-exhausted signal. Stderr: {harness.StandardError}");
        }
        Assert.Equal("PAIRING_ATTEMPTS_EXHAUSTED", exhaustedSignal);

        // Challenge is cleared by the hard limit: fresh pairing_request starts a new one with a fresh code.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId, null, new JsonObject()));
        Envelope freshStatus = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", freshStatus.MessageType);
        PairingStatusPayload freshStatusDecoded = PairingStatusPayload.Decode(freshStatus.Payload);
        Assert.Equal("available", freshStatusDecoded.State);
        Assert.True(freshStatusDecoded.ExpiresInSeconds!.Value > 0, "Fresh challenge must carry expiresInSeconds");

        string freshCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        // The new challenge has a different code (fresh attempt, not retry of the exhausted one).
        Assert.NotEqual(correctCode, freshCode);

        // Fresh code works (confirms the challenge was genuinely cleared and restarted).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-fresh", sessionId, null,
            new PairingConfirmPayload(freshCode).Encode()));
        Envelope freshConfirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", freshConfirmOutcome.MessageType);
        Assert.Equal("credential_issued", PairingOutcomePayload.Decode(freshConfirmOutcome.Payload).Outcome);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies pairing_cancel on an active challenge: the owning client can
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
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code1 = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        // Cancel the active challenge.
        await connection.SendAsync(BridgeScenario.CancelEnvelope(sessionId, "message-cancel-1"));
        Envelope cancelOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", cancelOutcome.MessageType);
        Assert.Equal("message-cancel-1", cancelOutcome.CorrelationId);
        PairingOutcomePayload cancelDecoded = PairingOutcomePayload.Decode(cancelOutcome.Payload);
        Assert.Equal("cancelled", cancelDecoded.Outcome);
        // cancelled outcome carries no credential or retry info (challenge is cleared, not retryable).
        Assert.Null(cancelDecoded.Credential);
        Assert.Null(cancelDecoded.RetryAfterSeconds);

        // Fresh pairing_request: should start a new challenge with a fresh code.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId, null, new JsonObject()));
        Envelope freshStatus = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", freshStatus.MessageType);
        PairingStatusPayload freshStatusDecoded = PairingStatusPayload.Decode(freshStatus.Payload);
        Assert.Equal("available", freshStatusDecoded.State);
        Assert.True(freshStatusDecoded.ExpiresInSeconds!.Value > 0);

        string code2 = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        // Fresh challenge must have a new code, not the cancelled one.
        Assert.NotEqual(code1, code2);

        // Fresh code works (proves challenge was genuinely cleared).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-fresh", sessionId, null,
            new PairingConfirmPayload(code2).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        Assert.Equal("credential_issued", PairingOutcomePayload.Decode(confirmOutcome.Payload).Outcome);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies pairing_cancel on a pending credential: the owning client can
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
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", confirmOutcome.MessageType);
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string pendingCredential = confirmDecoded.Credential!;

        // Before ack: cancel the pending credential.
        await connection.SendAsync(BridgeScenario.CancelEnvelope(sessionId, "message-cancel-pending"));
        Envelope cancelOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", cancelOutcome.MessageType);
        Assert.Equal("message-cancel-pending", cancelOutcome.CorrelationId);
        PairingOutcomePayload cancelDecoded = PairingOutcomePayload.Decode(cancelOutcome.Payload);
        Assert.Equal("cancelled", cancelDecoded.Outcome);
        // cancelled outcome carries no credential or retry info.
        Assert.Null(cancelDecoded.Credential);
        Assert.Null(cancelDecoded.RetryAfterSeconds);

        // The pending credential is not persisted: fresh pairing_request starts a new challenge
        // (not a resume of the cancelled pending state).
        await connection.SendAsync(new Envelope("pairing_request", "message-request-2", sessionId, null, new JsonObject()));
        Envelope freshStatus = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", freshStatus.MessageType);
        Assert.Equal("available", PairingStatusPayload.Decode(freshStatus.Payload).State);

        string freshCode = await BridgeScenario.ReadPairingCodeReportAsync(harness);
        Assert.NotEqual(code, freshCode);

        // Fresh code works (proves the cancelled credential was not persisted and challenge was cleared).
        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-fresh", sessionId, null,
            new PairingConfirmPayload(freshCode).Encode()));
        Envelope freshConfirmOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", freshConfirmOutcome.MessageType);
        PairingOutcomePayload freshConfirmDecoded = PairingOutcomePayload.Decode(freshConfirmOutcome.Payload);
        Assert.Equal("credential_issued", freshConfirmDecoded.Outcome);
        string freshCredential = freshConfirmDecoded.Credential!;
        // Fresh pairing must issue a new credential, not re-use the cancelled pending one.
        Assert.NotEqual(pendingCredential, freshCredential);

        // Complete fresh pairing to confirm it works.
        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-fresh", sessionId, null,
            new PairingAckPayload(freshCredential).Encode()));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", ackOutcome.MessageType);
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // Verify the cancelled credential was truly not persisted: attempting to reconnect with it fails.
        await connection.CloseAsync();
        await using BridgeConnection reconnectWithCancelled = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnectWithCancelled.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(pendingCredential));
        Envelope rejectionError = await reconnectWithCancelled.ReceiveAsync();
        Assert.Equal("error", rejectionError.MessageType);
        // Cancelled pending credential must be rejected as unauthenticated, not persisted.
        Assert.Equal("unauthenticated", ErrorPayload.Decode(rejectionError.Payload).Code);

        // Fresh credential works on a new connection (contrasting proof).
        await reconnectWithCancelled.CloseAsync();
        await using BridgeConnection reconnectWithFresh = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await reconnectWithFresh.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(freshCredential));
        Envelope freshHelloAck = await reconnectWithFresh.ReceiveAsync();
        Assert.Equal("hello_ack", freshHelloAck.MessageType);
        Assert.Equal("paired", HelloAckPayload.Decode(freshHelloAck.Payload).ClientIdentityKind);

        await BridgeScenario.CloseAndQuitAsync(harness, reconnectWithFresh);
    }

    /// <summary>
    /// Verifies pairing_cancel when idle: the owning client can cancel
    /// even when no active challenge or pending credential exists. The outcome is "already_idle"
    /// (idempotent, no error), proving cancel is safe to retry and doesn't treat absence as a fault.
    /// </summary>
    [Fact]
    public async Task CancelWhenIdleReturnsAlreadyIdle()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Session is connected but no pairing activity yet: idle state.
        // Send cancel: should return "already_idle", not an error.
        await connection.SendAsync(BridgeScenario.CancelEnvelope(sessionId, "message-cancel-idle"));
        Envelope alreadyIdleOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", alreadyIdleOutcome.MessageType);
        Assert.Equal("message-cancel-idle", alreadyIdleOutcome.CorrelationId);
        PairingOutcomePayload alreadyIdleDecoded = PairingOutcomePayload.Decode(alreadyIdleOutcome.Payload);
        Assert.Equal("already_idle", alreadyIdleDecoded.Outcome);
        // already_idle carries no credential, expiry, or retry info (idempotent, no state change).
        Assert.Null(alreadyIdleDecoded.Credential);
        Assert.Null(alreadyIdleDecoded.RetryAfterSeconds);
        Assert.False(alreadyIdleOutcome.Payload.ContainsKey("expiresInSeconds"));

        // Idempotent: send cancel again, should still return "already_idle".
        await connection.SendAsync(BridgeScenario.CancelEnvelope(sessionId, "message-cancel-idle-2"));
        Envelope secondIdleOutcome = await connection.ReceiveAsync();
        Assert.Equal("pairing_outcome", secondIdleOutcome.MessageType);
        Assert.Equal("message-cancel-idle-2", secondIdleOutcome.CorrelationId);
        Assert.Equal("already_idle", PairingOutcomePayload.Decode(secondIdleOutcome.Payload).Outcome);

        // Session remains functional: pairing_request works normally.
        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("pairing_status", status.MessageType);
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Verifies Reset Trust's live-session effect: among the four administrative reasons, resetting
    /// trust while a just-paired client is connected
    /// force-closes its session immediately, mirroring
    /// <see cref="RevokeWhileConnectedClosesTheLiveSessionImmediately"/>'s same proof for revoke,
    /// via the harness's own <c>trust_reset &lt;clientId&gt;</c> shortcut
    /// (bridge/harness/dovahlink_bridge_harness.cpp) straight to <c>TrustStore::ResetTrust</c>.
    /// </summary>
    [Fact]
    public async Task TrustResetWhileConnectedClosesTheLiveSessionImmediately()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // Unlike a bare reconnect-only proof, this connection stays open through the reset below --
        // proving the live session itself is force-closed, not merely that a later reconnect
        // attempt would be rejected.
        await harness.WriteLineAsync("trust_reset client-1");
        Assert.Equal("TRUST_RESET client-1", await harness.ReadLineAsync());

        // The Bridge sends session_invalidated(reason: "trust_reset") before force-closing (Stage
        // 7, ai/context/protocol/security.md's "Administrative session invalidation"), mirroring
        // RevokeWhileConnectedClosesTheLiveSessionImmediately's same proof for revoke.
        Envelope invalidated = await connection.ReceiveAsync();
        Assert.Equal("session_invalidated", invalidated.MessageType);
        Assert.Equal(sessionId, invalidated.SessionId);
        Assert.Null(invalidated.CorrelationId);
        Assert.Equal("trust_reset", SessionInvalidatedPayload.Decode(invalidated.Payload).Reason);

        InvalidOperationException closeException =
            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        Assert.Contains("ended before a complete protocol message was received", closeException.Message);
        await connection.CloseAsync();

        // The connection slot isn't wedged: a fresh, unrelated connection still succeeds right
        // after, mirroring RevokeWhileConnectedClosesTheLiveSessionImmediately's same proof.
        await using BridgeConnection healthyConnection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await healthyConnection.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-2"));
        Envelope healthyHelloAck = await healthyConnection.ReceiveAsync();
        Assert.Equal("hello_ack", healthyHelloAck.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, healthyConnection);
    }

    /// <summary>
    /// Verifies Factory Reset's live-session effect: among the four administrative reasons, a
    /// factory reset while a just-paired client is connected
    /// force-closes its session immediately, mirroring
    /// <see cref="TrustResetWhileConnectedClosesTheLiveSessionImmediately"/>'s same proof for trust
    /// reset, via the harness's own <c>factory_reset</c> shortcut
    /// (bridge/harness/dovahlink_bridge_harness.cpp) straight to <c>TrustStore::Reset</c>. Unlike
    /// trust reset, Factory Reset disconnects the active session unconditionally -- it targets no
    /// specific clientId, since it wipes every known device record. Also verifies the distinct
    /// reconnect outcome ai/context/protocol/security.md documents for this case: because Factory
    /// Reset deletes the Known Device record and any revocation tombstone entirely, a reconnect
    /// with the old credential gets the generic "unauthenticated" outcome -- the same as a device
    /// that was never paired -- rather than the "revoked"/"blocked" outcomes
    /// <see cref="RevokedCredentialReconnectReceivesARevokedOutcome"/> and the block scenario prove
    /// for those two reasons, which still leave a classifiable tombstone behind.
    /// </summary>
    [Fact]
    public async Task FactoryResetWhileConnectedClosesTheLiveSessionImmediately()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
        Envelope status = await connection.ReceiveAsync();
        Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

        string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

        await connection.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
            new PairingConfirmPayload(code).Encode()));
        Envelope confirmOutcome = await connection.ReceiveAsync();
        PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
        Assert.Equal("credential_issued", confirmDecoded.Outcome);
        string credential = confirmDecoded.Credential!;

        await connection.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
            new PairingAckPayload(credential).Encode()));
        Envelope ackOutcome = await connection.ReceiveAsync();
        Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

        // Unlike a bare reconnect-only proof, this connection stays open through the reset below --
        // proving the live session itself is force-closed, not merely that a later reconnect
        // attempt would be rejected.
        await harness.WriteLineAsync("factory_reset");
        Assert.Equal("FACTORY_RESET", await harness.ReadLineAsync());

        // The Bridge sends session_invalidated(reason: "factory_reset") before force-closing
        // (Stage 7, ai/context/protocol/security.md's "Administrative session invalidation"),
        // mirroring TrustResetWhileConnectedClosesTheLiveSessionImmediately's same proof.
        Envelope invalidated = await connection.ReceiveAsync();
        Assert.Equal("session_invalidated", invalidated.MessageType);
        Assert.Equal(sessionId, invalidated.SessionId);
        Assert.Null(invalidated.CorrelationId);
        Assert.Equal("factory_reset", SessionInvalidatedPayload.Decode(invalidated.Payload).Reason);

        InvalidOperationException closeException =
            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        Assert.Contains("ended before a complete protocol message was received", closeException.Message);
        await connection.CloseAsync();

        // A reconnect with the old (now-unrecorded) credential gets the generic "unauthenticated"
        // outcome, distinct from "revoked"/"blocked" -- security.md's "the Bridge rejects it
        // through the ordinary unrecognized-credential/unpaired path, the same as a device that
        // was never paired, not through the blocked/revoked outcomes above", since Factory Reset
        // deleted the Known Device record entirely rather than leaving a revocation tombstone.
        await using BridgeConnection staleReconnect = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await staleReconnect.SendAsync(BridgeScenario.TrustedDeviceHelloEnvelope(credential));
        Envelope staleError = await staleReconnect.ReceiveAsync();
        Assert.Equal("error", staleError.MessageType);
        Assert.Equal("unauthenticated", ErrorPayload.Decode(staleError.Payload).Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => staleReconnect.ReceiveAsync());
        await staleReconnect.CloseAsync();

        // The connection slot isn't wedged: a fresh, unrelated connection still succeeds right
        // after, mirroring TrustResetWhileConnectedClosesTheLiveSessionImmediately's same proof.
        await using BridgeConnection healthyConnection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await healthyConnection.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-2"));
        Envelope healthyHelloAck = await healthyConnection.ReceiveAsync();
        Assert.Equal("hello_ack", healthyHelloAck.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, healthyConnection);
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
