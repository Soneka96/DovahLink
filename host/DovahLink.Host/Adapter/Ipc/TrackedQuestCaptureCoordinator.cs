using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Time;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Runs non-overlapping Slow-cadence capture cycles and publishes complete results.</summary>
public interface ITrackedQuestCaptureCoordinator
{
    /// <summary>Runs serialized complete tracked-quest Snapshot cycles until cancellation.</summary>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <remarks>Each attempt is followed by the full configured Slow interval (currently one second), including long captures; no catch-up cycle starts.</remarks>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="ITrackedQuestCaptureCoordinator"/>
public sealed class TrackedQuestCaptureCoordinator : ITrackedQuestCaptureCoordinator
{
    /// <summary>The Host's owning loopback connection to the Adapter.</summary>
    private readonly Func<IAdapterIpcListener> listenerAccessor;

    /// <summary>Builds one complete bounded value from private pages.</summary>
    private readonly ITrackedQuestSnapshotCollector snapshotCollector;

    /// <summary>Provides one coherent adapter identity, generation, and resynchronization view.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>Provides one coherent active play-context identity and transition generation.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Applies values through shared Host authority and resynchronization semantics.</summary>
    private readonly ILiveStateApplication liveStateApplication;

    /// <summary>Stamps each completed Snapshot with Host UTC time.</summary>
    private readonly IClock clock;

    /// <summary>Creates the Host-owned quest capture cycle.</summary>
    /// <param name="listenerAccessor">Defers reading the Host's current Adapter connection until a collection runs, avoiding the connection factory's capture-handler dependency cycle.</param>
    /// <param name="snapshotCollector">Collects one complete bounded tracked-quest value.</param>
    /// <param name="adapterAvailabilityTracker">Provides adapter identity and resynchronization provenance.</param>
    /// <param name="playContextTracker">Provides the current play-context identity and transition generation.</param>
    /// <param name="liveStateApplication">Applies the Snapshot through shared authority rules.</param>
    /// <param name="clock">Stamps each completed Snapshot.</param>
    public TrackedQuestCaptureCoordinator(
        Func<IAdapterIpcListener> listenerAccessor,
        ITrackedQuestSnapshotCollector snapshotCollector,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker,
        ILiveStateApplication liveStateApplication,
        IClock clock)
    {
        this.listenerAccessor = listenerAccessor;
        this.snapshotCollector = snapshotCollector;
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;
        this.liveStateApplication = liveStateApplication;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CaptureAuthority? authority = TryGetCurrentAuthority();
                if (authority is CaptureAuthority current)
                {
                    var source = new AdapterCaptureSource(current.InstanceId, current.ConnectionGeneration);
                    var playContext = new PlayContextSnapshot(current.PlayContextId, current.PlayContextGeneration);
                    TrackedQuests? value = await snapshotCollector.CollectAsync(
                        source, playContext, cancellationToken).ConfigureAwait(false);
                    ApplySnapshot(current, value);
                }

                await Task.Delay(Constants.LiveStateSlowSampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Background capture is part of the Host lifetime; normal shutdown ends this service cleanly.
        }
    }


    /// <summary>Applies the complete value or explicit unavailability under the current Host authority.</summary>
    /// <param name="authority">The connection and play context that produced the complete capture attempt.</param>
    /// <param name="value">The complete collection or <see langword="null"/> for unavailable state.</param>
    private void ApplySnapshot(CaptureAuthority authority, TrackedQuests? value)
    {
        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        PlayContextSnapshot playContext = playContextTracker.GetSnapshot();
        IAdapterIpcConnection? connection = listenerAccessor().CurrentConnection;
        if (adapterSnapshot.Current != AdapterAvailability.Available
            || adapterSnapshot.CurrentInstanceId != authority.InstanceId
            || adapterSnapshot.ConnectionGeneration != authority.ConnectionGeneration
            || connection?.ConnectionGeneration != authority.ConnectionGeneration
            || playContext.Current != authority.PlayContextId
            || playContext.TransitionGeneration != authority.PlayContextGeneration)
        {
            return;
        }

        liveStateApplication.Apply(
            UpdateMode.Snapshot,
            new StateAreaId(Constants.TrackedQuestsStateArea),
            value,
            adapterSnapshot.NeedsResynchronization,
            new AdapterCaptureSource(authority.InstanceId, authority.ConnectionGeneration),
            adapterSnapshot,
            authority.PlayContextId,
            playContext.TransitionGeneration,
            clock.UtcNow);
    }

    /// <summary>Tries to read a current adapter connection and active play context as one capture authority.</summary>
    /// <returns>The authority tuple, or <see langword="null"/> before a valid game capture context exists.</returns>
    private CaptureAuthority? TryGetCurrentAuthority()
    {
        AdapterAvailabilitySnapshot adapter = adapterAvailabilityTracker.GetSnapshot();
        PlayContextSnapshot playContext = playContextTracker.GetSnapshot();
        IAdapterIpcConnection? connection = listenerAccessor().CurrentConnection;
        if (adapter.Current != AdapterAvailability.Available
            || adapter.CurrentInstanceId is not AdapterInstanceId instanceId
            || playContext.Current is not PlayContextId playContextId
            || connection?.ConnectionGeneration != adapter.ConnectionGeneration)
        {
            return null;
        }

        return new CaptureAuthority(instanceId, adapter.ConnectionGeneration, playContextId, playContext.TransitionGeneration);
    }

    /// <summary>The source authority that must remain unchanged for one collection.</summary>
    /// <param name="InstanceId">The Adapter instance supplying the captured values.</param>
    /// <param name="ConnectionGeneration">The Adapter connection generation supplying them.</param>
    /// <param name="PlayContextId">The active game context at capture start.</param>
    /// <param name="PlayContextGeneration">The play-context transition generation at capture start.</param>
    private readonly record struct CaptureAuthority(
        AdapterInstanceId InstanceId,
        long ConnectionGeneration,
        PlayContextId PlayContextId,
        long PlayContextGeneration);

}
