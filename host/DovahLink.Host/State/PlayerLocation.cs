using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>The player's current cell and its distinct location and worldspace facts.</summary>
/// <param name="CellId">The current cell's runtime FormID.</param>
/// <param name="CellKind">Whether the current cell is interior or exterior.</param>
/// <param name="CellName">The localized cell display name, when available.</param>
/// <param name="LocationId">The selected location's runtime FormID, when one exists.</param>
/// <param name="LocationName">The selected location's localized display name, when available.</param>
/// <param name="WorldspaceId">The current worldspace runtime FormID, when one exists.</param>
/// <param name="WorldspaceName">The current worldspace's localized display name, when available.</param>
public sealed record PlayerLocation(
    [property: JsonPropertyName("cellId")] uint CellId,
    [property: JsonPropertyName("cellKind")] PlayerLocationCellKind CellKind,
    [property: JsonPropertyName("cellName")] string? CellName,
    [property: JsonPropertyName("locationId")] uint? LocationId,
    [property: JsonPropertyName("locationName")] string? LocationName,
    [property: JsonPropertyName("worldspaceId")] uint? WorldspaceId,
    [property: JsonPropertyName("worldspaceName")] string? WorldspaceName);
