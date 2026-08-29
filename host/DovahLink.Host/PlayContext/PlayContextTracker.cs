using DovahLink.Host.Identity;

namespace DovahLink.Host.PlayContext;

/// <summary>
/// The host's authoritative <see cref="PlayContextId"/> identity, established only by the
/// adapter's play-context transition notifications. Holds no play context until the first
/// notification, matching a restarted host having no play context until it resynchronizes with
/// the adapter.
/// </summary>
public interface IPlayContextTracker
{
    /// <summary>The currently active play context, or <see langword="null"/> if none has been notified yet.</summary>
    PlayContextId? Current { get; }

    /// <summary>Raised after every transition, including the first.</summary>
    event Action<PlayContextTransition>? Transitioned;

    /// <summary>Records a play-context transition notified by the adapter.</summary>
    /// <param name="newPlayContextId">The play context now active.</param>
    void NotifyTransition(PlayContextId newPlayContextId);
}

/// <inheritdoc cref="IPlayContextTracker"/>
public sealed class PlayContextTracker : IPlayContextTracker
{
    /// <summary>Guards <see cref="current"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The currently active play context, or <see langword="null"/> if none has been notified yet.</summary>
    private PlayContextId? current;

    /// <inheritdoc/>
    public PlayContextId? Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    /// <inheritdoc/>
    public event Action<PlayContextTransition>? Transitioned;

    /// <inheritdoc/>
    public void NotifyTransition(PlayContextId newPlayContextId)
    {
        PlayContextTransition transition;
        lock (gate)
        {
            transition = new PlayContextTransition(current, newPlayContextId);
            current = newPlayContextId;
        }

        Transitioned?.Invoke(transition);
    }
}
