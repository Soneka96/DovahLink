using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IPlayContextTracker"/> whose current context a test can set directly.</summary>
public sealed class FakePlayContextTracker : IPlayContextTracker
{
    private readonly object publicationGate = new();

    /// <inheritdoc/>
    public PlayContextId? Current { get; private set; }

    /// <inheritdoc/>
    public long TransitionGeneration { get; private set; }

    /// <inheritdoc/>
    public event Action<PlayContextTransition>? Transitioned;

    /// <inheritdoc/>
    public PlayContextSnapshot GetSnapshot() => new(Current, TransitionGeneration);

    /// <inheritdoc/>
    public void NotifyTransition(PlayContextId newPlayContextId)
    {
        lock (publicationGate)
        {
            var transition = new PlayContextTransition(Current, newPlayContextId);
            Current = newPlayContextId;
            TransitionGeneration++;
            Transitioned?.Invoke(transition);
        }
    }
}
