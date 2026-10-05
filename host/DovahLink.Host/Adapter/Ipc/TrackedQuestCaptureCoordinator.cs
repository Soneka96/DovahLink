using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Time;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Runs and receives one Host-owned multi-request tracked-quest capture at a time.</summary>
public interface ITrackedQuestCaptureCoordinator
{
    /// <summary>Runs complete tracked-quest Snapshot cycles until cancellation.</summary>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>Supplies one provenance-validated private page response to its pending capture.</summary>
    /// <param name="context">The raw page response after generic Adapter and play-context validation.</param>
    void AcceptPageCapture(LiveCaptureContext context);
}

/// <inheritdoc cref="ITrackedQuestCaptureCoordinator"/>
public sealed class TrackedQuestCaptureCoordinator : ITrackedQuestCaptureCoordinator
{
    /// <summary>The Host's owning loopback connection to the Adapter.</summary>
    private readonly Func<IAdapterIpcListener> listenerAccessor;

    /// <summary>Provides one coherent adapter identity, generation, and resynchronization view.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>Provides one coherent active play-context identity and transition generation.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Publishes the immutable complete quest collection or explicit unavailability.</summary>
    private readonly IStatePublisher<TrackedQuests?> publisher;

    /// <summary>Applies values through shared Host authority and resynchronization semantics.</summary>
    private readonly ILiveStateApplication liveStateApplication;

    /// <summary>Stamps each completed Snapshot with Host UTC time.</summary>
    private readonly IClock clock;

    /// <summary>Protects the one response currently awaited by the single collection loop.</summary>
    private readonly object gate = new();

    /// <summary>The active request completion, if one page is currently in flight.</summary>
    private PendingPage? pendingPage;

    /// <summary>The current Host collection generation, used to discard late replies.</summary>
    private long activeCaptureGeneration;

    /// <summary>The monotonically increasing local collection generation.</summary>
    private long nextCaptureGeneration;

    /// <summary>Bounds one lost page response using the existing Slow capture timeout.</summary>
    private static TimeSpan PageResponseTimeout => TimeSpan.FromTicks(
        Constants.LiveStateSlowSampleInterval.Ticks * Constants.LiveStateSampleTimeoutTicks);

    /// <summary>Creates the Host-owned quest-page assembler and publisher.</summary>
    /// <param name="listenerAccessor">Defers reading the Host's current Adapter connection until a collection runs, avoiding the connection factory's capture-handler dependency cycle.</param>
    /// <param name="adapterAvailabilityTracker">Provides adapter identity and resynchronization provenance.</param>
    /// <param name="playContextTracker">Provides the current play-context identity and transition generation.</param>
    /// <param name="publisher">Publishes the complete tracked-quest Snapshot.</param>
    /// <param name="liveStateApplication">Applies the Snapshot through shared authority rules.</param>
    /// <param name="clock">Stamps each completed Snapshot.</param>
    public TrackedQuestCaptureCoordinator(
        Func<IAdapterIpcListener> listenerAccessor,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker,
        IStatePublisher<TrackedQuests?> publisher,
        ILiveStateApplication liveStateApplication,
        IClock clock)
    {
        this.listenerAccessor = listenerAccessor;
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;
        this.publisher = publisher;
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
                    long captureGeneration = Interlocked.Increment(ref nextCaptureGeneration);
                    lock (gate)
                    {
                        activeCaptureGeneration = captureGeneration;
                    }

                    try
                    {
                        TrackedQuests? value = await CaptureCompleteSnapshotAsync(
                            current, captureGeneration, cancellationToken).ConfigureAwait(false);
                        if (IsAuthorityCurrent(current))
                        {
                            ApplySnapshot(current, value);
                        }
                    }
                    finally
                    {
                        lock (gate)
                        {
                            if (activeCaptureGeneration == captureGeneration)
                            {
                                activeCaptureGeneration = 0;
                                pendingPage = null;
                            }
                        }
                    }

                }

                await Task.Delay(Constants.LiveStateSlowSampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Background capture is part of the Host lifetime; normal shutdown ends this service cleanly.
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
                && pending.CaptureGeneration == activeCaptureGeneration
                && pending.CorrelationId == result.CorrelationId
                && pending.ConnectionGeneration == context.Source.ConnectionGeneration)
            {
                pending.Completion.TrySetResult(context);
            }
        }
    }

    /// <summary>Captures quest IDs, metadata, and all required objective pages as one complete value.</summary>
    /// <param name="authority">The connection and play context this collection is bound to.</param>
    /// <param name="captureGeneration">This collection's local generation.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>A complete collection, or <see langword="null"/> when it cannot be trusted as complete.</returns>
    private async Task<TrackedQuests?> CaptureCompleteSnapshotAsync(
        CaptureAuthority authority,
        long captureGeneration,
        CancellationToken cancellationToken)
    {
        uint[]? initialQuestIds = await ReadAllTrackedQuestIdsAsync(authority, captureGeneration, cancellationToken).ConfigureAwait(false);
        if (initialQuestIds is null)
        {
            return null;
        }

        List<TrackedQuest> quests = [];
        int totalObjectiveCount = 0;
        foreach (uint questId in initialQuestIds)
        {
            (TrackedQuest Quest, int ObjectiveRecordCount)? capture = await ReadTrackedQuestAsync(
                authority, captureGeneration, questId, totalObjectiveCount, cancellationToken).ConfigureAwait(false);
            if (capture is not { } questCapture)
            {
                return null;
            }

            totalObjectiveCount += questCapture.ObjectiveRecordCount;
            if (totalObjectiveCount > Constants.MaxTrackedQuestObjectives)
            {
                return null;
            }

            quests.Add(questCapture.Quest);
        }

        uint[]? finalQuestIds = await ReadAllTrackedQuestIdsAsync(authority, captureGeneration, cancellationToken).ConfigureAwait(false);
        if (finalQuestIds is null || !initialQuestIds.SequenceEqual(finalQuestIds))
        {
            return null;
        }

        var value = new TrackedQuests(quests);
        byte[] serializedState = JsonSerializer.SerializeToUtf8Bytes(new { value });
        return serializedState.Length <= Constants.MaxTrackedQuestsSerializedBytes ? value : null;
    }

    /// <summary>Reads every bounded tracked-ID page and returns a distinct, deterministic set.</summary>
    /// <param name="authority">The connection and play context this list read is bound to.</param>
    /// <param name="captureGeneration">This collection's local generation.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>Sorted unique runtime IDs, or <see langword="null"/> for any invalid or incomplete page.</returns>
    private async Task<uint[]?> ReadAllTrackedQuestIdsAsync(
        CaptureAuthority authority,
        long captureGeneration,
        CancellationToken cancellationToken)
    {
        HashSet<uint> questIds = [];
        ushort cursor = 0;
        while (true)
        {
            LiveCaptureContext? context = await RequestPageAsync(
                authority, captureGeneration, TrackedQuestPageKind.TrackedQuestIds, 0, cursor, cancellationToken)
                .ConfigureAwait(false);
            if (context is null
                || !IsCaptureForAuthority(context, authority)
                || !TryGetAvailablePayload(context.CaptureResult, out byte[] payload)
                || !TrackedQuestPageDecoder.TryDecodeQuestIds(payload, out uint[] pageIds, out bool hasMore))
            {
                return null;
            }

            foreach (uint questId in pageIds)
            {
                questIds.Add(questId);
            }

            if (questIds.Count > Constants.MaxTrackedQuests)
            {
                return null;
            }

            if (!hasMore)
            {
                return questIds.Order().ToArray();
            }

            if (pageIds.Length != Constants.TrackedQuestIdsPerPage
                || cursor + pageIds.Length >= Constants.MaxTrackedQuests)
            {
                return null;
            }

            cursor = (ushort)(cursor + pageIds.Length);
        }
    }

    /// <summary>Reads one quest's metadata and every current-instance objective page.</summary>
    /// <param name="authority">The connection and play context this quest read is bound to.</param>
    /// <param name="captureGeneration">This collection's local generation.</param>
    /// <param name="questId">The runtime quest FormID.</param>
    /// <param name="alreadyCollectedObjectives">The number of public objectives already assembled.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The complete tracked quest and raw record count, or <see langword="null"/> for invalid or incomplete data.</returns>
    private async Task<(TrackedQuest Quest, int ObjectiveRecordCount)?> ReadTrackedQuestAsync(
        CaptureAuthority authority,
        long captureGeneration,
        uint questId,
        int alreadyCollectedObjectives,
        CancellationToken cancellationToken)
    {
        (string Title, byte Type, uint CurrentInstanceId)? metadata = await ReadMetadataAsync(
            authority, captureGeneration, questId, cancellationToken).ConfigureAwait(false);
        if (metadata is not { } questMetadata)
        {
            return null;
        }

        Dictionary<(ushort Index, uint InstanceId), QuestObjective> currentObjectives = [];
        int objectiveRecordCount = 0;
        ushort cursor = 0;
        while (true)
        {
            LiveCaptureContext? context = await RequestPageAsync(
                authority, captureGeneration, TrackedQuestPageKind.Objectives, questId, cursor, cancellationToken)
                .ConfigureAwait(false);
            if (context is null
                || !IsCaptureForAuthority(context, authority)
                || !TryGetAvailablePayload(context.CaptureResult, out byte[] payload)
                || !TrackedQuestPageDecoder.TryDecodeObjectives(
                    payload, questId, cursor, out ushort nextCursor, out bool hasMore, out QuestObjective[] objectives))
            {
                return null;
            }

            objectiveRecordCount += objectives.Length;
            if (alreadyCollectedObjectives + objectiveRecordCount > Constants.MaxTrackedQuestObjectives)
            {
                return null;
            }

            foreach (QuestObjective objective in objectives)
            {
                if (objective.InstanceId != questMetadata.CurrentInstanceId)
                {
                    continue;
                }

                var identity = (objective.Index, objective.InstanceId);
                if (currentObjectives.TryGetValue(identity, out QuestObjective? existing))
                {
                    if (!existing.Equals(objective))
                    {
                        return null;
                    }
                }
                else
                {
                    currentObjectives.Add(identity, objective);
                }
            }

            cursor = nextCursor;
            if (!hasMore)
            {
                break;
            }
        }

        (string Title, byte Type, uint CurrentInstanceId)? finalMetadata = await ReadMetadataAsync(
            authority, captureGeneration, questId, cancellationToken).ConfigureAwait(false);
        if (finalMetadata is not { } confirmedMetadata || confirmedMetadata != questMetadata)
        {
            return null;
        }

        QuestObjective[] orderedObjectives = currentObjectives.Values
            .OrderBy(objective => objective.Index)
            .ThenBy(objective => objective.InstanceId)
            .ToArray();
        return (new TrackedQuest(questId, questMetadata.Title, questMetadata.Type, orderedObjectives), objectiveRecordCount);
    }

    /// <summary>Reads one tracked quest's localized title, raw type, and current instance ID.</summary>
    /// <param name="authority">The connection and play context this metadata read is bound to.</param>
    /// <param name="captureGeneration">This collection's local generation.</param>
    /// <param name="questId">The runtime quest FormID.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The complete metadata tuple, or <see langword="null"/> when unavailable or malformed.</returns>
    private async Task<(string Title, byte Type, uint CurrentInstanceId)?> ReadMetadataAsync(
        CaptureAuthority authority,
        long captureGeneration,
        uint questId,
        CancellationToken cancellationToken)
    {
        LiveCaptureContext? context = await RequestPageAsync(
            authority, captureGeneration, TrackedQuestPageKind.QuestMetadata, questId, 0, cancellationToken)
            .ConfigureAwait(false);
        if (context is null
            || !IsCaptureForAuthority(context, authority)
            || !TryGetAvailablePayload(context.CaptureResult, out byte[] payload)
            || !TrackedQuestPageDecoder.TryDecodeMetadata(
                payload, questId, out string? title, out byte type, out uint currentInstanceId)
            || title is null)
        {
            return null;
        }

        return (title, type, currentInstanceId);
    }

    /// <summary>Requests one page after registering its correlation before queue admission.</summary>
    /// <param name="authority">The connection and play context this request belongs to.</param>
    /// <param name="captureGeneration">The Host collection generation.</param>
    /// <param name="kind">The requested bounded page.</param>
    /// <param name="questId">The runtime quest FormID, or zero for ID pages.</param>
    /// <param name="cursor">The tracked-ID or objective offset.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The validated page result context, or <see langword="null"/> when the request fails.</returns>
    private async Task<LiveCaptureContext?> RequestPageAsync(
        CaptureAuthority authority,
        long captureGeneration,
        TrackedQuestPageKind kind,
        uint questId,
        ushort cursor,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorityCurrent(authority))
        {
            return null;
        }

        IAdapterIpcConnection? connection = listenerAccessor().CurrentConnection;
        if (connection is null || connection.ConnectionGeneration != authority.ConnectionGeneration)
        {
            return null;
        }

        IpcReadTrackedQuestPageMessage? request = connection.PrepareReadTrackedQuestPage(kind, questId, cursor);
        if (request is null)
        {
            return null;
        }

        var pending = new PendingPage(
            captureGeneration, authority.ConnectionGeneration, request.CorrelationId);
        lock (gate)
        {
            if (activeCaptureGeneration != captureGeneration || pendingPage is not null)
            {
                return null;
            }

            pendingPage = pending;
        }

        if (!connection.TrySendPreparedTrackedQuestPage(
                request, authority.ConnectionGeneration, out ulong sentCorrelationId)
            || sentCorrelationId != request.CorrelationId)
        {
            ClearPending(pending);
            return null;
        }

        try
        {
            return await pending.Completion.Task.WaitAsync(PageResponseTimeout, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Applies the complete value or explicit unavailability under the current Host authority.</summary>
    /// <param name="authority">The connection and play context that produced the complete capture attempt.</param>
    /// <param name="value">The complete collection or <see langword="null"/> for unavailable state.</param>
    private void ApplySnapshot(CaptureAuthority authority, TrackedQuests? value)
    {
        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        PlayContextSnapshot playContext = playContextTracker.GetSnapshot();
        if (!IsAuthorityCurrent(authority))
        {
            return;
        }

        liveStateApplication.Apply(
            publisher,
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

    /// <summary>Checks that an assembly still belongs to the live adapter and play-context generations.</summary>
    /// <param name="authority">The connection and play context captured at collection start.</param>
    /// <returns>Whether both source authorities still match exactly.</returns>
    private bool IsAuthorityCurrent(CaptureAuthority authority)
    {
        AdapterAvailabilitySnapshot adapter = adapterAvailabilityTracker.GetSnapshot();
        PlayContextSnapshot playContext = playContextTracker.GetSnapshot();
        IAdapterIpcConnection? connection = listenerAccessor().CurrentConnection;
        return adapter.Current == AdapterAvailability.Available
            && adapter.CurrentInstanceId == authority.InstanceId
            && adapter.ConnectionGeneration == authority.ConnectionGeneration
            && connection?.ConnectionGeneration == authority.ConnectionGeneration
            && playContext.Current == authority.PlayContextId
            && playContext.TransitionGeneration == authority.PlayContextGeneration;
    }

    /// <summary>Validates one page response against the collection's source and context tuple.</summary>
    /// <param name="context">The capture result validated by the generic live-capture sink.</param>
    /// <param name="authority">The connection and play context captured at collection start.</param>
    /// <returns>Whether this page belongs to the collection's exact source and context.</returns>
    private static bool IsCaptureForAuthority(LiveCaptureContext context, CaptureAuthority authority) =>
        context.Source.InstanceId == authority.InstanceId
        && context.Source.ConnectionGeneration == authority.ConnectionGeneration
        && context.PlayContextId == authority.PlayContextId
        && context.PlayContextGeneration == authority.PlayContextGeneration
        && context.CaptureResult.PlayContextId == authority.PlayContextId;

    /// <summary>Requires an available capture to carry a nonempty payload, or an unavailable capture to carry none.</summary>
    /// <param name="capture">The page response.</param>
    /// <param name="payload">The available payload bytes.</param>
    /// <returns>Whether the availability/payload relationship is valid.</returns>
    private static bool TryGetAvailablePayload(IpcCaptureResultMessage capture, out byte[] payload)
    {
        payload = [];
        if (capture.Availability != CaptureAvailability.Available
            || capture.Payload.Length == 0
            || capture.Payload.Length > Constants.MaxTrackedQuestCapturePageBytes)
        {
            return false;
        }

        payload = capture.Payload;
        return true;
    }

    /// <summary>Removes exactly this request's pending response slot.</summary>
    /// <param name="request">The pending request that completed or failed.</param>
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

    /// <summary>The source authority that must remain unchanged for one collection.</summary>
    private readonly record struct CaptureAuthority(
        AdapterInstanceId InstanceId,
        long ConnectionGeneration,
        PlayContextId PlayContextId,
        long PlayContextGeneration);

    /// <summary>One correlation registered before its request enters the outbound queue.</summary>
    private sealed class PendingPage
    {
        /// <summary>The Host collection generation owning this page request.</summary>
        public long CaptureGeneration { get; }

        /// <summary>The adapter connection generation owning the correlation.</summary>
        public long ConnectionGeneration { get; }

        /// <summary>The exact Adapter request correlation.</summary>
        public ulong CorrelationId { get; }

        /// <summary>Completes with the one matching provenance-validated page response.</summary>
        public TaskCompletionSource<LiveCaptureContext> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Creates one pending page request.</summary>
        /// <param name="captureGeneration">The Host collection generation.</param>
        /// <param name="connectionGeneration">The Adapter connection generation.</param>
        /// <param name="correlationId">The exact Adapter request correlation.</param>
        public PendingPage(long captureGeneration, long connectionGeneration, ulong correlationId)
        {
            CaptureGeneration = captureGeneration;
            ConnectionGeneration = connectionGeneration;
            CorrelationId = correlationId;
        }
    }
}
