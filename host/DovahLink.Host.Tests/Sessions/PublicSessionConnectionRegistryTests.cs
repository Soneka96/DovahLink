using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Sessions;

/// <summary>Tests for <see cref="PublicSessionConnectionRegistry"/>.</summary>
public class PublicSessionConnectionRegistryTests
{
    /// <summary>Verifies that a registered connection is returned for its exact session and connection identity.</summary>
    [Fact]
    public void TryGet_RegisteredMatchingIdentity_ReturnsTheConnection()
    {
        var registry = new PublicSessionConnectionRegistry();
        SessionId sessionId = SessionId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        IPublicConnectionContext connection = new PublicConnectionContext(new FakePublicWebSocketConnection(Stream.Null));

        registry.Register(sessionId, connectionId, connection);

        Assert.Same(connection, registry.TryGet(sessionId, connectionId));
    }

    /// <summary>Verifies that an unregistered connection identity returns no match.</summary>
    [Fact]
    public void TryGet_NeverRegistered_ReturnsNull()
    {
        var registry = new PublicSessionConnectionRegistry();

        Assert.Null(registry.TryGet(SessionId.NewId(), ConnectionId.NewId()));
    }

    /// <summary>
    /// Verifies that a connection registered under one session identity is not returned for a
    /// different session identity, even when the connection identity matches.
    /// </summary>
    [Fact]
    public void TryGet_ConnectionRegisteredUnderDifferentSession_ReturnsNull()
    {
        var registry = new PublicSessionConnectionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        IPublicConnectionContext connection = new PublicConnectionContext(new FakePublicWebSocketConnection(Stream.Null));
        registry.Register(SessionId.NewId(), connectionId, connection);

        Assert.Null(registry.TryGet(SessionId.NewId(), connectionId));
    }

    /// <summary>Verifies that unregistering removes a connection's registration.</summary>
    [Fact]
    public void Unregister_RegisteredConnection_RemovesIt()
    {
        var registry = new PublicSessionConnectionRegistry();
        SessionId sessionId = SessionId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        registry.Register(sessionId, connectionId, new PublicConnectionContext(new FakePublicWebSocketConnection(Stream.Null)));

        registry.Unregister(connectionId);

        Assert.Null(registry.TryGet(sessionId, connectionId));
    }

    /// <summary>Verifies that unregistering a connection identity that was never registered is a harmless no-op.</summary>
    [Fact]
    public void Unregister_NeverRegistered_DoesNotThrow()
    {
        var registry = new PublicSessionConnectionRegistry();

        registry.Unregister(ConnectionId.NewId());
    }

    /// <summary>
    /// Verifies that unregistering one connection never affects a different, still-registered
    /// connection -- the exact guarantee a later administrative invalidation depends on.
    /// </summary>
    [Fact]
    public void Unregister_OneConnection_LeavesAnotherRegisteredConnectionUntouched()
    {
        var registry = new PublicSessionConnectionRegistry();
        SessionId firstSessionId = SessionId.NewId();
        ConnectionId firstConnectionId = ConnectionId.NewId();
        SessionId secondSessionId = SessionId.NewId();
        ConnectionId secondConnectionId = ConnectionId.NewId();
        IPublicConnectionContext secondConnection = new PublicConnectionContext(new FakePublicWebSocketConnection(Stream.Null));
        registry.Register(firstSessionId, firstConnectionId, new PublicConnectionContext(new FakePublicWebSocketConnection(Stream.Null)));
        registry.Register(secondSessionId, secondConnectionId, secondConnection);

        registry.Unregister(firstConnectionId);

        Assert.Null(registry.TryGet(firstSessionId, firstConnectionId));
        Assert.Same(secondConnection, registry.TryGet(secondSessionId, secondConnectionId));
    }

    /// <summary>
    /// Verifies that concurrent registration, unregistration, and lookup of many distinct
    /// connections -- exactly how production traffic uses this registry, since each connection's
    /// own admission handler registers/unregisters independently of a concurrent trust-mutation
    /// lookup -- never corrupts state or throws.
    /// </summary>
    [Fact]
    public async Task ConcurrentRegisterUnregisterAndTryGet_ManyDistinctConnections_NeverCorruptsStateOrThrows()
    {
        var registry = new PublicSessionConnectionRegistry();
        const int connectionCount = 100;
        var sessionIds = new SessionId[connectionCount];
        var connectionIds = new ConnectionId[connectionCount];
        for (int index = 0; index < connectionCount; index++)
        {
            sessionIds[index] = SessionId.NewId();
            connectionIds[index] = ConnectionId.NewId();
        }

        var tasks = new List<Task>();
        for (int index = 0; index < connectionCount; index++)
        {
            int capturedIndex = index;
            tasks.Add(Task.Run(() =>
            {
                IPublicConnectionContext connection = new PublicConnectionContext(new FakePublicWebSocketConnection(Stream.Null));
                registry.Register(sessionIds[capturedIndex], connectionIds[capturedIndex], connection);
                IPublicConnectionContext? found = registry.TryGet(sessionIds[capturedIndex], connectionIds[capturedIndex]);
                registry.Unregister(connectionIds[capturedIndex]);
                Assert.Same(connection, found);
            }));
        }

        await Task.WhenAll(tasks);

        for (int index = 0; index < connectionCount; index++)
        {
            Assert.Null(registry.TryGet(sessionIds[index], connectionIds[index]));
        }
    }
}
