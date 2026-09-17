using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Drives the host's own sampling cadence for every <see cref="RateClass"/>-classed
/// <see cref="CaptureUnitDefinition"/> in <see cref="LiveStateCatalog"/>: one independent, sequential
/// per-unit loop that waits its unit's interval, then calls
/// <see cref="IAdapterIpcConnection.TrySendReadSample"/> on the adapter listener's currently active
/// connection. A capture unit whose <see cref="CaptureUnitDefinition.RateClass"/> is
/// <see langword="null"/> is never polled here -- it is either event-sourced or a
/// resynchronization-only baseline sample, both handled entirely by the adapter's own
/// resynchronization sequence, not by this scheduler.
/// </summary>
/// <remarks>
/// Each loop is a single sequential wait-then-send cycle, so there is never more than one send
/// attempt in flight per capture unit by construction -- no separate in-flight tracking is needed.
/// No connection, or a rejected send (for example a full outbound queue), silently skips that tick
/// rather than retrying or backlogging; the next tick tries again against whatever the adapter
/// listener's current state is then. The first send for a unit happens only after its first interval
/// elapses -- an immediate authoritative baseline is already established by the adapter's own
/// resynchronization sequence, so this scheduler does not need to rush a first sample.
/// </remarks>
public sealed class LiveStateScheduler
{
    /// <summary>Whichever adapter connection is currently active is where every sample send goes.</summary>
    private readonly IAdapterIpcListener listener;

    /// <summary>The catalog naming every capture unit and its rate class.</summary>
    private readonly LiveStateCatalog catalog;

    /// <summary>Maps each <see cref="RateClass"/> to how often that class is sampled.</summary>
    private readonly IReadOnlyDictionary<RateClass, TimeSpan> intervals;

    /// <summary>Creates a scheduler using the production Fast/Medium intervals from <see cref="Constants"/>.</summary>
    /// <param name="listener">Whichever adapter connection is currently active is where every sample send goes.</param>
    /// <param name="catalog">The catalog naming every capture unit and its rate class.</param>
    public LiveStateScheduler(IAdapterIpcListener listener, LiveStateCatalog catalog)
        : this(listener, catalog, ProductionIntervals)
    {
    }

    /// <summary>Creates a scheduler over an explicit interval map. Exposed for tests that need faster-than-production cadences.</summary>
    /// <param name="listener">Whichever adapter connection is currently active is where every sample send goes.</param>
    /// <param name="catalog">The catalog naming every capture unit and its rate class.</param>
    /// <param name="intervals">Maps each <see cref="RateClass"/> that appears in <paramref name="catalog"/> to how often that class is sampled.</param>
    internal LiveStateScheduler(IAdapterIpcListener listener, LiveStateCatalog catalog, IReadOnlyDictionary<RateClass, TimeSpan> intervals)
    {
        this.listener = listener;
        this.catalog = catalog;
        this.intervals = intervals;
    }

    /// <summary>The production Fast/Medium sampling intervals, per <see cref="Constants"/>.</summary>
    private static IReadOnlyDictionary<RateClass, TimeSpan> ProductionIntervals { get; } = new Dictionary<RateClass, TimeSpan>
    {
        [RateClass.Fast] = Constants.LiveStateFastSampleInterval,
        [RateClass.Medium] = Constants.LiveStateMediumSampleInterval,
    };

    /// <summary>
    /// Runs one sampling loop per rate-classed capture unit in <see cref="LiveStateCatalog"/> until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop every loop.</param>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        List<Task> loops = [];
        foreach (CaptureUnitDefinition unit in catalog.CaptureUnits)
        {
            if (unit.RateClass is RateClass rateClass)
            {
                loops.Add(RunSampleLoopAsync(unit.CaptureKey, intervals[rateClass], cancellationToken));
            }
        }

        return Task.WhenAll(loops);
    }

    /// <summary>Waits <paramref name="interval"/>, sends a read-sample request, and repeats until cancelled.</summary>
    /// <param name="sampleToken">The capture unit's sample token.</param>
    /// <param name="interval">How long to wait between send attempts.</param>
    /// <param name="cancellationToken">The token used to stop this loop.</param>
    private async Task RunSampleLoopAsync(uint sampleToken, TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            listener.CurrentConnection?.TrySendReadSample(sampleToken, out _);
        }
    }
}
