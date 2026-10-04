using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>The independent vampire and transformation-capability observations.</summary>
/// <param name="IsVampire">Whether the player's vampire global is nonzero.</param>
/// <param name="HasVampireLordForm">Whether the player possesses the Vampire Lord spell.</param>
/// <param name="HasWerewolfForm">Whether the player possesses the Beast Form spell.</param>
public sealed record CharacterSupernaturalTraits(
    [property: JsonPropertyName("isVampire")] bool IsVampire,
    [property: JsonPropertyName("hasVampireLordForm")] bool HasVampireLordForm,
    [property: JsonPropertyName("hasWerewolfForm")] bool HasWerewolfForm);
