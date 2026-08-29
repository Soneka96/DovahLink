using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;

namespace DovahLink.Host.Tests.Sessions;

/// <summary>Tests for <see cref="SessionRegistry"/>.</summary>
public class SessionRegistryTests
{
    /// <summary>Verifies that a newly created session reports as active.</summary>
    [Fact]
    public void Create_ReturnsAnActiveSession()
    {
        var registry = new SessionRegistry();

        SessionId sessionId = registry.Create(ClientId.NewId());

        Assert.True(registry.IsActive(sessionId));
    }

    /// <summary>Verifies that a session id that was never created is reported as not active.</summary>
    [Fact]
    public void IsActive_UnknownSession_ReturnsFalse()
    {
        var registry = new SessionRegistry();

        Assert.False(registry.IsActive(SessionId.NewId()));
    }

    /// <summary>Verifies that an invalidated session stops being reported as active.</summary>
    [Fact]
    public void Invalidate_ActiveSession_BecomesInactive()
    {
        var registry = new SessionRegistry();
        SessionId sessionId = registry.Create(ClientId.NewId());

        registry.Invalidate(sessionId);

        Assert.False(registry.IsActive(sessionId));
    }

    /// <summary>Verifies that invalidating an already-invalidated session is a harmless no-op.</summary>
    [Fact]
    public void Invalidate_AlreadyInvalidatedSession_StaysInactive()
    {
        var registry = new SessionRegistry();
        SessionId sessionId = registry.Create(ClientId.NewId());
        registry.Invalidate(sessionId);

        registry.Invalidate(sessionId);

        Assert.False(registry.IsActive(sessionId));
    }

    /// <summary>Verifies that invalidating an unknown session id does not throw.</summary>
    [Fact]
    public void Invalidate_UnknownSession_DoesNotThrow()
    {
        var registry = new SessionRegistry();

        registry.Invalidate(SessionId.NewId());
    }

    /// <summary>Verifies that invalidating all of a client's sessions affects every one of them but no other client's.</summary>
    [Fact]
    public void InvalidateAllForClient_InvalidatesOnlyThatClientsSessions()
    {
        var registry = new SessionRegistry();
        ClientId targetClient = ClientId.NewId();
        SessionId firstSession = registry.Create(targetClient);
        SessionId secondSession = registry.Create(targetClient);
        SessionId otherClientSession = registry.Create(ClientId.NewId());

        registry.InvalidateAllForClient(targetClient);

        Assert.False(registry.IsActive(firstSession));
        Assert.False(registry.IsActive(secondSession));
        Assert.True(registry.IsActive(otherClientSession));
    }

    /// <summary>
    /// Verifies the stale-session guarantee: reconnecting for the same client always creates a
    /// fresh session id, and the prior session never becomes active again.
    /// </summary>
    [Fact]
    public void Create_AfterInvalidatingPriorSessionForSameClient_NeverReactivatesOldSession()
    {
        var registry = new SessionRegistry();
        ClientId clientId = ClientId.NewId();
        SessionId firstSession = registry.Create(clientId);
        registry.Invalidate(firstSession);

        SessionId secondSession = registry.Create(clientId);

        Assert.NotEqual(firstSession, secondSession);
        Assert.False(registry.IsActive(firstSession));
        Assert.True(registry.IsActive(secondSession));
    }

    /// <summary>Verifies that session state does not survive a host restart: a fresh registry starts with no active sessions.</summary>
    [Fact]
    public void NewRegistry_StartsWithNoActiveSessions()
    {
        var priorRegistry = new SessionRegistry();
        SessionId sessionId = priorRegistry.Create(ClientId.NewId());

        var restartedRegistry = new SessionRegistry();

        Assert.False(restartedRegistry.IsActive(sessionId));
    }

    /// <summary>Verifies that back-to-back sessions for the same client never collide.</summary>
    [Fact]
    public void Create_CalledTwiceForSameClient_ReturnsDistinctActiveSessions()
    {
        var registry = new SessionRegistry();
        ClientId clientId = ClientId.NewId();

        SessionId first = registry.Create(clientId);
        SessionId second = registry.Create(clientId);

        Assert.NotEqual(first, second);
        Assert.True(registry.IsActive(first));
        Assert.True(registry.IsActive(second));
    }

    /// <summary>Verifies that invalidating a client with no sessions at all is a harmless no-op.</summary>
    [Fact]
    public void InvalidateAllForClient_ClientWithNoSessions_DoesNotThrow()
    {
        var registry = new SessionRegistry();

        registry.InvalidateAllForClient(ClientId.NewId());
    }

    /// <summary>Verifies that a client can still create fresh, active sessions after all of its prior sessions were invalidated.</summary>
    [Fact]
    public void Create_AfterInvalidateAllForClient_StillCreatesActiveSession()
    {
        var registry = new SessionRegistry();
        ClientId clientId = ClientId.NewId();
        registry.Create(clientId);
        registry.InvalidateAllForClient(clientId);

        SessionId newSession = registry.Create(clientId);

        Assert.True(registry.IsActive(newSession));
    }

    /// <summary>Verifies that concurrent creates and invalidations for distinct clients don't corrupt the shared dictionary.</summary>
    [Fact]
    public async Task ConcurrentCreateAndInvalidate_DistinctClients_AllEndUpCorrect()
    {
        var registry = new SessionRegistry();
        ClientId[] clientIds = Enumerable.Range(0, 20).Select(_ => ClientId.NewId()).ToArray();

        SessionId[] sessions = await Task.WhenAll(clientIds.Select(clientId => Task.Run(() => registry.Create(clientId))));
        await Task.WhenAll(sessions.Take(10).Select(sessionId => Task.Run(() => registry.Invalidate(sessionId))));

        Assert.Equal(20, sessions.Distinct().Count());
        for (int i = 0; i < sessions.Length; i++)
        {
            Assert.Equal(i >= 10, registry.IsActive(sessions[i]));
        }
    }

    /// <summary>Verifies that InvalidateAll invalidates every session regardless of client.</summary>
    [Fact]
    public void InvalidateAll_InvalidatesEverySessionForEveryClient()
    {
        var registry = new SessionRegistry();
        SessionId firstClientSession = registry.Create(ClientId.NewId());
        SessionId secondClientSession = registry.Create(ClientId.NewId());

        registry.InvalidateAll();

        Assert.False(registry.IsActive(firstClientSession));
        Assert.False(registry.IsActive(secondClientSession));
    }

    /// <summary>Verifies that InvalidateAll on a registry with no sessions is a harmless no-op.</summary>
    [Fact]
    public void InvalidateAll_NoSessions_DoesNotThrow()
    {
        var registry = new SessionRegistry();

        registry.InvalidateAll();
    }

    /// <summary>Verifies that calling InvalidateAll a second time is a harmless no-op.</summary>
    [Fact]
    public void InvalidateAll_CalledTwice_StaysInactive()
    {
        var registry = new SessionRegistry();
        SessionId sessionId = registry.Create(ClientId.NewId());

        registry.InvalidateAll();
        registry.InvalidateAll();

        Assert.False(registry.IsActive(sessionId));
    }
}
