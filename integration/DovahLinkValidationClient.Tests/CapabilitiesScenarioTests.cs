using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises capability negotiation and validation scenarios.</summary>
public class CapabilitiesScenarioTests
{
    /// <summary>Verifies that the bridge advertises the registered character capability.</summary>
    [Fact]
    public async Task BridgeAdvertisesTheOneRegisteredCharacterCapability()
    {
        (HarnessProcess harness, BridgeConnection connection, string _, Envelope capabilities) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // protocol/schema/README.md: "the only registered capability is
        // state.character version 1."
        JsonArray registered = capabilities.Payload["capabilities"]!.AsArray();
        Assert.Single(registered);
        Assert.Equal("state.character", registered[0]!["id"]!.GetValue<string>());
        Assert.Equal(1, registered[0]!["version"]!.GetValue<int>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an empty client capability list receives no response.</summary>
    [Fact]
    public async Task EmptyClientCapabilitiesListIsAcceptedWithNoResponse()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, _) = await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("capabilities", "message-cap-1", sessionId, null,
            new JsonObject { ["capabilities"] = new JsonArray() }));

        // v1's registered client-side capabilities is deliberately the
        // empty set (protocol/schema/README.md): a valid, empty list gets
        // no response at all. Proven by sending a ping right after and
        // seeing its pong arrive next, not a stray reply to capabilities.
        await connection.SendAsync(new Envelope("ping", "message-ping-1", sessionId, null, new JsonObject()));
        Envelope response = await connection.ReceiveAsync();
        Assert.Equal("pong", response.MessageType);
        Assert.Equal("message-ping-1", response.CorrelationId);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that a registered client capability receives no response.</summary>
    [Fact]
    public async Task RegisteredCapabilityIdIsAcceptedWithNoResponse()
    {
        // The negation of the empty-list case: a real, registered ID (not
        // just an empty list) must also be accepted with no response, not
        // just inferred from the empty-list path.
        (HarnessProcess harness, BridgeConnection connection, string sessionId, _) = await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("capabilities", "message-cap-valid", sessionId, null,
            new JsonObject
            {
                ["capabilities"] = new JsonArray(new JsonObject { ["id"] = "state.character", ["version"] = 1 }),
            }));

        await connection.SendAsync(new Envelope("ping", "message-ping-valid", sessionId, null, new JsonObject()));
        Envelope response = await connection.ReceiveAsync();
        Assert.Equal("pong", response.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an unknown capability is rejected without closing the connection.</summary>
    [Fact]
    public async Task UnregisteredCapabilityIdIsRejectedWithoutClosingTheConnection()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, _) = await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("capabilities", "message-cap-2", sessionId, null,
            new JsonObject
            {
                ["capabilities"] = new JsonArray(new JsonObject { ["id"] = "made.up.capability", ["version"] = 1 }),
            }));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-cap-2", error.CorrelationId);
        Assert.Equal("unsupported_capability", error.Payload["code"]!.GetValue<string>());

        // One protocol violation does not close the connection -- the limit
        // is 3 within 30 seconds (ai/context/protocol/security.md). Proven
        // the same way: a ping sent right after still gets a pong back.
        await connection.SendAsync(new Envelope("ping", "message-ping-2", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that one unknown entry rejects an otherwise valid capability list.</summary>
    [Fact]
    public async Task OneUnregisteredIdAmongOthersStillRejectsTheWholeMessage()
    {
        // Proves the validation loop actually checks every entry rather
        // than stopping after the first (which happens to be valid here).
        (HarnessProcess harness, BridgeConnection connection, string sessionId, _) = await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("capabilities", "message-cap-mixed", sessionId, null,
            new JsonObject
            {
                ["capabilities"] = new JsonArray(
                    new JsonObject { ["id"] = "state.character", ["version"] = 1 },
                    new JsonObject { ["id"] = "made.up.capability", ["version"] = 1 }),
            }));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("unsupported_capability", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }
}
