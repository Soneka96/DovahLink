using DovahLink.Host.Identity;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter;

/// <summary>
/// Tracks one in-progress resynchronization transaction -- keyed by the exact
/// (<see cref="AdapterInstanceId"/>, connection generation, <see cref="PlayContextId"/>,
/// play-context generation) tuple it was requested under -- and completes it on
/// <see cref="IAdapterAvailabilityTracker"/> only once both halves have landed: every required
/// baseline area accepted (not merely attempted, per <see cref="StateApplyResult.Accepted"/>) and
/// the adapter's own wire-level plan admitted (every requested event registration succeeded and
/// every requested sample token was recognized). A strictly newer tuple always replaces an older
/// tracked transaction outright, discarding its partial progress; a call for an older tuple than the
/// one currently tracked is ignored rather than resurrecting stale progress. "Newer" is decided by
/// connection generation first, then play-context generation -- both monotonically increasing for
/// the host process's own lifetime, so this ordering alone is enough without this type separately
/// consulting the live trackers.
/// </summary>
public interface IResynchronizationTransactionCoordinator
{
    /// <summary>
    /// Returns the resynchronization token to apply one baseline area's capture under, claiming it
    /// once for the tracked transaction and retaining it for every later call under the same tuple.
    /// </summary>
    /// <param name="instanceId">The adapter instance the baseline was captured from.</param>
    /// <param name="connectionGeneration">The adapter connection generation the baseline was captured under.</param>
    /// <param name="playContextId">The play context that was current when the baseline was captured.</param>
    /// <param name="playContextGeneration">The play-context transition generation that was current when the baseline was captured.</param>
    /// <returns>The token to apply the baseline with, or <see langword="null"/> for a stale tuple or when no token could be claimed.</returns>
    IAdapterResynchronizationToken? AcquireToken(AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration);

    /// <summary>
    /// Records that <paramref name="areaId"/>'s baseline was accepted -- not merely attempted -- for
    /// the given tuple, completing the transaction if this was the last piece it needed.
    /// </summary>
    /// <param name="areaId">The state area whose baseline was accepted.</param>
    /// <param name="instanceId">The adapter instance the baseline was captured from.</param>
    /// <param name="connectionGeneration">The adapter connection generation the baseline was captured under.</param>
    /// <param name="playContextId">The play context that was current when the baseline was captured.</param>
    /// <param name="playContextGeneration">The play-context transition generation that was current when the baseline was captured.</param>
    void RecordAreaAccepted(StateAreaId areaId, AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration);

    /// <summary>
    /// Records the adapter's own wire-level admission result for the given tuple's resynchronize
    /// request, completing the transaction if this was the last piece it needed.
    /// </summary>
    /// <param name="accepted">
    /// Whether the adapter's resynchronize result reported every requested event registration
    /// succeeded and every requested sample token was recognized.
    /// </param>
    /// <param name="instanceId">The adapter instance the request was sent to.</param>
    /// <param name="connectionGeneration">The adapter connection generation the request was sent under.</param>
    /// <param name="playContextId">The play context that was current when the request was sent.</param>
    /// <param name="playContextGeneration">The play-context transition generation that was current when the request was sent.</param>
    void RecordAdapterPlanAccepted(bool accepted, AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration);
}

/// <inheritdoc cref="IResynchronizationTransactionCoordinator"/>
public sealed class ResynchronizationTransactionCoordinator : IResynchronizationTransactionCoordinator
{
    /// <summary>The tracker this coordinator claims tokens from and completes resynchronization through.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>Every state area a complete baseline transaction must accept, derived once from the catalog's BaselineSample units.</summary>
    private readonly IReadOnlySet<StateAreaId> requiredAreas;

    /// <summary>Guards every field below against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Whether a transaction is currently tracked at all.</summary>
    private bool hasTransaction;

    /// <summary>The tracked transaction's adapter instance.</summary>
    private AdapterInstanceId transactionInstanceId;

    /// <summary>The tracked transaction's connection generation.</summary>
    private long transactionConnectionGeneration;

    /// <summary>The tracked transaction's play context.</summary>
    private PlayContextId transactionPlayContextId;

    /// <summary>The tracked transaction's play-context generation.</summary>
    private long transactionPlayContextGeneration;

    /// <summary>The token claimed for the tracked transaction, if any yet.</summary>
    private IAdapterResynchronizationToken? transactionToken;

    /// <summary>The state areas accepted so far for the tracked transaction.</summary>
    private readonly HashSet<StateAreaId> transactionAcceptedAreas = [];

    /// <summary>The adapter's own wire-level plan-acceptance result for the tracked transaction, if reported yet.</summary>
    private bool? transactionAdapterPlanAccepted;

    /// <summary>Whether the tracked transaction has already been completed, so it is never re-notified.</summary>
    private bool transactionCompleted;

    /// <summary>Creates a coordinator whose required-area set is derived from <paramref name="catalog"/>.</summary>
    /// <param name="catalog">The catalog this coordinator derives its required baseline areas from.</param>
    /// <param name="adapterAvailabilityTracker">The tracker this coordinator claims tokens from and completes resynchronization through.</param>
    public ResynchronizationTransactionCoordinator(LiveStateCatalog catalog, IAdapterAvailabilityTracker adapterAvailabilityTracker)
    {
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        requiredAreas = catalog.CaptureUnits
            .Where(unit => unit.SynchronizationRole == SynchronizationRole.BaselineSample)
            .SelectMany(unit => unit.StateAreas)
            .ToHashSet();
    }

    /// <inheritdoc/>
    public IAdapterResynchronizationToken? AcquireToken(AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration)
    {
        lock (gate)
        {
            if (!EnsureTrackingLocked(instanceId, connectionGeneration, playContextId, playContextGeneration))
            {
                return null;
            }

            transactionToken ??= adapterAvailabilityTracker.TryClaimResynchronizationToken();
            return transactionToken;
        }
    }

    /// <inheritdoc/>
    public void RecordAreaAccepted(StateAreaId areaId, AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration)
    {
        bool shouldComplete;
        AdapterInstanceId completingInstanceId;
        long completingConnectionGeneration;
        IAdapterResynchronizationToken? completingToken;
        lock (gate)
        {
            if (!EnsureTrackingLocked(instanceId, connectionGeneration, playContextId, playContextGeneration))
            {
                return;
            }

            transactionAcceptedAreas.Add(areaId);
            shouldComplete = TryMarkCompletedLocked(out completingInstanceId, out completingConnectionGeneration, out completingToken);
        }

        if (shouldComplete && completingToken is not null)
        {
            adapterAvailabilityTracker.NotifyResynchronized(completingInstanceId, completingConnectionGeneration, completingToken);
        }
    }

    /// <inheritdoc/>
    public void RecordAdapterPlanAccepted(bool accepted, AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration)
    {
        bool shouldComplete;
        AdapterInstanceId completingInstanceId;
        long completingConnectionGeneration;
        IAdapterResynchronizationToken? completingToken;
        lock (gate)
        {
            if (!EnsureTrackingLocked(instanceId, connectionGeneration, playContextId, playContextGeneration))
            {
                return;
            }

            transactionAdapterPlanAccepted = accepted;
            shouldComplete = TryMarkCompletedLocked(out completingInstanceId, out completingConnectionGeneration, out completingToken);
        }

        if (shouldComplete && completingToken is not null)
        {
            adapterAvailabilityTracker.NotifyResynchronized(completingInstanceId, completingConnectionGeneration, completingToken);
        }
    }

    /// <summary>
    /// Matches the given tuple against the currently tracked transaction, replacing it with a fresh
    /// one when the tuple is strictly newer (by connection generation, then play-context generation),
    /// and rejecting a call for a tuple older than the one already tracked. Must be called with
    /// <see cref="gate"/> already held.
    /// </summary>
    /// <returns><see langword="true"/> when the caller's tuple is (now) the tracked transaction; <see langword="false"/> for a stale tuple.</returns>
    private bool EnsureTrackingLocked(AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration)
    {
        if (hasTransaction
            && transactionInstanceId == instanceId
            && transactionConnectionGeneration == connectionGeneration
            && transactionPlayContextId == playContextId
            && transactionPlayContextGeneration == playContextGeneration)
        {
            return true;
        }

        bool isNewer = !hasTransaction
            || connectionGeneration > transactionConnectionGeneration
            || (connectionGeneration == transactionConnectionGeneration && playContextGeneration > transactionPlayContextGeneration);
        if (!isNewer)
        {
            return false;
        }

        hasTransaction = true;
        transactionInstanceId = instanceId;
        transactionConnectionGeneration = connectionGeneration;
        transactionPlayContextId = playContextId;
        transactionPlayContextGeneration = playContextGeneration;
        transactionToken = null;
        transactionAcceptedAreas.Clear();
        transactionAdapterPlanAccepted = null;
        transactionCompleted = false;
        return true;
    }

    /// <summary>
    /// Checks whether the tracked transaction is now fully accepted -- every required area accepted
    /// and the adapter's own plan admitted -- and marks it completed exactly once if so, so a
    /// redundant later call can never re-fire completion. Must be called with <see cref="gate"/>
    /// already held.
    /// </summary>
    /// <param name="instanceId">The tracked transaction's adapter instance.</param>
    /// <param name="connectionGeneration">The tracked transaction's connection generation.</param>
    /// <param name="token">
    /// The token completion must be reported under, claimed here if <see cref="requiredAreas"/> is
    /// empty and no area ever claimed one through <see cref="AcquireToken"/>; <see langword="null"/>
    /// when this call does not complete the transaction, or when completion is otherwise satisfied
    /// but no token could be claimed.
    /// </param>
    /// <returns><see langword="true"/> exactly once, the call that completes the transaction.</returns>
    private bool TryMarkCompletedLocked(out AdapterInstanceId instanceId, out long connectionGeneration, out IAdapterResynchronizationToken? token)
    {
        instanceId = transactionInstanceId;
        connectionGeneration = transactionConnectionGeneration;
        token = null;
        if (transactionCompleted || transactionAdapterPlanAccepted != true || !requiredAreas.IsSubsetOf(transactionAcceptedAreas))
        {
            return false;
        }

        transactionToken ??= adapterAvailabilityTracker.TryClaimResynchronizationToken();
        token = transactionToken;
        if (token is null)
        {
            return false;
        }

        transactionCompleted = true;
        return true;
    }
}
