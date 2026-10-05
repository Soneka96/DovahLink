using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>The complete ordered collection of quests currently tracked by the player.</summary>
public sealed class TrackedQuests : IEquatable<TrackedQuests>
{
    /// <summary>The immutable, FormID-ordered tracked quests.</summary>
    [JsonPropertyName("quests")]
    public IReadOnlyList<TrackedQuest> Quests { get; }

    /// <summary>Copies the ordered tracked-quest values into an immutable view.</summary>
    /// <param name="quests">The complete quest collection; an empty collection is valid state.</param>
    public TrackedQuests(IReadOnlyList<TrackedQuest> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        Quests = Array.AsReadOnly(quests.ToArray());
    }

    /// <summary>Compares complete quest contents in their deterministic order.</summary>
    /// <param name="other">The other complete collection.</param>
    /// <returns>Whether both ordered quest collections have equal values.</returns>
    public bool Equals(TrackedQuests? other) => other is not null && Quests.SequenceEqual(other.Quests);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrackedQuests other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (TrackedQuest quest in Quests)
        {
            hash.Add(quest);
        }

        return hash.ToHashCode();
    }
}
