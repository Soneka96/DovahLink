using System.Net.WebSockets;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Client.Transport;

/// <summary>Tests for <see cref="PublicConnectionContext"/>.</summary>
public class PublicConnectionContextTests
{
    /// <summary>Verifies that <see cref="PublicConnectionContext.TrySend"/> forwards the exact payload and lane to the wrapped connection and returns its result when the connection admits the message.</summary>
    [Fact]
    public void TrySend_ConnectionAdmitsMessage_ForwardsPayloadAndLaneAndReturnsTrue()
    {
        var connection = new FakePublicWebSocketConnection(new MemoryStream()) { TrySendResult = true };
        var context = new PublicConnectionContext(connection);
        byte[] payload = "response"u8.ToArray();

        bool result = context.TrySend(payload, PublicOutboundLane.Data);

        Assert.True(result);
        Assert.Equal(payload, Assert.Single(connection.SentPayloads));
        Assert.Equal(PublicOutboundLane.Data, Assert.Single(connection.SentLanes));
    }

    /// <summary>Verifies that <see cref="PublicConnectionContext.TrySend"/> returns <see langword="false"/> when the wrapped connection does not admit the message, rather than swallowing that outcome.</summary>
    [Fact]
    public void TrySend_ConnectionRejectsMessage_ReturnsFalse()
    {
        var connection = new FakePublicWebSocketConnection(new MemoryStream()) { TrySendResult = false };
        var context = new PublicConnectionContext(connection);
        byte[] payload = "response"u8.ToArray();

        bool result = context.TrySend(payload, PublicOutboundLane.ControlOrRecovery);

        Assert.False(result);
        Assert.Equal(payload, Assert.Single(connection.SentPayloads));
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, Assert.Single(connection.SentLanes));
    }

    /// <summary>Verifies that <see cref="PublicConnectionContext.TrySendSnapshot"/> forwards the exact area and payload to the wrapped connection and returns its result when the connection admits the value.</summary>
    [Fact]
    public void TrySendSnapshot_ConnectionAdmitsValue_ForwardsAreaAndPayloadAndReturnsTrue()
    {
        var connection = new FakePublicWebSocketConnection(new MemoryStream()) { TrySendSnapshotResult = true };
        var context = new PublicConnectionContext(connection);
        var areaId = new StateAreaId("example_area");
        byte[] payload = "snapshot"u8.ToArray();

        bool result = context.TrySendSnapshot(areaId, payload);

        Assert.True(result);
        (StateAreaId SentAreaId, byte[] SentPayload) sent = Assert.Single(connection.SentSnapshots);
        Assert.Equal(areaId, sent.SentAreaId);
        Assert.Equal(payload, sent.SentPayload);
    }

    /// <summary>Verifies that <see cref="PublicConnectionContext.TrySendSnapshot"/> returns <see langword="false"/> when the wrapped connection declines the value, rather than swallowing that outcome.</summary>
    [Fact]
    public void TrySendSnapshot_ConnectionDeclinesValue_ReturnsFalse()
    {
        var connection = new FakePublicWebSocketConnection(new MemoryStream()) { TrySendSnapshotResult = false };
        var context = new PublicConnectionContext(connection);

        bool result = context.TrySendSnapshot(new StateAreaId("example_area"), "snapshot"u8.ToArray());

        Assert.False(result);
    }

    /// <summary>Verifies that <see cref="PublicConnectionContext.RequestClose"/> forwards to the wrapped connection.</summary>
    [Fact]
    public void RequestClose_DelegatesToConnection()
    {
        var connection = new FakePublicWebSocketConnection(new MemoryStream());
        var context = new PublicConnectionContext(connection);

        context.RequestClose();

        Assert.Equal(1, connection.RequestCloseCalls);
    }

    /// <summary>Verifies that <see cref="PublicConnectionContext.RemainingOutboundCapacity"/> forwards the exact lane to the wrapped connection and returns its result.</summary>
    [Fact]
    public void RemainingOutboundCapacity_DelegatesLaneAndResultToConnection()
    {
        var connection = new FakePublicWebSocketConnection(new MemoryStream()) { RemainingOutboundCapacityResult = 5 };
        var context = new PublicConnectionContext(connection);

        int result = context.RemainingOutboundCapacity(PublicOutboundLane.Data);

        Assert.Equal(5, result);
    }

    /// <summary>
    /// Verifies that <see cref="IPublicConnectionContext"/>'s members expose no raw WebSocket, stream,
    /// or socket type -- nor any type that derives from one -- proving application code reached
    /// through this capability cannot obtain transport ownership through a future member typed to a
    /// concrete subtype such as <see cref="System.Net.Sockets.NetworkStream"/> or
    /// <see cref="System.Net.WebSockets.ClientWebSocket"/>.
    /// </summary>
    [Fact]
    public void Interface_Members_ExposeNoRawTransportType()
    {
        Type[] disallowedTypes = [typeof(WebSocket), typeof(Stream), typeof(System.Net.Sockets.Socket)];

        foreach (var method in typeof(IPublicConnectionContext).GetMethods())
        {
            AssertNotAssignableToAnyDisallowedType(method.ReturnType, disallowedTypes);
            foreach (var parameter in method.GetParameters())
            {
                AssertNotAssignableToAnyDisallowedType(parameter.ParameterType, disallowedTypes);
            }
        }
    }

    /// <summary>Asserts that <paramref name="actualType"/> is not, and does not derive from, any type in <paramref name="disallowedTypes"/>.</summary>
    /// <param name="actualType">The return or parameter type under test.</param>
    /// <param name="disallowedTypes">The raw transport types no interface member may expose, directly or through a subtype.</param>
    private static void AssertNotAssignableToAnyDisallowedType(Type actualType, Type[] disallowedTypes)
    {
        foreach (Type disallowedType in disallowedTypes)
        {
            Assert.False(
                disallowedType.IsAssignableFrom(actualType),
                $"{actualType} is or derives from the disallowed transport type {disallowedType}.");
        }
    }
}
