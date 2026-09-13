using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Sessions;

/// <summary>
/// The real <see cref="ISessionTerminationNotifier"/> implementation over the public WebSocket
/// transport: looks up the target's exact live connection through <see cref="IPublicSessionConnectionRegistry"/>
/// -- never "whichever connection is currently active" -- so a stale target can never reach a
/// different, newer connection that reused the single admission slot after this target's own
/// connection already ended.
/// </summary>
public sealed class PublicSessionTerminationNotifier : ISessionTerminationNotifier
{
    /// <summary>Resolves a target's exact live connection, if it still has one.</summary>
    private readonly IPublicSessionConnectionRegistry registry;

    /// <summary>Encodes the <c>session_invalidated</c> notification.</summary>
    private readonly IPublicEnvelopeCodec codec;

    /// <summary>Supplies the <c>playContextId</c> stamped onto the notification.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Creates a session-termination notifier over the public transport.</summary>
    /// <param name="registry">Resolves a target's exact live connection, if it still has one.</param>
    /// <param name="codec">Encodes the <c>session_invalidated</c> notification.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto the notification.</param>
    public PublicSessionTerminationNotifier(IPublicSessionConnectionRegistry registry, IPublicEnvelopeCodec codec, IPlayContextTracker playContextTracker)
    {
        this.registry = registry;
        this.codec = codec;
        this.playContextTracker = playContextTracker;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A target no longer registered (already disconnected on its own) is a no-op. Otherwise, a
    /// best-effort <c>session_invalidated</c> send -- contained here so an encoding or transport
    /// failure can never skip the forced close below -- is always followed by requesting the
    /// connection's close, regardless of whether the notification itself succeeded.
    /// </remarks>
    public Task NotifyAndCloseAsync(SessionInvalidationTarget target, CancellationToken cancellationToken = default)
    {
        IPublicConnectionContext? connection = registry.TryGet(target.SessionId, target.ConnectionId);
        if (connection is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            PlayContextSnapshot snapshot = playContextTracker.GetSnapshot();
            var payload = new SessionInvalidatedPayload { Reason = target.Reason };
            byte[] bytes = codec.Encode(
                PublicMessageType.SessionInvalidated,
                Guid.NewGuid().ToString(),
                target.SessionId.ToString(),
                null,
                snapshot.Current?.ToString(),
                null,
                payload);
            connection.TrySend(bytes, PublicOutboundLane.ControlOrRecovery);
        }
        catch
        {
            // Best-effort: a failed notification must never skip the forced close below.
        }

        connection.RequestClose();
        return Task.CompletedTask;
    }
}
