using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IStateAuthorityLifecycle"/> whose current value a test can rotate directly.</summary>
public sealed class FakeStateAuthorityLifecycle : IStateAuthorityLifecycle
{
    /// <summary>Backing field for <see cref="Current"/>.</summary>
    private StateAuthorityId current = new(Guid.NewGuid());

    /// <summary>One-shot callback invoked after a read captures its return value but before the getter returns.</summary>
    public Action? OnCurrentRead { get; set; }

    /// <inheritdoc/>
    public StateAuthorityId Current
    {
        get
        {
            StateAuthorityId value = current;
            Action? callback = OnCurrentRead;
            OnCurrentRead = null;
            callback?.Invoke();
            return value;
        }
    }

    /// <inheritdoc/>
    public bool IsFaulted => false;

    /// <summary>Never raised: no test against this fake exercises the runtime-mint-failure path.</summary>
    event Action? IStateAuthorityLifecycle.FatalFailureOccurred
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event Action<StateAuthorityId>? Rotated;

    /// <summary>Whether any caller currently holds a live registration on <see cref="Rotated"/>.</summary>
    public bool HasSubscribers => Rotated is not null;

    /// <summary>Sets <see cref="Current"/> to a freshly minted value and raises <see cref="Rotated"/> with it, as a real lifecycle would at a continuity-loss rotation.</summary>
    public void NotifyRotated()
    {
        current = new StateAuthorityId(Guid.NewGuid());
        Rotated?.Invoke(current);
    }
}
