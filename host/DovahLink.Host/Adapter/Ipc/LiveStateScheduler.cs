using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Drives the host's own sampling cadence for every <see cref="RateClass"/>-classed
/// <see cref="CaptureUnitDefinition"/> in <see cref="LiveStateCatalog"/>: one independent, sequential
/// per-unit loop that waits its unit's interval, then sends a read-sample request on the adapter
/// listener's currently active connection -- but only when that unit has no outstanding request
/// already awaiting a reply. A capture unit whose <see cref="CaptureUnitDefinition.RateClass"/> is
/// <see langword="null"/> is never polled here -- it is either event-sourced or a
/// resynchronization-only baseline sample, both handled entirely by the adapter's own
/// resynchronization sequence, not by this scheduler.
/// </summary>
public interface ILiveStateScheduler
{
    /// <summary>
    /// Runs one sampling loop per rate-classed capture unit in <see cref="LiveStateCatalog"/> until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop every loop.</param>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="ILiveStateScheduler"/>
/// <remarks>
/// Each capture unit's own outstanding slot bounds it to at most one in-flight request at a time: a
/// tick while a slot is outstanding is skipped rather than sending a second overlapping request. A
/// slot releases either when <see cref="ILiveCaptureSink.CaptureResultApplied"/> reports a matching
/// reply -- subscribed to for the host process's own lifetime at construction, matching
/// <see cref="PlayContextResynchronizationTrigger"/>'s identical subscription discipline for the same
/// kind of event, and deliberately not a direct dependency this scheduler receives calls through,
/// which would create a composition-root cycle back through the connection factory that builds every
/// session this scheduler itself sends through -- or after
/// <see cref="Constants.LiveStateSampleTimeoutTicks"/> consecutive skipped ticks with no reply,
/// best-effort cancelling the stale correlation first, so a dropped result, a failed game-thread
/// dispatch, or a disconnect can never wedge a unit forever. A timeout releases the slot for exactly
/// the next tick to use, never a burst of catch-up sends. No connection, or a rejected send (for
/// example a full outbound queue), never marks a slot outstanding at all, so the next tick tries
/// again immediately against whatever the adapter listener's current state is then. The first send
/// for a unit happens only after its first interval elapses -- an immediate authoritative baseline is
/// already established by the adapter's own resynchronization sequence, so this scheduler does not
/// need to rush a first sample.
/// </remarks>
public sealed class LiveStateScheduler : ILiveStateScheduler
{
    /// <summary>Whichever adapter connection is currently active is where every sample send goes.</summary>
    private readonly IAdapterIpcListener listener;

    /// <summary>The catalog naming every capture unit and its rate class.</summary>
    private readonly LiveStateCatalog catalog;

    /// <summary>Maps each <see cref="RateClass"/> to how often that class is sampled.</summary>
    private readonly IReadOnlyDictionary<RateClass, TimeSpan> intervals;

    /// <summary>Every rate-classed capture unit's own outstanding-request slot, keyed by its sample token.</summary>
    private readonly Dictionary<uint, OutstandingSlot> slotsBySampleToken;

    /// <summary>Creates a scheduler using the production Fast/Medium intervals from <see cref="Constants"/>.</summary>
    /// <param name="listener">Whichever adapter connection is currently active is where every sample send goes.</param>
    /// <param name="catalog">The catalog naming every capture unit and its rate class.</param>
    /// <param name="liveCaptureSink">Raises <see cref="ILiveCaptureSink.CaptureResultApplied"/> for every arriving capture result, releasing this scheduler's own outstanding-request slots.</param>
    public LiveStateScheduler(IAdapterIpcListener listener, LiveStateCatalog catalog, ILiveCaptureSink liveCaptureSink)
        : this(listener, catalog, liveCaptureSink, ProductionIntervals)
    {
    }

    /// <summary>Creates a scheduler over an explicit interval map. Exposed for tests that need faster-than-production cadences.</summary>
    /// <param name="listener">Whichever adapter connection is currently active is where every sample send goes.</param>
    /// <param name="catalog">The catalog naming every capture unit and its rate class.</param>
    /// <param name="liveCaptureSink">Raises <see cref="ILiveCaptureSink.CaptureResultApplied"/> for every arriving capture result, releasing this scheduler's own outstanding-request slots.</param>
    /// <param name="intervals">Maps each <see cref="RateClass"/> that appears in <paramref name="catalog"/> to how often that class is sampled.</param>
    internal LiveStateScheduler(IAdapterIpcListener listener, LiveStateCatalog catalog, ILiveCaptureSink liveCaptureSink, IReadOnlyDictionary<RateClass, TimeSpan> intervals)
    {
        this.listener = listener;
        this.catalog = catalog;
        this.intervals = intervals;
        slotsBySampleToken = catalog.CaptureUnits
            .Where(unit => unit.RateClass is not null)
            .ToDictionary(unit => unit.CaptureKey, _ => new OutstandingSlot());

        liveCaptureSink.CaptureResultApplied += HandleCaptureResultApplied;
    }

    /// <summary>The production Fast/Medium sampling intervals, per <see cref="Constants"/>.</summary>
    private static IReadOnlyDictionary<RateClass, TimeSpan> ProductionIntervals { get; } = new Dictionary<RateClass, TimeSpan>
    {
        [RateClass.Fast] = Constants.LiveStateFastSampleInterval,
        [RateClass.Medium] = Constants.LiveStateMediumSampleInterval,
    };

    /// <inheritdoc/>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        List<Task> loops = [];
        foreach (CaptureUnitDefinition unit in catalog.CaptureUnits)
        {
            if (unit.RateClass is RateClass rateClass)
            {
                loops.Add(RunSampleLoopAsync(unit.CaptureKey, intervals[rateClass], slotsBySampleToken[unit.CaptureKey], cancellationToken));
            }
        }

        return Task.WhenAll(loops);
    }

    /// <summary>
    /// Releases the outstanding slot matching an arriving capture result's own sample token,
    /// correlation id, and connection generation, if any. A result from an older connection
    /// generation or a stale/foreign correlation id never releases the current slot. An Event-sourced
    /// result, or one for a token this scheduler is not tracking (for example a
    /// resynchronization-only baseline sample), is a harmless no-op -- an Event's own key shares the
    /// same raw <see langword="uint"/> namespace as a Sample's own token (see
    /// <see cref="CharacterSampleToken"/>/<see cref="CharacterEventKey"/>), so this scheduler must
    /// never treat an Event result as a reply to a Sample request it never sent.
    /// </summary>
    /// <param name="captureResult">The capture result that was just applied.</param>
    /// <param name="connectionGeneration">The connection generation the result arrived under.</param>
    private void HandleCaptureResultApplied(IpcCaptureResultMessage captureResult, long connectionGeneration)
    {
        if (captureResult.Source != CaptureSourceKind.Sample || !slotsBySampleToken.TryGetValue(captureResult.CaptureKey, out OutstandingSlot? slot))
        {
            return;
        }

        lock (slot.Gate)
        {
            if (slot.Outstanding && slot.CorrelationId == captureResult.CorrelationId && slot.ConnectionGeneration == connectionGeneration)
            {
                slot.Outstanding = false;
            }
        }
    }

    /// <summary>
    /// Waits <paramref name="interval"/>, then either skips (a slot already outstanding and still
    /// within its timeout budget), times the outstanding slot out and immediately retries, or sends a
    /// fresh read-sample request -- repeating until cancelled.
    /// </summary>
    /// <param name="sampleToken">The capture unit's sample token.</param>
    /// <param name="interval">How long to wait between ticks.</param>
    /// <param name="slot">This unit's own outstanding-request slot.</param>
    /// <param name="cancellationToken">The token used to stop this loop.</param>
    private async Task RunSampleLoopAsync(uint sampleToken, TimeSpan interval, OutstandingSlot slot, CancellationToken cancellationToken)
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

            ulong? timedOutCorrelationId = null;
            lock (slot.Gate)
            {
                if (slot.Outstanding)
                {
                    if (++slot.TicksOutstanding < Constants.LiveStateSampleTimeoutTicks)
                    {
                        continue;
                    }

                    timedOutCorrelationId = slot.CorrelationId;
                    slot.Outstanding = false;
                }
            }

            if (timedOutCorrelationId is ulong staleCorrelationId)
            {
                listener.CurrentConnection?.TryCancel(staleCorrelationId);
            }

            IAdapterIpcConnection? connection = listener.CurrentConnection;
            if (connection is null || !connection.TrySendReadSample(sampleToken, out ulong correlationId) || connection.ConnectionGeneration is not long generation)
            {
                continue;
            }

            lock (slot.Gate)
            {
                slot.Outstanding = true;
                slot.CorrelationId = correlationId;
                slot.ConnectionGeneration = generation;
                slot.TicksOutstanding = 0;
            }
        }
    }

    /// <summary>
    /// One rate-classed capture unit's outstanding-request bookkeeping, shared between its own
    /// sampling loop and <see cref="HandleCaptureResultApplied"/>.
    /// </summary>
    private sealed class OutstandingSlot
    {
        /// <summary>Guards every other field, since the sampling loop and a capture-result notification can run concurrently.</summary>
        public readonly object Gate = new();

        /// <summary>Whether a request is currently outstanding for this unit.</summary>
        public bool Outstanding;

        /// <summary>The outstanding request's correlation id, meaningful only while <see cref="Outstanding"/> is <see langword="true"/>.</summary>
        public ulong CorrelationId;

        /// <summary>The connection generation the outstanding request was sent on, meaningful only while <see cref="Outstanding"/> is <see langword="true"/>.</summary>
        public long ConnectionGeneration;

        /// <summary>How many consecutive ticks the current request has been outstanding, reset whenever a fresh request is sent.</summary>
        public int TicksOutstanding;
    }
}
