using System.Text.Json;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests the typed Skyrim calendar value and its complete public JSON shape.</summary>
public class GameTimeTests
{
    /// <summary>Verifies that game time serializes as calendar fields, not an OS date or clock.</summary>
    [Fact]
    public void Serialize_UsesCanonicalCalendarFields()
    {
        var value = new GameTime(201, 9, "Hearthfire", 17, 17, 45);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        JsonElement json = document.RootElement;

        Assert.Equal(6, json.EnumerateObject().Count());
        Assert.Equal(201, json.GetProperty("year").GetInt32());
        Assert.Equal(9, json.GetProperty("month").GetInt32());
        Assert.Equal("Hearthfire", json.GetProperty("monthName").GetString());
        Assert.Equal(17, json.GetProperty("day").GetInt32());
        Assert.Equal(17, json.GetProperty("hour").GetInt32());
        Assert.Equal(45, json.GetProperty("minute").GetInt32());
    }

    /// <summary>Verifies that a localized month name survives JSON round-tripping unchanged.</summary>
    [Fact]
    public void JsonRoundTrip_PreservesLocalizedMonthName()
    {
        var expected = new GameTime(201, 4, "Sønens Dag", 2, 23, 59);

        string encoded = JsonSerializer.Serialize(expected);
        GameTime? decoded = JsonSerializer.Deserialize<GameTime>(encoded);

        Assert.Equal(expected, decoded);
    }
}
