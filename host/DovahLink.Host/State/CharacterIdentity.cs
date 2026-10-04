using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>The complete player display name and identity-race display name.</summary>
/// <param name="Name">The player's display name.</param>
/// <param name="Race">The game-provided display name for the identity race.</param>
public sealed record CharacterIdentity(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("race")] string Race);
