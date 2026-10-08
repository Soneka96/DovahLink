using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Subscription;
using DovahLink.Host.Identity;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// Composes the Host's real live-state chain exactly as production wires it --
/// <see cref="AdapterAvailabilityTracker"/>, <see cref="StateAuthorityLifecycle"/>, the single
/// <see cref="AuthoritativeStateStore"/> and its <see cref="StatePublicationFeed"/> view,
/// <see cref="ResynchronizationTransactionCoordinator"/>, and <see cref="LiveStateApplication"/> -- so a
/// test drives capture application and then observes what a public client is actually told. Only the
/// play-context tracker, continuity recovery, and client transport are test doubles.
/// </summary>
public sealed class LivePipelineHarness
{
    /// <summary>The adapter connection generation last committed by <see cref="ConnectAdapter"/>.</summary>
    private long connectionGeneration;

    /// <summary>Composes the chain over <paramref name="catalog"/>, registering every one of its state areas.</summary>
    /// <param name="catalog">The catalog that defines required baseline areas and registered areas; <see cref="LiveStateCatalog.Default"/> when omitted.</param>
    public LivePipelineHarness(LiveStateCatalog? catalog = null)
    {
        Catalog = catalog ?? LiveStateCatalog.Default;
        AdapterTracker = new AdapterAvailabilityTracker();
        Authority = new StateAuthorityLifecycle(AdapterTracker);
        PlayContextTracker = new FakePlayContextTracker();
        PlayContext = PlayContextId.NewId();
        PlayContextTracker.NotifyTransition(PlayContext);

        Registered = new RegisteredStateAreaPolicy();
        foreach (StateAreaDefinition area in Catalog.StateAreas)
        {
            Registered.TryRegister(area.Id);
        }

        Store = new AuthoritativeStateStore(AdapterTracker, PlayContextTracker, Registered, Authority);
        Feed = new StatePublicationFeed(Store);
        Recovery = new FakeAdapterContinuityRecovery();
        Coordinator = new ResynchronizationTransactionCoordinator(Catalog, AdapterTracker, Recovery, TimeSpan.FromHours(1));
        Application = new LiveStateApplication(Coordinator, Recovery, Store);
        Clock = new FakeClock();
    }

    /// <summary>The state-area ids of the eight production areas, in catalog order.</summary>
    public static IReadOnlyList<string> ProductionAreas { get; } =
    [
        Constants.CharacterVitalsStateArea,
        Constants.CharacterXpStateArea,
        Constants.CharacterIdentityStateArea,
        Constants.CharacterSupernaturalTraitsStateArea,
        Constants.PlayerLocationStateArea,
        Constants.GameTimeStateArea,
        Constants.TrackedQuestsStateArea,
        Constants.CharacterLevelStateArea,
    ];

    /// <summary>The catalog this chain was composed from.</summary>
    public LiveStateCatalog Catalog { get; }

    /// <summary>The real adapter availability tracker.</summary>
    public AdapterAvailabilityTracker AdapterTracker { get; }

    /// <summary>The real state-authority lifecycle.</summary>
    public StateAuthorityLifecycle Authority { get; }

    /// <summary>The controllable play-context source.</summary>
    public FakePlayContextTracker PlayContextTracker { get; }

    /// <summary>The play context established when the harness was built.</summary>
    public PlayContextId PlayContext { get; }

    /// <summary>The registered-area policy shared by the feed and every subscription.</summary>
    public RegisteredStateAreaPolicy Registered { get; }

    /// <summary>The single owner of authoritative current state, revisions, and replay Snapshots.</summary>
    public AuthoritativeStateStore Store { get; }

    /// <summary>The read-only publication view a public client subscribes through.</summary>
    public StatePublicationFeed Feed { get; }

    /// <summary>Records recovery requests instead of closing connections.</summary>
    public FakeAdapterContinuityRecovery Recovery { get; }

    /// <summary>The real resynchronization transaction coordinator.</summary>
    public ResynchronizationTransactionCoordinator Coordinator { get; }

    /// <summary>The real shared authority-and-publication application service.</summary>
    public LiveStateApplication Application { get; }

    /// <summary>The clock supplying capture timestamps.</summary>
    public FakeClock Clock { get; }

    /// <summary>The exact adapter connection last committed by <see cref="ConnectAdapter"/>.</summary>
    public AdapterCaptureSource Source { get; private set; }

    /// <summary>Connects a new adapter instance as the next connection generation, requiring resynchronization.</summary>
    public void ConnectAdapter()
    {
        connectionGeneration++;
        var instance = AdapterInstanceId.NewId();
        AdapterAvailabilityTransition? transition = AdapterTracker.CommitConnected(instance, connectionGeneration);
        Source = new AdapterCaptureSource(instance, connectionGeneration);
        if (transition is not null)
        {
            AdapterTracker.PublishTransition(transition);
        }
    }

    /// <summary>Drops the current adapter connection, which is a continuity loss.</summary>
    public void LoseContinuity()
    {
        AdapterAvailabilityTransition? transition = AdapterTracker.CommitDisconnected(Source.InstanceId, Source.ConnectionGeneration);
        if (transition is not null)
        {
            AdapterTracker.PublishTransition(transition);
        }
    }

    /// <summary>Starts tracking the current connection's resynchronization transaction, as sending a resynchronize request does.</summary>
    public void BeginResynchronization() =>
        Coordinator.BeginTransaction(Source.InstanceId, Source.ConnectionGeneration, PlayContext, PlayContextTracker.TransitionGeneration);

    /// <summary>Reports the Adapter's own wire-level resynchronize plan as fully admitted.</summary>
    public void AcceptAdapterPlan() =>
        Coordinator.RecordAdapterPlanAccepted(true, Source.InstanceId, Source.ConnectionGeneration, PlayContext, PlayContextTracker.TransitionGeneration);

    /// <summary>Applies an identified resynchronization baseline Sample for one area.</summary>
    /// <typeparam name="TState">The captured value type.</typeparam>
    /// <param name="area">The area receiving the baseline.</param>
    /// <param name="value">The captured baseline value.</param>
    public void ApplyBaseline<TState>(string area, TState value) =>
        Apply(UpdateMode.Snapshot, area, value, isResynchronizationBaseline: true);

    /// <summary>Applies an ordinary periodic Snapshot Sample for one area.</summary>
    /// <typeparam name="TState">The captured value type.</typeparam>
    /// <param name="area">The area receiving the sample.</param>
    /// <param name="value">The captured value.</param>
    public void ApplySample<TState>(string area, TState value) =>
        Apply(UpdateMode.Snapshot, area, value, isResynchronizationBaseline: false);

    /// <summary>Applies a reliable Event for one area.</summary>
    /// <typeparam name="TState">The captured value type.</typeparam>
    /// <param name="area">The area receiving the Event.</param>
    /// <param name="value">The Event's resulting value.</param>
    public void ApplyEvent<TState>(string area, TState value) =>
        Apply(UpdateMode.Event, area, value, isResynchronizationBaseline: false);

    /// <summary>Applies one production area's baseline Sample from <paramref name="values"/>.</summary>
    /// <param name="area">One of <see cref="ProductionAreas"/>.</param>
    /// <param name="values">The complete value set to take the area's value from.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="area"/> is not a production area.</exception>
    public void ApplyProductionBaseline(string area, ProductionStateValues values)
    {
        switch (area)
        {
            case Constants.CharacterVitalsStateArea: ApplyBaseline(area, values.Vitals); break;
            case Constants.CharacterXpStateArea: ApplyBaseline(area, values.Xp); break;
            case Constants.CharacterIdentityStateArea: ApplyBaseline(area, values.Identity); break;
            case Constants.CharacterSupernaturalTraitsStateArea: ApplyBaseline(area, values.Traits); break;
            case Constants.PlayerLocationStateArea: ApplyBaseline(area, values.Location); break;
            case Constants.GameTimeStateArea: ApplyBaseline(area, values.GameTime); break;
            case Constants.TrackedQuestsStateArea: ApplyBaseline(area, values.Quests); break;
            case Constants.CharacterLevelStateArea: ApplyBaseline(area, values.Level); break;
            default: throw new ArgumentOutOfRangeException(nameof(area), area, "Not a production state area.");
        }
    }

    /// <summary>Applies every production baseline, ending with <paramref name="lastArea"/> when given.</summary>
    /// <param name="values">The complete value set.</param>
    /// <param name="lastArea">The area whose baseline must arrive last, or <see langword="null"/> for catalog order.</param>
    public void ApplyAllProductionBaselines(ProductionStateValues values, string? lastArea = null)
    {
        foreach (string area in ProductionAreas.Where(area => area != lastArea))
        {
            ApplyProductionBaseline(area, values);
        }

        if (lastArea is not null)
        {
            ApplyProductionBaseline(lastArea, values);
        }
    }

    /// <summary>Establishes initial current state through the real path: connect, begin, baseline every area, admit the plan.</summary>
    /// <param name="values">The complete value set to baseline.</param>
    public void EstablishInitialState(ProductionStateValues values)
    {
        ConnectAdapter();
        BeginResynchronization();
        ApplyAllProductionBaselines(values);
        AcceptAdapterPlan();
    }

    /// <summary>Applies a Level-changed Event for the production Level area.</summary>
    /// <param name="level">The new Level.</param>
    public void ApplyLevelChanged(ushort level) => ApplyEvent(Constants.CharacterLevelStateArea, (ushort?)level);

    /// <summary>Lists which of <paramref name="areas"/> the feed cannot currently replay.</summary>
    /// <param name="areas">The areas to check.</param>
    /// <returns>Every area whose <see cref="IStatePublicationFeed.TryGetSnapshot"/> returns <see langword="false"/>.</returns>
    public IReadOnlyList<string> UnreadableAreas(IEnumerable<string> areas) =>
        areas.Where(area => !Feed.TryGetSnapshot(new StateAreaId(area), out _)).ToArray();

    /// <summary>Reports whether the store currently considers a production area's typed value authoritative and readable.</summary>
    /// <param name="area">One of <see cref="ProductionAreas"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="area"/> is not a production area.</exception>
    public bool HasCurrentTypedValue(string area)
    {
        var id = new StateAreaId(area);
        return area switch
        {
            Constants.CharacterVitalsStateArea => Store.TryGetCurrentValue<CharacterVitals?>(id, out _),
            Constants.CharacterXpStateArea => Store.TryGetCurrentValue<float?>(id, out _),
            Constants.CharacterIdentityStateArea => Store.TryGetCurrentValue<CharacterIdentity?>(id, out _),
            Constants.CharacterSupernaturalTraitsStateArea => Store.TryGetCurrentValue<CharacterSupernaturalTraits?>(id, out _),
            Constants.PlayerLocationStateArea => Store.TryGetCurrentValue<PlayerLocation?>(id, out _),
            Constants.GameTimeStateArea => Store.TryGetCurrentValue<GameTime?>(id, out _),
            Constants.TrackedQuestsStateArea => Store.TryGetCurrentValue<TrackedQuests?>(id, out _),
            Constants.CharacterLevelStateArea => Store.TryGetCurrentValue<ushort?>(id, out _),
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Not a production state area."),
        };
    }

    /// <summary>Reads an area's current revision from the store.</summary>
    /// <param name="area">The area to read.</param>
    public RevisionNumber CurrentRevision(string area) => Store.CurrentRevision(new StateAreaId(area));

    /// <summary>Creates a fresh public client over the real feed, as a newly accepted connection would.</summary>
    /// <param name="pendingBaselineDeadline">How long a pending baseline waits before an error; effectively never when omitted.</param>
    public LivePipelineClient CreateClient(TimeSpan? pendingBaselineDeadline = null)
    {
        var codec = new PublicEnvelopeCodec(Authority);
        var subscription = new PublicStateSubscription(
            Registered, Feed, codec, PlayContextTracker, Authority, pendingBaselineDeadline ?? TimeSpan.FromSeconds(30));
        return new LivePipelineClient(subscription, new FakePublicConnectionContext(), codec);
    }

    /// <summary>Applies one capture through the shared application service with this chain's current provenance.</summary>
    /// <typeparam name="TState">The captured value type.</typeparam>
    /// <param name="mode">The area's update mode for this capture.</param>
    /// <param name="area">The destination area.</param>
    /// <param name="value">The captured value.</param>
    /// <param name="isResynchronizationBaseline">Whether the capture is an identified baseline.</param>
    private void Apply<TState>(UpdateMode mode, string area, TState value, bool isResynchronizationBaseline) =>
        Application.Apply(
            mode,
            new StateAreaId(area),
            value,
            isResynchronizationBaseline,
            Source,
            AdapterTracker.GetSnapshot(),
            PlayContext,
            PlayContextTracker.TransitionGeneration,
            Clock.UtcNow);
}
