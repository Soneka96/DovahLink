using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

// Phase 1 only proves the pull side of state delivery (subscribe and
// snapshot_request); a level change reaching an already-subscribed client
// as an unprompted state_event is deferred to Roadmap Phase 1.5
// (bridge/README.md's "Live event delivery is deferred to Phase 1.5"). No
// scenario here sends increase_level and expects a push -- every visibility
// check goes through a fresh subscribe or snapshot_request instead.
public class StateScenarioTests
{
    [Fact]
    public async Task SubscribeToCharacterReturnsAcceptedAckThenSnapshotAtRevisionOne()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope(1, "subscribe", "message-sub-1", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));

        Envelope ack = await connection.ReceiveAsync();
        Assert.Equal("subscription_ack", ack.MessageType);
        Assert.Equal("message-sub-1", ack.CorrelationId);
        Assert.Equal(["character"], ack.Payload["acceptedStateAreas"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Empty(ack.Payload["rejectedStateAreas"]!.AsArray());

        Envelope snapshot = await connection.ReceiveAsync();
        Assert.Equal("state_snapshot", snapshot.MessageType);
        // protocol/schema/README.md: "each initial snapshot also uses the
        // subscribe message ID as its correlationId."
        Assert.Equal("message-sub-1", snapshot.CorrelationId);
        Assert.Equal("character", snapshot.Payload["stateArea"]!.GetValue<string>());
        Assert.Equal(1, snapshot.Payload["revision"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(snapshot.Payload["occurredAt"]!.GetValue<string>()));
        JsonObject data = snapshot.Payload["data"]!.AsObject();
        Assert.Equal(5, data["level"]!.GetValue<int>());
        // TASK.md: health/magicka/stamina are explicitly null in Phase 1,
        // never a placeholder value.
        Assert.Null(data["health"]);
        Assert.Null(data["magicka"]);
        Assert.Null(data["stamina"]);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    [Fact]
    public async Task UnregisteredStateAreaIsRejectedInSubscriptionAckWithoutASnapshot()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope(1, "subscribe", "message-sub-unknown", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("inventory") }));

        Envelope ack = await connection.ReceiveAsync();
        Assert.Equal("subscription_ack", ack.MessageType);
        Assert.Empty(ack.Payload["acceptedStateAreas"]!.AsArray());
        Assert.Equal(["inventory"], ack.Payload["rejectedStateAreas"]!.AsArray().Select(v => v!.GetValue<string>()));

        // An unregistered area in subscribe is reported through
        // rejectedStateAreas, not treated as a protocol violation
        // (application/subscription_handler.hpp) -- and gets no snapshot.
        // Proven by the next message being the pong that answers this
        // ping, not a stray snapshot for an area that was never accepted.
        await connection.SendAsync(new Envelope(1, "ping", "message-ping-1", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    [Fact]
    public async Task MixedSubscribeAcceptsRegisteredAreaAndRejectsUnknownOneWithOneSnapshotOnly()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope(1, "subscribe", "message-sub-mixed", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character", "inventory") }));

        Envelope ack = await connection.ReceiveAsync();
        Assert.Equal(["character"], ack.Payload["acceptedStateAreas"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal(["inventory"], ack.Payload["rejectedStateAreas"]!.AsArray().Select(v => v!.GetValue<string>()));

        Envelope snapshot = await connection.ReceiveAsync();
        Assert.Equal("state_snapshot", snapshot.MessageType);
        Assert.Equal("character", snapshot.Payload["stateArea"]!.GetValue<string>());

        // Exactly one snapshot (character's), none for the rejected area --
        // proven the same way, by the next message being a pong.
        await connection.SendAsync(new Envelope(1, "ping", "message-ping-2", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    [Fact]
    public async Task SnapshotRequestAfterIncreaseLevelReturnsTheNewValueAtAHigherRevision()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope(1, "subscribe", "message-sub-2", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        await connection.ReceiveAsync();  // subscription_ack
        Envelope initialSnapshot = await connection.ReceiveAsync();
        Assert.Equal(5, initialSnapshot.Payload["data"]!["level"]!.GetValue<int>());
        Assert.Equal(1, initialSnapshot.Payload["revision"]!.GetValue<int>());

        await harness.WriteLineAsync("increase_level");
        Assert.Equal("LEVEL 6", await harness.ReadLineAsync());

        await connection.SendAsync(new Envelope(1, "snapshot_request", "message-snap-1", sessionId, null,
            new JsonObject { ["stateArea"] = "character" }));

        Envelope freshSnapshot = await connection.ReceiveAsync();
        Assert.Equal("state_snapshot", freshSnapshot.MessageType);
        // protocol/schema/README.md: "later snapshots use the
        // snapshot_request message ID that caused them", not subscribe's.
        Assert.Equal("message-snap-1", freshSnapshot.CorrelationId);
        Assert.Equal(6, freshSnapshot.Payload["data"]!["level"]!.GetValue<int>());
        Assert.Equal(2, freshSnapshot.Payload["revision"]!.GetValue<int>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    [Fact]
    public async Task DuplicateAreasInSubscribeAreDedupedInTheAckAndYieldExactlyOneSnapshot()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Exercises subscription_handler.cpp's alreadySeen dedup on both
        // sides: "character" listed twice must appear once in accepted,
        // "inventory" listed twice must appear once in rejected.
        await connection.SendAsync(new Envelope(1, "subscribe", "message-sub-dup", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character", "character", "inventory", "inventory") }));

        Envelope ack = await connection.ReceiveAsync();
        Assert.Equal(["character"], ack.Payload["acceptedStateAreas"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal(["inventory"], ack.Payload["rejectedStateAreas"]!.AsArray().Select(v => v!.GetValue<string>()));

        Envelope snapshot = await connection.ReceiveAsync();
        Assert.Equal("state_snapshot", snapshot.MessageType);

        // Exactly one snapshot total, not two for the duplicated accepted
        // area -- proven the same way, by the next message being a pong.
        await connection.SendAsync(new Envelope(1, "ping", "message-ping-3", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    [Fact]
    public async Task EmptyStateAreasListIsAcknowledgedWithNoSnapshot()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope(1, "subscribe", "message-sub-empty", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray() }));

        Envelope ack = await connection.ReceiveAsync();
        Assert.Equal("subscription_ack", ack.MessageType);
        Assert.Empty(ack.Payload["acceptedStateAreas"]!.AsArray());
        Assert.Empty(ack.Payload["rejectedStateAreas"]!.AsArray());

        await connection.SendAsync(new Envelope(1, "ping", "message-ping-4", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    [Fact]
    public async Task RevisionAdvancesOnEachPullEvenWithoutAnInterveningStateChange()
    {
        // RevisionTracker::StartSnapshot (application/revision_tracker.cpp)
        // increments unconditionally on every call -- a second pull with no
        // increase_level in between still lands at a higher revision, same
        // (unchanged) data. Documents real behavior: revision tracks "a
        // fresh baseline was captured," not "the value changed."
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope(1, "subscribe", "message-sub-3", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        await connection.ReceiveAsync();  // subscription_ack
        Envelope firstSnapshot = await connection.ReceiveAsync();
        Assert.Equal(1, firstSnapshot.Payload["revision"]!.GetValue<int>());
        Assert.Equal(5, firstSnapshot.Payload["data"]!["level"]!.GetValue<int>());

        await connection.SendAsync(new Envelope(1, "snapshot_request", "message-snap-nochange", sessionId, null,
            new JsonObject { ["stateArea"] = "character" }));
        Envelope secondSnapshot = await connection.ReceiveAsync();
        Assert.Equal(2, secondSnapshot.Payload["revision"]!.GetValue<int>());
        Assert.Equal(5, secondSnapshot.Payload["data"]!["level"]!.GetValue<int>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    [Fact]
    public async Task SnapshotRequestForAnUnregisteredAreaReturnsUnsupportedCapabilityError()
    {
        // Deliberately different from subscribe's soft rejectedStateAreas
        // handling: snapshot_request has no equivalent partial-accept
        // field, so an unknown area maps to a hard error instead
        // (application/subscription_handler.hpp's HandleSnapshotRequest doc).
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope(1, "snapshot_request", "message-snap-unknown", sessionId, null,
            new JsonObject { ["stateArea"] = "inventory" }));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-snap-unknown", error.CorrelationId);
        Assert.Equal("unsupported_capability", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }
}
