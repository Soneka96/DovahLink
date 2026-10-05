using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Reads one provenance-validated private tracked-quest page from the Adapter.</summary>
public interface ITrackedQuestPageReader
{
    /// <summary>Requests one bounded page for the supplied capture authority.</summary>
    /// <param name="source">The Adapter instance and connection generation being captured.</param>
    /// <param name="playContext">The play context and transition generation being captured.</param>
    /// <param name="kind">The page operation to request.</param>
    /// <param name="questId">The nonzero runtime FormID, or zero for a tracked-ID page.</param>
    /// <param name="cursor">The page cursor represented by the private request.</param>
    /// <param name="cancellationToken">The lifetime token for the containing capture.</param>
    /// <returns>The matching validated capture context, or <see langword="null"/> when the request fails.</returns>
    Task<LiveCaptureContext?> ReadPageAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        TrackedQuestPageKind kind,
        uint questId,
        ushort cursor,
        CancellationToken cancellationToken);

    /// <summary>Completes a pending request when its correlation and provenance match.</summary>
    /// <param name="context">A capture result accepted by the generic live-capture sink.</param>
    void AcceptPageCapture(LiveCaptureContext context);
}

/// <inheritdoc cref="ITrackedQuestPageReader"/>
public sealed class TrackedQuestPageReader : ITrackedQuestPageReader
{
    /// <summary>Bounds one lost page response using the existing Slow capture timeout.</summary>
    private static TimeSpan PageResponseTimeout => TimeSpan.FromTicks(
        Constants.LiveStateSlowSampleInterval.Ticks * Constants.LiveStateSampleTimeoutTicks);

    /// <summary>Resolves the current listener without creating a capture-handler dependency cycle.</summary>
    private readonly Func<IAdapterIpcListener> listenerAccessor;

    /// <summary>Provides the current Adapter source authority.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>Provides the current play-context authority.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Protects the one page response currently awaited by the reader.</summary>
    private readonly object gate = new();

    /// <summary>The pending correlation, if a page request is in flight.</summary>
    private PendingPage? pendingPage;

    /// <summary>Creates the private page reader.</summary>
    /// <param name="listenerAccessor">Resolves the Host's current Adapter connection on each read.</param>
    /// <param name="adapterAvailabilityTracker">Provides the current Adapter instance and connection generation.</param>
    /// <param name="playContextTracker">Provides the current play context and transition generation.</param>
    public TrackedQuestPageReader(
        Func<IAdapterIpcListener> listenerAccessor,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker)
    {
        this.listenerAccessor = listenerAccessor;
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;
    }

    /// <inheritdoc/>
    public async Task<LiveCaptureContext?> ReadPageAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        TrackedQuestPageKind kind,
        uint questId,
        ushort cursor,
        CancellationToken cancellationToken)
    {
        if (playContext.Current is not PlayContextId playContextId)
        {
            return null;
        }

        AdapterAvailabilitySnapshot adapter = adapterAvailabilityTracker.GetSnapshot();
        PlayContextSnapshot currentPlayContext = playContextTracker.GetSnapshot();
        if (adapter.Current != AdapterAvailability.Available
            || adapter.CurrentInstanceId != source.InstanceId
            || adapter.ConnectionGeneration != source.ConnectionGeneration
            || currentPlayContext != playContext)
        {
            return null;
        }

        IAdapterIpcConnection? connection = listenerAccessor().CurrentConnection;
        if (connection is null || connection.ConnectionGeneration != source.ConnectionGeneration)
        {
            return null;
        }

        IpcReadTrackedQuestPageMessage? request = connection.PrepareReadTrackedQuestPage(kind, questId, cursor);
        if (request is null || request.CorrelationId == 0)
        {
            return null;
        }

        var pending = new PendingPage(source, playContextId, playContext.TransitionGeneration, request.CorrelationId);
        lock (gate)
        {
            if (pendingPage is not null)
            {
                return null;
            }

            pendingPage = pending;
        }

        if (!connection.TrySendPreparedTrackedQuestPage(
                request, source.ConnectionGeneration, out ulong sentCorrelationId)
            || sentCorrelationId != request.CorrelationId)
        {
            ClearPending(pending);
            return null;
        }

        try
        {
            return await pending.Completion.Task.WaitAsync(PageResponseTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            connection.TryCancel(request.CorrelationId);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            connection.TryCancel(request.CorrelationId);
            throw;
        }
        finally
        {
            ClearPending(pending);
        }
    }

    /// <inheritdoc/>
    public void AcceptPageCapture(LiveCaptureContext context)
    {
        IpcCaptureResultMessage result = context.CaptureResult;
        if (result.Source != CaptureSourceKind.Sample
            || result.CaptureKey != (uint)TrackedQuestCaptureKey.Page
            || result.CorrelationId == 0)
        {
            return;
        }

        lock (gate)
        {
            PendingPage? pending = pendingPage;
            if (pending is not null
                && pending.CorrelationId == result.CorrelationId
                && pending.Source == context.Source
                && pending.PlayContextId == context.PlayContextId
                && pending.PlayContextGeneration == context.PlayContextGeneration
                && pending.PlayContextId == result.PlayContextId)
            {
                pending.Completion.TrySetResult(context);
            }
        }
    }

    /// <summary>Clears only the request that owns the current pending slot.</summary>
    /// <param name="request">The completed, failed, or canceled request.</param>
    private void ClearPending(PendingPage request)
    {
        lock (gate)
        {
            if (ReferenceEquals(pendingPage, request))
            {
                pendingPage = null;
            }
        }
    }

    /// <summary>One correlation bound to the source and play context expected for its reply.</summary>
    private sealed class PendingPage
    {
        /// <summary>The expected Adapter instance and connection generation.</summary>
        public AdapterCaptureSource Source { get; }

        /// <summary>The expected play-context identity.</summary>
        public PlayContextId PlayContextId { get; }

        /// <summary>The expected play-context transition generation.</summary>
        public long PlayContextGeneration { get; }

        /// <summary>The exact Adapter request correlation.</summary>
        public ulong CorrelationId { get; }

        /// <summary>Completes with the matching validated page response.</summary>
        public TaskCompletionSource<LiveCaptureContext> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Creates one pending page request.</summary>
        /// <param name="source">The expected Adapter source.</param>
        /// <param name="playContextId">The expected play-context identity.</param>
        /// <param name="playContextGeneration">The expected transition generation.</param>
        /// <param name="correlationId">The exact request correlation.</param>
        public PendingPage(
            AdapterCaptureSource source,
            PlayContextId playContextId,
            long playContextGeneration,
            ulong correlationId)
        {
            Source = source;
            PlayContextId = playContextId;
            PlayContextGeneration = playContextGeneration;
            CorrelationId = correlationId;
        }
    }
}
