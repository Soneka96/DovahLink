using System.Text.Json.Serialization;

namespace DovahLink.Host.State;

/// <summary>A normalized Skyrim calendar date and time, independent of any Gregorian calendar.</summary>
/// <param name="Year">The nonnegative game-year value supplied by Skyrim.</param>
/// <param name="Month">The public one-based Skyrim month, in the range 1–12.</param>
/// <param name="MonthName">The localized month name supplied by the running game.</param>
/// <param name="Day">The Skyrim calendar day.</param>
/// <param name="Hour">The whole game hour, in the range 0–23.</param>
/// <param name="Minute">The minute derived from the fractional game hour, in the range 0–59.</param>
public sealed record GameTime(
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("month")] int Month,
    [property: JsonPropertyName("monthName")] string MonthName,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("hour")] int Hour,
    [property: JsonPropertyName("minute")] int Minute);
