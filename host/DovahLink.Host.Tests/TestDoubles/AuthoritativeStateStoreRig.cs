using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// Composes a real <see cref="AuthoritativeStateStore"/> over controllable trackers so a store test can
/// drive capture, continuity loss, and resynchronization in a few calls. Starts with an available,
/// already-synchronized adapter and an established play context, with the supplied areas registered.
/// </summary>
public sealed class AuthoritativeStateStoreRig
{
    /// <summary>The claimed resynchronization authorization for the current gate, if any.</summary>
    private IAdapterResynchronizationToken? token;

    /// <summary>Composes the store with an available adapter and one play context, registering <paramref name="registeredAreas"/>.</summary>
    /// <param name="registeredAreas">The areas the store may commit.</param>
    public AuthoritativeStateStoreRig(params string[] registeredAreas)
    {
        PlayContextTracker = new FakePlayContextTracker();
        ContextId = PlayContextId.NewId();
        PlayContextTracker.NotifyTransition(ContextId);
        Adapter = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        Authority = new FakeStateAuthorityLifecycle();
        Registered = new RegisteredStateAreaPolicy();
        foreach (string area in registeredAreas)
        {
            Registered.TryRegister(new StateAreaId(area));
        }

        Store = new AuthoritativeStateStore(Adapter, PlayContextTracker, Registered, Authority);
    }

    /// <summary>The fixed capture time used unless a call supplies another.</summary>
    public static DateTimeOffset At { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The controllable play-context source.</summary>
    public FakePlayContextTracker PlayContextTracker { get; }

    /// <summary>The play context established at construction.</summary>
    public PlayContextId ContextId { get; }

    /// <summary>The controllable adapter availability tracker.</summary>
    public FakeAdapterAvailabilityTracker Adapter { get; }

    /// <summary>The controllable state-authority lifecycle.</summary>
    public FakeStateAuthorityLifecycle Authority { get; }

    /// <summary>The registered-area policy shared with the store.</summary>
    public RegisteredStateAreaPolicy Registered { get; }

    /// <summary>The store under test.</summary>
    public AuthoritativeStateStore Store { get; }

    /// <summary>Applies an ordinary Snapshot capture under the adapter's current identity.</summary>
    /// <typeparam name="TState">The value type.</typeparam>
    /// <param name="area">The destination area.</param>
    /// <param name="value">The captured value.</param>
    /// <param name="occurredAt">The capture time; <see cref="At"/> when omitted.</param>
    public StateApplyResult Apply<TState>(string area, TState value, DateTimeOffset? occurredAt = null) =>
        Store.Apply(
            Adapter.CurrentInstanceId!.Value, Adapter.CurrentConnectionGeneration, ContextId, PlayContextTracker.TransitionGeneration,
            occurredAt ?? At, new StateAreaId(area), value);

    /// <summary>Applies an Event under the adapter's current identity, supplying the gate's token only while resynchronization is required.</summary>
    /// <typeparam name="TState">The value type.</typeparam>
    /// <param name="area">The destination area.</param>
    /// <param name="value">The Event's resulting value.</param>
    /// <param name="occurredAt">The capture time; <see cref="At"/> when omitted.</param>
    public StateApplyResult ApplyEvent<TState>(string area, TState value, DateTimeOffset? occurredAt = null) =>
        Store.ApplyEvent(
            Adapter.CurrentInstanceId!.Value, Adapter.CurrentConnectionGeneration, ContextId, PlayContextTracker.TransitionGeneration,
            Adapter.NeedsResynchronization ? ClaimToken() : null, occurredAt ?? At, new StateAreaId(area), value);

    /// <summary>Applies a resynchronization baseline using the current gate's token.</summary>
    /// <typeparam name="TState">The value type.</typeparam>
    /// <param name="area">The destination area.</param>
    /// <param name="value">The baseline value.</param>
    /// <param name="mode">How a changed baseline is announced; Snapshot when omitted.</param>
    /// <param name="occurredAt">The capture time; <see cref="At"/> when omitted.</param>
    /// <param name="onCommitted">The commit hook to pass to the store, if any.</param>
    public StateApplyResult Baseline<TState>(string area, TState value, UpdateMode mode = UpdateMode.Snapshot, DateTimeOffset? occurredAt = null, Action? onCommitted = null) =>
        Store.ApplyResynchronizationBaseline(
            mode, ClaimToken(), ContextId, PlayContextTracker.TransitionGeneration, occurredAt ?? At, new StateAreaId(area), value, onCommitted);

    /// <summary>Drops the adapter connection, which is a continuity loss.</summary>
    public void LoseContinuity()
    {
        AdapterAvailabilityTransition? transition = Adapter.CommitDisconnected(Adapter.CurrentInstanceId!.Value, Adapter.CurrentConnectionGeneration);
        if (transition is not null)
        {
            Adapter.PublishTransition(transition);
        }
    }

    /// <summary>Reconnects the adapter as the next connection generation, which requires resynchronization.</summary>
    public void Reconnect()
    {
        token = null;
        AdapterAvailabilityTransition? transition = Adapter.CommitConnected(Adapter.CurrentInstanceId!.Value, Adapter.CurrentConnectionGeneration + 1);
        if (transition is not null)
        {
            Adapter.PublishTransition(transition);
        }
    }

    /// <summary>Completes the current resynchronization, as the coordinator does once every required baseline is accepted.</summary>
    public void CompleteResynchronization() =>
        Adapter.NotifyResynchronized(Adapter.CurrentInstanceId!.Value, Adapter.CurrentConnectionGeneration, ClaimToken());

    /// <summary>Reads an area's current Snapshot, failing the test if it is not replayable.</summary>
    /// <param name="area">The area to read.</param>
    public StateSnapshotPublication Snapshot(string area) =>
        Store.TryGetSnapshot(new StateAreaId(area), out StateSnapshotPublication? snapshot)
            ? snapshot
            : throw new InvalidOperationException($"Area '{area}' is not replayable.");

    /// <summary>Claims the current gate's resynchronization token once and reuses it for later calls.</summary>
    private IAdapterResynchronizationToken ClaimToken() =>
        token ??= Adapter.TryClaimResynchronizationToken()
            ?? throw new InvalidOperationException("No resynchronization token is available.");
}
