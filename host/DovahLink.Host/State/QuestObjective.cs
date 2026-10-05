using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>One current objective instance belonging to a tracked quest.</summary>
/// <param name="Index">The authored objective index.</param>
/// <param name="InstanceId">The engine quest-instance ID.</param>
/// <param name="Text">The localized authored display text, or <see langword="null"/> when absent.</param>
/// <param name="State">The actual Skyrim objective state.</param>
public sealed record QuestObjective(
    [property: JsonPropertyName("index")] ushort Index,
    [property: JsonPropertyName("instanceId")] uint InstanceId,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("state")] TrackedQuestObjectiveState State);
