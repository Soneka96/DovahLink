using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A configurable stand-in for <see cref="IClientMessageDispatcher"/> that records every dispatched call.</summary>
public sealed class FakeClientMessageDispatcher : IClientMessageDispatcher
{
    /// <summary>The result <see cref="DispatchAsync"/> returns for every call.</summary>
    public ClientDispatchResult ResultToReturn { get; set; } = new();

    /// <summary>Every call's client id, session id, and message type, in call order.</summary>
    public List<(ClientId ClientId, SessionId SessionId, PublicMessageType MessageType)> Calls { get; } = [];

    /// <summary>
    /// Optional hook invoked immediately before <see cref="DispatchAsync"/> returns
    /// <see cref="ResultToReturn"/>, letting a test simulate a concurrent administrative action (for
    /// example invalidating the session) landing during this exact dispatch.
    /// </summary>
    public Action? BeforeReturning { get; set; }

    /// <inheritdoc/>
    public Task<ClientDispatchResult> DispatchAsync(
        ClientId clientId,
        SessionId sessionId,
        IPublicConnectionContext connection,
        PublicEnvelope envelope,
        CancellationToken cancellationToken)
    {
        Calls.Add((clientId, sessionId, envelope.MessageType));
        BeforeReturning?.Invoke();
        return Task.FromResult(ResultToReturn);
    }
}
