using DovahLink.Host.Client.Readiness;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A recording in-memory stand-in for <see cref="IPublicGameplayReadinessPublisher"/> that publishes nothing.</summary>
public sealed class FakeGameplayReadinessPublisher : IPublicGameplayReadinessPublisher
{
    /// <summary>The session identities <see cref="Bind"/> was called with, in order.</summary>
    public List<SessionId> BoundSessions { get; } = [];

    /// <summary>How many times <see cref="Unbind"/> was called.</summary>
    public int UnbindCount { get; private set; }

    /// <inheritdoc/>
    public void Bind(IPublicConnectionContext connectionContext, SessionId sessionId) => BoundSessions.Add(sessionId);

    /// <inheritdoc/>
    public void Unbind() => UnbindCount++;
}
