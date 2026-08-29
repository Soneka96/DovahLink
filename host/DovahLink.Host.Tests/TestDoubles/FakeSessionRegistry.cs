using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>An in-memory stand-in for <see cref="ISessionRegistry"/> that records which clients were mass-invalidated.</summary>
public sealed class FakeSessionRegistry : ISessionRegistry
{
    /// <summary>The client each currently active session belongs to.</summary>
    private readonly Dictionary<SessionId, ClientId> activeSessionClients = new();

    /// <summary>Every client id passed to <see cref="InvalidateAllForClient"/>, in call order.</summary>
    public List<ClientId> InvalidateAllForClientCalls { get; } = [];

    /// <inheritdoc/>
    public SessionId Create(ClientId clientId)
    {
        SessionId sessionId = SessionId.NewId();
        activeSessionClients[sessionId] = clientId;
        return sessionId;
    }

    /// <inheritdoc/>
    public void Invalidate(SessionId sessionId) => activeSessionClients.Remove(sessionId);

    /// <inheritdoc/>
    public void InvalidateAllForClient(ClientId clientId)
    {
        InvalidateAllForClientCalls.Add(clientId);
        foreach (SessionId sessionId in activeSessionClients.Where(pair => pair.Value.Equals(clientId)).Select(pair => pair.Key).ToList())
        {
            activeSessionClients.Remove(sessionId);
        }
    }

    /// <inheritdoc/>
    public bool IsActive(SessionId sessionId) => activeSessionClients.ContainsKey(sessionId);
}
