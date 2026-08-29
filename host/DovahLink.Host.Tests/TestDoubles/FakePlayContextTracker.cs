using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IPlayContextTracker"/> whose current context a test can set directly.</summary>
public sealed class FakePlayContextTracker : IPlayContextTracker
{
    /// <summary>Guards <see cref="current"/> and <see cref="transitionGeneration"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Serializes state updates with ordered transition callback publication.</summary>
    private readonly object publicationGate = new();

    /// <summary>Backing field for <see cref="Current"/>.</summary>
    private PlayContextId? current;

    /// <summary>Backing field for <see cref="TransitionGeneration"/>.</summary>
    private long transitionGeneration;

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
    public long TransitionGeneration
    {
        get
        {
            lock (gate)
            {
                return transitionGeneration;
            }
        }
    }

    /// <inheritdoc/>
    public event Action<PlayContextTransition>? Transitioned;

    /// <inheritdoc/>
    public PlayContextSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new(current, transitionGeneration);
        }
    }

    /// <inheritdoc/>
    public void NotifyTransition(PlayContextId newPlayContextId)
    {
        lock (publicationGate)
        {
            PlayContextTransition transition;
            lock (gate)
            {
                transition = new PlayContextTransition(current, newPlayContextId);
                current = newPlayContextId;
                transitionGeneration++;
            }

            Transitioned?.Invoke(transition);
        }
    }
}
