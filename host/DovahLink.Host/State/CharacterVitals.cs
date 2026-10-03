using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>The coherent Health, Magicka, and Stamina values from one Vitals capture.</summary>
/// <param name="Health">Health current and effective maximum.</param>
/// <param name="Magicka">Magicka current and effective maximum.</param>
/// <param name="Stamina">Stamina current and effective maximum.</param>
public sealed record CharacterVitals(
    [property: JsonPropertyName("health")] CharacterVital Health,
    [property: JsonPropertyName("magicka")] CharacterVital Magicka,
    [property: JsonPropertyName("stamina")] CharacterVital Stamina);
