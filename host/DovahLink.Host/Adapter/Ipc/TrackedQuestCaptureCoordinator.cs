using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Time;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Runs one Host-owned multi-request tracked-quest capture at a time.</summary>
public interface ITrackedQuestCaptureCoordinator
{
    /// <summary>Runs complete tracked-quest Snapshot cycles until cancellation.</summary>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="ITrackedQuestCaptureCoordinator"/>
public sealed class TrackedQuestCaptureCoordinator : ITrackedQuestCaptureCoordinator
{
    /// <summary>The Host's owning loopback connection to the Adapter.</summary>
    private readonly Func<IAdapterIpcListener> listenerAccessor;

    /// <summary>Reads bounded private pages and owns their request correlations.</summary>
    private readonly ITrackedQuestPageReader pageReader;

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

    /// <summary>Creates the Host-owned quest capture cycle and publisher.</summary>
    /// <param name="listenerAccessor">Defers reading the Host's current Adapter connection until a collection runs, avoiding the connection factory's capture-handler dependency cycle.</param>
    /// <param name="pageReader">Reads each bounded Adapter page under the collection's captured authority.</param>
    /// <param name="adapterAvailabilityTracker">Provides adapter identity and resynchronization provenance.</param>
    /// <param name="playContextTracker">Provides the current play-context identity and transition generation.</param>
    /// <param name="publisher">Publishes the complete tracked-quest Snapshot.</param>
    /// <param name="liveStateApplication">Applies the Snapshot through shared authority rules.</param>
    /// <param name="clock">Stamps each completed Snapshot.</param>
    public TrackedQuestCaptureCoordinator(
        Func<IAdapterIpcListener> listenerAccessor,
        ITrackedQuestPageReader pageReader,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker,
        IStatePublisher<TrackedQuests?> publisher,
        ILiveStateApplication liveStateApplication,
        IClock clock)
    {
        this.listenerAccessor = listenerAccessor;
        this.pageReader = pageReader;
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
                    TrackedQuests? value = await CaptureCompleteSnapshotAsync(current, cancellationToken)
                        .ConfigureAwait(false);
                    if (IsAuthorityCurrent(current))
                    {
                        ApplySnapshot(current, value);
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

    /// <summary>Captures quest IDs, metadata, and all required objective pages as one complete value.</summary>
    /// <param name="authority">The connection and play context this collection is bound to.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>A complete collection, or <see langword="null"/> when it cannot be trusted as complete.</returns>
    private async Task<TrackedQuests?> CaptureCompleteSnapshotAsync(
        CaptureAuthority authority,
        CancellationToken cancellationToken)
    {
        uint[]? initialQuestIds = await ReadAllTrackedQuestIdsAsync(authority, cancellationToken).ConfigureAwait(false);
        if (initialQuestIds is null)
        {
            return null;
        }

        List<TrackedQuest> quests = [];
        int totalObjectiveCount = 0;
        foreach (uint questId in initialQuestIds)
        {
            (TrackedQuest Quest, int ObjectiveRecordCount)? capture = await ReadTrackedQuestAsync(
                authority, questId, totalObjectiveCount, cancellationToken).ConfigureAwait(false);
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

        uint[]? finalQuestIds = await ReadAllTrackedQuestIdsAsync(authority, cancellationToken).ConfigureAwait(false);
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
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>Sorted unique runtime IDs, or <see langword="null"/> for any invalid or incomplete page.</returns>
    private async Task<uint[]?> ReadAllTrackedQuestIdsAsync(
        CaptureAuthority authority,
        CancellationToken cancellationToken)
    {
        HashSet<uint> questIds = [];
        ushort cursor = 0;
        while (true)
        {
            LiveCaptureContext? context = await RequestPageAsync(
                authority, TrackedQuestPageKind.TrackedQuestIds, 0, cursor, cancellationToken)
                .ConfigureAwait(false);
            if (context is null
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

    /// <summary>Reads one quest's metadata and raw objective pages, retaining its current instance.</summary>
    /// <param name="authority">The connection and play context this quest read is bound to.</param>
    /// <param name="questId">The runtime quest FormID.</param>
    /// <param name="alreadyCollectedObjectives">The number of public objectives already assembled.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The complete tracked quest and raw record count, or <see langword="null"/> for invalid or incomplete data.</returns>
    private async Task<(TrackedQuest Quest, int ObjectiveRecordCount)?> ReadTrackedQuestAsync(
        CaptureAuthority authority,
        uint questId,
        int alreadyCollectedObjectives,
        CancellationToken cancellationToken)
    {
        (string Title, byte Type, uint CurrentInstanceId)? metadata = await ReadMetadataAsync(
            authority, questId, cancellationToken).ConfigureAwait(false);
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
                authority, TrackedQuestPageKind.Objectives, questId, cursor, cancellationToken)
                .ConfigureAwait(false);
            if (context is null
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
            authority, questId, cancellationToken).ConfigureAwait(false);
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
    /// <param name="questId">The runtime quest FormID.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The complete metadata tuple, or <see langword="null"/> when unavailable or malformed.</returns>
    private async Task<(string Title, byte Type, uint CurrentInstanceId)?> ReadMetadataAsync(
        CaptureAuthority authority,
        uint questId,
        CancellationToken cancellationToken)
    {
        LiveCaptureContext? context = await RequestPageAsync(
            authority, TrackedQuestPageKind.QuestMetadata, questId, 0, cancellationToken)
            .ConfigureAwait(false);
        if (context is null
            || !TryGetAvailablePayload(context.CaptureResult, out byte[] payload)
            || !TrackedQuestPageDecoder.TryDecodeMetadata(
                payload, questId, out string? title, out byte type, out uint currentInstanceId)
            || title is null)
        {
            return null;
        }

        return (title, type, currentInstanceId);
    }

    /// <summary>Reads one page only while the collection's original authority remains current.</summary>
    /// <param name="authority">The connection and play context this request belongs to.</param>
    /// <param name="kind">The requested bounded page.</param>
    /// <param name="questId">The runtime quest FormID, or zero for ID pages.</param>
    /// <param name="cursor">The tracked-ID or objective offset.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The matching page context, or <see langword="null"/> when authority or transport validation fails.</returns>
    private async Task<LiveCaptureContext?> RequestPageAsync(
        CaptureAuthority authority,
        TrackedQuestPageKind kind,
        uint questId,
        ushort cursor,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorityCurrent(authority))
        {
            return null;
        }

        var source = new AdapterCaptureSource(authority.InstanceId, authority.ConnectionGeneration);
        var playContext = new PlayContextSnapshot(authority.PlayContextId, authority.PlayContextGeneration);
        return await pageReader.ReadPageAsync(
            source, playContext, kind, questId, cursor, cancellationToken).ConfigureAwait(false);
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
