using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises state-area validation while no production state area is registered.</summary>
public class StateScenarioTests
{
    /// <summary>Verifies that an unregistered subscription is acknowledged without a snapshot.</summary>
    [Fact]
    public async Task UnregisteredSubscriptionReturnsRejectedAckWithoutSnapshot()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("subscribe", "message-sub-1", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray("example_area") }));

        Envelope ack = await connection.ReceiveAsync();
        Assert.Equal("subscription_ack", ack.MessageType);
        Assert.Equal("message-sub-1", ack.CorrelationId);
        Assert.Empty(ack.Payload["acceptedStateAreas"]!.AsArray());
        Assert.Equal(["example_area"], ack.Payload["rejectedStateAreas"]!.AsArray().Select(v => v!.GetValue<string>()));

        // The next response must be the pong, proving that the rejected
        // subscription did not produce a state_snapshot.
        await connection.SendAsync(new Envelope("ping", "message-ping-1", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that duplicate unregistered areas are deduplicated in the acknowledgement.</summary>
    [Fact]
    public async Task DuplicateUnregisteredAreasAreDeduplicatedWithoutSnapshots()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("subscribe", "message-sub-duplicate", sessionId, null,
            new JsonObject
            {
                ["stateAreas"] = new JsonArray("example_area", "example_area", "other_area", "other_area"),
            }));

        Envelope ack = await connection.ReceiveAsync();
        Assert.Equal("subscription_ack", ack.MessageType);
        Assert.Empty(ack.Payload["acceptedStateAreas"]!.AsArray());
        Assert.Equal(["example_area", "other_area"],
            ack.Payload["rejectedStateAreas"]!.AsArray().Select(v => v!.GetValue<string>()));

        await connection.SendAsync(new Envelope("ping", "message-ping-duplicate", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an empty subscription is acknowledged without a snapshot.</summary>
    [Fact]
    public async Task EmptySubscriptionReturnsEmptyAckWithoutSnapshot()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("subscribe", "message-sub-empty", sessionId, null,
            new JsonObject { ["stateAreas"] = new JsonArray() }));

        Envelope ack = await connection.ReceiveAsync();
        Assert.Equal("subscription_ack", ack.MessageType);
        Assert.Empty(ack.Payload["acceptedStateAreas"]!.AsArray());
        Assert.Empty(ack.Payload["rejectedStateAreas"]!.AsArray());

        await connection.SendAsync(new Envelope("ping", "message-ping-empty", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that a snapshot request for an unregistered area is rejected.</summary>
    [Fact]
    public async Task SnapshotRequestForUnregisteredAreaReturnsUnsupportedCapabilityError()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("snapshot_request", "message-snapshot-unsupported", sessionId, null,
            new JsonObject { ["stateArea"] = "example_area" }));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-snapshot-unsupported", error.CorrelationId);
        Assert.Equal("unsupported_capability", error.Payload["code"]!.GetValue<string>());

        await connection.SendAsync(new Envelope("ping", "message-ping-after-snapshot-error", sessionId, null,
            new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }
}
