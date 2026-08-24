using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises capability negotiation and validation scenarios.</summary>
public class CapabilitiesScenarioTests
{
    /// <summary>Verifies that the bridge advertises no state capabilities before a domain is registered.</summary>
    [Fact]
    public async Task BridgeAdvertisesNoRegisteredCapabilities()
    {
        (HarnessProcess harness, BridgeConnection connection, string _, Envelope capabilities) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        Assert.Empty(CapabilitiesPayload.Decode(capabilities.Payload).Capabilities);

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
            new CapabilitiesPayload([]).Encode()));

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

    /// <summary>Verifies that a non-empty client capability list is rejected without closing the connection.</summary>
    [Fact]
    public async Task NonEmptyClientCapabilitiesListIsRejectedWithoutClosingTheConnection()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, _) = await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("capabilities", "message-cap-nonempty", sessionId, null,
            new CapabilitiesPayload([new Capability("example.capability", 1)]).Encode()));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-cap-nonempty", error.CorrelationId);
        Assert.Equal("unsupported_capability", ErrorPayload.Decode(error.Payload).Code);

        await connection.SendAsync(new Envelope("ping", "message-ping-after-capability-error", sessionId, null, new JsonObject()));
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
            new CapabilitiesPayload([new Capability("made.up.capability", 1)]).Encode()));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-cap-2", error.CorrelationId);
        Assert.Equal("unsupported_capability", ErrorPayload.Decode(error.Payload).Code);

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
        // Proves the validation loop rejects every non-empty list while the
        // Bridge has no registered client capability IDs.
        (HarnessProcess harness, BridgeConnection connection, string sessionId, _) = await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("capabilities", "message-cap-mixed", sessionId, null,
            new CapabilitiesPayload([
                new Capability("example.capability", 1),
                new Capability("made.up.capability", 1),
            ]).Encode()));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("unsupported_capability", ErrorPayload.Decode(error.Payload).Code);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }
}
