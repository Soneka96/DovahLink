using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>A resource's current and effective maximum value from one Vitals capture.</summary>
/// <param name="Current">The current resource value.</param>
/// <param name="Max">The effective maximum resource value.</param>
public sealed record CharacterVital(
    [property: JsonPropertyName("current")] float Current,
    [property: JsonPropertyName("max")] float Max);
