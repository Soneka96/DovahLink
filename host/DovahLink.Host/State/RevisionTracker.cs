using DovahLink.Host.Identity;

namespace DovahLink.Host.State;

/// <summary>
/// Tracks each state area's revision independently per play context: one authoritative state
/// store per state area per active play context, with the revision advancing only on change.
/// </summary>
public interface IRevisionTracker
{
    /// <summary>Reads a state area's current revision within a play context.</summary>
    /// <param name="playContextId">The play context the state area belongs to.</param>
    /// <param name="areaId">The state area.</param>
    /// <returns>The area's current revision, or <see cref="RevisionNumber.Initial"/> if it has never changed.</returns>
    RevisionNumber Current(PlayContextId playContextId, StateAreaId areaId);

    /// <summary>Advances a state area's revision because its authoritative state actually changed.</summary>
    /// <param name="playContextId">The play context the state area belongs to.</param>
    /// <param name="areaId">The state area.</param>
    /// <returns>The new, advanced revision.</returns>
    RevisionNumber AdvanceOnChange(PlayContextId playContextId, StateAreaId areaId);

    /// <summary>
    /// Invalidates every state area's revision within a play context, so each one starts again
    /// from <see cref="RevisionNumber.Initial"/> under that context.
    /// </summary>
    /// <param name="playContextId">The play context to invalidate.</param>
    void InvalidateContext(PlayContextId playContextId);
}

/// <summary>
/// An in-memory revision tracker. Revisions do not persist across a host restart: a restarted
/// host holds no authoritative state until it resynchronizes with the adapter and starts a fresh
/// revision sequence for every affected state area.
/// </summary>
public sealed class RevisionTracker : IRevisionTracker
{
    /// <summary>Guards <see cref="revisionsByKey"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Each known state area's current revision, keyed by play context and area.</summary>
    private readonly Dictionary<(PlayContextId PlayContextId, StateAreaId AreaId), RevisionNumber> revisionsByKey = new();

    /// <inheritdoc/>
    public RevisionNumber Current(PlayContextId playContextId, StateAreaId areaId)
    {
        lock (gate)
        {
            return revisionsByKey.GetValueOrDefault((playContextId, areaId), RevisionNumber.Initial);
        }
    }

    /// <inheritdoc/>
    public RevisionNumber AdvanceOnChange(PlayContextId playContextId, StateAreaId areaId)
    {
        lock (gate)
        {
            RevisionNumber next = revisionsByKey.GetValueOrDefault((playContextId, areaId), RevisionNumber.Initial).Next();
            revisionsByKey[(playContextId, areaId)] = next;
            return next;
        }
    }

    /// <inheritdoc/>
    public void InvalidateContext(PlayContextId playContextId)
    {
        lock (gate)
        {
            foreach (var key in revisionsByKey.Keys.Where(key => key.PlayContextId.Equals(playContextId)).ToList())
            {
                revisionsByKey.Remove(key);
            }
        }
    }
}
