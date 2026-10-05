using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>One currently tracked Skyrim quest and its current objective instances.</summary>
public sealed class TrackedQuest : IEquatable<TrackedQuest>
{
    /// <summary>The active-runtime quest FormID.</summary>
    [JsonPropertyName("questId")]
    public uint QuestId { get; }

    /// <summary>The localized quest title supplied by the running game.</summary>
    [JsonPropertyName("title")]
    public string Title { get; }

    /// <summary>The raw Skyrim quest-type byte, retained without a presentation category.</summary>
    [JsonPropertyName("type")]
    public byte Type { get; }

    /// <summary>The current-instance objectives, ordered by index and instance ID.</summary>
    [JsonPropertyName("objectives")]
    public IReadOnlyList<QuestObjective> Objectives { get; }

    /// <summary>Creates one immutable tracked quest value.</summary>
    /// <param name="questId">The active-runtime quest FormID.</param>
    /// <param name="title">The localized quest title.</param>
    /// <param name="type">The raw Skyrim quest-type byte.</param>
    /// <param name="objectives">The complete current objective collection.</param>
    public TrackedQuest(uint questId, string title, byte type, IReadOnlyList<QuestObjective> objectives)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(objectives);
        QuestId = questId;
        Title = title;
        Type = type;
        Objectives = Array.AsReadOnly(objectives.ToArray());
    }

    /// <summary>Compares the complete quest and ordered objective values.</summary>
    /// <param name="other">The other quest.</param>
    /// <returns>Whether every value is equal.</returns>
    public bool Equals(TrackedQuest? other) => other is not null
        && QuestId == other.QuestId
        && Title == other.Title
        && Type == other.Type
        && Objectives.SequenceEqual(other.Objectives);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrackedQuest other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(QuestId);
        hash.Add(Title, StringComparer.Ordinal);
        hash.Add(Type);
        foreach (QuestObjective objective in Objectives)
        {
            hash.Add(objective);
        }

        return hash.ToHashCode();
    }
}
