using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IPlayContextTracker"/> whose current context a test can set directly.</summary>
public sealed class FakePlayContextTracker : IPlayContextTracker
{
    /// <inheritdoc/>
    public PlayContextId? Current { get; private set; }

    /// <inheritdoc/>
    public event Action<PlayContextTransition>? Transitioned;

    /// <inheritdoc/>
    public void NotifyTransition(PlayContextId newPlayContextId)
    {
        var transition = new PlayContextTransition(Current, newPlayContextId);
        Current = newPlayContextId;
        Transitioned?.Invoke(transition);
    }
}
