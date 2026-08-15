using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises character subscription, snapshots, and state-area validation.</summary>
/// <remarks>Phase 1 validates pull-based delivery; unsolicited state events are deferred.</remarks>
public class StateScenarioTests
{
    /// <summary>Verifies subscription acknowledgment and the initial revision-one snapshot.</summary>
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
        // Health, magicka, and stamina are explicitly unavailable in Phase 1.
        Assert.Null(data["health"]);
        Assert.Null(data["magicka"]);
        Assert.Null(data["stamina"]);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an unknown state area is rejected without a snapshot.</summary>
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

    /// <summary>Verifies mixed state-area subscription acceptance and rejection.</summary>
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

    /// <summary>Verifies that a later snapshot reflects a level change and higher revision.</summary>
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

    /// <summary>Verifies that duplicate subscription areas are acknowledged once.</summary>
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

    /// <summary>Verifies that an empty state-area list produces no snapshot.</summary>
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

    /// <summary>Verifies that a v1 pull advances the revision even without a state change.</summary>
    [Fact]
    public async Task RevisionAdvancesOnEachPullEvenWithoutAnInterveningStateChangeForV1()
    {
        // RevisionTracker::StartSnapshot (application/revision_tracker.cpp)
        // increments unconditionally on every call when given no fingerprint
        // -- v1's published, frozen behavior, which this phase must not
        // reinterpret even though v2 (see the sibling test below) now
        // compares captured state instead.
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

    /// <summary>Verifies that a v2 pull with no intervening state change reuses the revision.</summary>
    [Fact]
    public async Task RevisionIsReusedOnEachPullWithoutAnInterveningStateChangeForV2()
    {
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        await harness.WaitForReadyAsync();
        // An active play context is required for a real, persistent
        // PlayContext-owned RevisionTracker: without one, the v2 NoContext
        // path uses a throwaway tracker rebuilt on every call, which would
        // trivially -- and uninformatively -- report revision 1 every time.
        await harness.WriteLineAsync("new_game");
        await harness.ReadLineAsync();  // PLAY_CONTEXT <id>

        await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await connection.SendAsync(BridgeScenario.HelloEnvelope(
            BridgeScenario.ValidHexToken, supportedProtocolVersions: [1, 2], clientId: ClientIdentity.Current.ToString()));
        Envelope helloAck = await connection.ReceiveAsync();
        Assert.Equal("hello_ack", helloAck.MessageType);
        string sessionId = helloAck.SessionId!;
        await connection.ReceiveAsync();  // capabilities

        await connection.SendAsync(new Envelope(2, "subscribe", "message-sub-v2-1", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("character") }));
        await connection.ReceiveAsync();  // subscription_ack
        Envelope firstSnapshot = await connection.ReceiveAsync();
        Assert.Equal(1, firstSnapshot.Payload["revision"]!.GetValue<int>());
        Assert.Null(firstSnapshot.Payload["data"]!["level"]);

        await connection.SendAsync(new Envelope(2, "snapshot_request", "message-snap-v2-nochange", sessionId, null,
            new JsonObject { ["stateArea"] = "character" }));
        Envelope secondSnapshot = await connection.ReceiveAsync();
        // A freshly-begun play context's captured character state is
        // unavailable (no harness command populates it) on both pulls --
        // still unchanged, so the revision is still correctly reused.
        Assert.Equal(1, secondSnapshot.Payload["revision"]!.GetValue<int>());
        Assert.Null(secondSnapshot.Payload["data"]!["level"]);

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that an unknown snapshot area returns an unsupported-capability error.</summary>
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
