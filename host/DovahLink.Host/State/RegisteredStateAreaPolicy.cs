namespace DovahLink.Host.State;

/// <summary>
/// The bounded set of state areas the host currently serves. Gates <c>subscribe</c> and
/// <c>snapshot_request</c> acceptance before any per-area queue, Snapshot-slot, or dirty-marker
/// state may be allocated for an area, per <c>ai/context/protocol/security.md</c>'s "maximum
/// registered state areas".
/// </summary>
public interface IRegisteredStateAreaPolicy
{
    /// <summary>The number of areas currently registered.</summary>
    int Count { get; }

    /// <summary>Whether <paramref name="areaId"/> is currently registered.</summary>
    /// <param name="areaId">The state area to check.</param>
    bool IsRegistered(StateAreaId areaId);

    /// <summary>
    /// Registers <paramref name="areaId"/> as an area the host now serves. Registering an
    /// already-registered area succeeds without consuming additional capacity.
    /// </summary>
    /// <param name="areaId">The state area to register.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="areaId"/> is registered -- either it already was,
    /// or it was newly registered within the bound; <see langword="false"/> when it was not already
    /// registered and registering it would exceed <see cref="Constants.MaxRegisteredStateAreas"/>.
    /// </returns>
    bool TryRegister(StateAreaId areaId);
}

/// <inheritdoc cref="IRegisteredStateAreaPolicy"/>
public sealed class RegisteredStateAreaPolicy : IRegisteredStateAreaPolicy
{
    /// <summary>Guards <see cref="registeredAreas"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Every currently registered state area.</summary>
    private readonly HashSet<StateAreaId> registeredAreas = new();

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return registeredAreas.Count;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsRegistered(StateAreaId areaId)
    {
        lock (gate)
        {
            return registeredAreas.Contains(areaId);
        }
    }

    /// <inheritdoc/>
    public bool TryRegister(StateAreaId areaId)
    {
        lock (gate)
        {
            if (registeredAreas.Contains(areaId))
            {
                return true;
            }

            if (registeredAreas.Count >= Constants.MaxRegisteredStateAreas)
            {
                return false;
            }

            registeredAreas.Add(areaId);
            return true;
        }
    }
}
