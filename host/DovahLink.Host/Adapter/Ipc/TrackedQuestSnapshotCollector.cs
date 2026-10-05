using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Assembles complete tracked-quest state from bounded Adapter pages.</summary>
public interface ITrackedQuestSnapshotCollector
{
    /// <summary>Collects one complete, bounded tracked-quest snapshot.</summary>
    /// <param name="source">The Adapter instance and connection generation for this collection.</param>
    /// <param name="playContext">The active play context and transition generation for this collection.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The complete ordered collection, or <see langword="null"/> when any required page or bound fails.</returns>
    Task<TrackedQuests?> CollectAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="ITrackedQuestSnapshotCollector"/>
public sealed class TrackedQuestSnapshotCollector : ITrackedQuestSnapshotCollector
{
    /// <summary>Reads one private page at a time under the supplied capture authority.</summary>
    private readonly ITrackedQuestPageReader pageReader;

    /// <summary>Creates the complete-snapshot collector.</summary>
    /// <param name="pageReader">Reads bounded Adapter pages and validates their provenance.</param>
    public TrackedQuestSnapshotCollector(ITrackedQuestPageReader pageReader)
    {
        this.pageReader = pageReader;
    }

    /// <inheritdoc/>
    public async Task<TrackedQuests?> CollectAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        CancellationToken cancellationToken)
    {
        if (playContext.Current is null)
        {
            return null;
        }

        uint[]? initialQuestIds = await ReadAllTrackedQuestIdsAsync(
            source, playContext, cancellationToken).ConfigureAwait(false);
        if (initialQuestIds is null)
        {
            return null;
        }

        List<TrackedQuest> quests = [];
        int totalObjectiveCount = 0;
        foreach (uint questId in initialQuestIds)
        {
            (TrackedQuest Quest, int ObjectiveRecordCount)? capture = await ReadTrackedQuestAsync(
                source, playContext, questId, totalObjectiveCount, cancellationToken).ConfigureAwait(false);
            if (capture is not { } questCapture)
            {
                return null;
            }

            totalObjectiveCount += questCapture.ObjectiveRecordCount;
            quests.Add(questCapture.Quest);
        }

        uint[]? finalQuestIds = await ReadAllTrackedQuestIdsAsync(
            source, playContext, cancellationToken).ConfigureAwait(false);
        if (finalQuestIds is null || !initialQuestIds.SequenceEqual(finalQuestIds))
        {
            return null;
        }

        var value = new TrackedQuests(quests);
        // LiveStateApplication publishes the same { value } data shape with the same serializer defaults.
        // Current page, text, and count limits make this bound defensive, but it protects later contract changes.
        byte[] serializedState = JsonSerializer.SerializeToUtf8Bytes(new { value });
        return serializedState.Length <= Constants.MaxTrackedQuestsSerializedBytes ? value : null;
    }

    /// <summary>Reads all tracked-ID pages, collapses repeated FormIDs, and sorts the unique IDs.</summary>
    /// <param name="source">The Adapter source for the complete collection.</param>
    /// <param name="playContext">The play context for the complete collection.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The ordered unique IDs, or <see langword="null"/> when the pages are invalid or incomplete.</returns>
    private async Task<uint[]?> ReadAllTrackedQuestIdsAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        CancellationToken cancellationToken)
    {
        HashSet<uint> questIds = [];
        int rawQuestCount = 0;
        ushort cursor = 0;
        while (true)
        {
            LiveCaptureContext? context = await ReadPageAsync(
                source, playContext, TrackedQuestPageKind.TrackedQuestIds, 0, cursor, cancellationToken)
                .ConfigureAwait(false);
            if (context is null
                || !TryGetAvailablePayload(context.CaptureResult, out byte[] payload)
                || !TrackedQuestPageDecoder.TryDecodeQuestIds(payload, out uint[] pageIds, out bool hasMore))
            {
                return null;
            }

            rawQuestCount += pageIds.Length;
            foreach (uint questId in pageIds)
            {
                questIds.Add(questId);
            }

            if (rawQuestCount > Constants.MaxTrackedQuests
                || (hasMore && rawQuestCount >= Constants.MaxTrackedQuests))
            {
                return null;
            }

            if (!hasMore)
            {
                return questIds.Order().ToArray();
            }

            if (pageIds.Length != Constants.TrackedQuestIdsPerPage
                || (hasMore && cursor + pageIds.Length > ushort.MaxValue))
            {
                return null;
            }

            cursor = (ushort)(cursor + pageIds.Length);
        }
    }

    /// <summary>Reads one quest's metadata and raw objective pages, retaining its current instance.</summary>
    /// <param name="source">The Adapter source for the complete collection.</param>
    /// <param name="playContext">The play context for the complete collection.</param>
    /// <param name="questId">The runtime quest FormID.</param>
    /// <param name="alreadyCollectedObjectives">The number of raw objective records in earlier quests.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The complete quest and raw record count, or <see langword="null"/> on any invalid or incomplete page.</returns>
    private async Task<(TrackedQuest Quest, int ObjectiveRecordCount)?> ReadTrackedQuestAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        uint questId,
        int alreadyCollectedObjectives,
        CancellationToken cancellationToken)
    {
        (string Title, byte Type, uint CurrentInstanceId)? metadata = await ReadMetadataAsync(
            source, playContext, questId, cancellationToken).ConfigureAwait(false);
        if (metadata is not { } questMetadata)
        {
            return null;
        }

        Dictionary<(ushort Index, uint InstanceId), QuestObjective> currentObjectives = [];
        int objectiveRecordCount = 0;
        ushort cursor = 0;
        while (true)
        {
            LiveCaptureContext? context = await ReadPageAsync(
                source, playContext, TrackedQuestPageKind.Objectives, questId, cursor, cancellationToken)
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
            source, playContext, questId, cancellationToken).ConfigureAwait(false);
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
    /// <param name="source">The Adapter source for the complete collection.</param>
    /// <param name="playContext">The play context for the complete collection.</param>
    /// <param name="questId">The runtime quest FormID.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The metadata tuple, or <see langword="null"/> when the page is invalid or unavailable.</returns>
    private async Task<(string Title, byte Type, uint CurrentInstanceId)?> ReadMetadataAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        uint questId,
        CancellationToken cancellationToken)
    {
        LiveCaptureContext? context = await ReadPageAsync(
            source, playContext, TrackedQuestPageKind.QuestMetadata, questId, 0, cancellationToken)
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

    /// <summary>Reads one page under the collection's exact Adapter and play-context authority.</summary>
    /// <param name="source">The Adapter source for the complete collection.</param>
    /// <param name="playContext">The play context for the complete collection.</param>
    /// <param name="kind">The bounded page operation to request.</param>
    /// <param name="questId">The runtime quest FormID, or zero for tracked-ID pages.</param>
    /// <param name="cursor">The current tracked-ID or raw objective cursor.</param>
    /// <param name="cancellationToken">The Host lifetime cancellation token.</param>
    /// <returns>The matching capture context, or <see langword="null"/> when the page cannot be read.</returns>
    private Task<LiveCaptureContext?> ReadPageAsync(
        AdapterCaptureSource source,
        PlayContextSnapshot playContext,
        TrackedQuestPageKind kind,
        uint questId,
        ushort cursor,
        CancellationToken cancellationToken) =>
        pageReader.ReadPageAsync(source, playContext, kind, questId, cursor, cancellationToken);

    /// <summary>Requires an available capture to carry a nonempty payload within the private page bound.</summary>
    /// <param name="capture">The page response.</param>
    /// <param name="payload">The available payload bytes.</param>
    /// <returns>Whether the availability and payload size are valid.</returns>
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
}
