using System.Text.Json;
using System.Text.Json.Serialization;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests the complete typed and JSON representation of player location.</summary>
public class PlayerLocationTests
{
    /// <summary>Uses the Host's public Protocol naming and string-enum policy.</summary>
    private static readonly JsonSerializerOptions ProtocolOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Verifies that every distinct cell, location, and worldspace field keeps its canonical JSON name.</summary>
    [Fact]
    public void Serialize_UsesCanonicalFieldsAndCellKind()
    {
        var location = new PlayerLocation(
            123456,
            PlayerLocationCellKind.Exterior,
            "WhiterunWorld",
            98765,
            "Whiterun",
            1,
            "Skyrim");

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(location, ProtocolOptions));
        JsonElement json = document.RootElement;

        Assert.Equal(7, json.EnumerateObject().Count());
        Assert.Equal(123456u, json.GetProperty("cellId").GetUInt32());
        Assert.Equal("exterior", json.GetProperty("cellKind").GetString());
        Assert.Equal("WhiterunWorld", json.GetProperty("cellName").GetString());
        Assert.Equal(98765u, json.GetProperty("locationId").GetUInt32());
        Assert.Equal("Whiterun", json.GetProperty("locationName").GetString());
        Assert.Equal(1u, json.GetProperty("worldspaceId").GetUInt32());
        Assert.Equal("Skyrim", json.GetProperty("worldspaceName").GetString());
    }

    /// <summary>Verifies that valid cell context serializes absent names and locations as explicit nulls.</summary>
    [Fact]
    public void Serialize_PreservesLegitimateOptionalAbsence()
    {
        var location = new PlayerLocation(1, PlayerLocationCellKind.Interior, null, null, null, null, null);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(location, ProtocolOptions));
        JsonElement json = document.RootElement;

        Assert.Equal(1u, json.GetProperty("cellId").GetUInt32());
        Assert.Equal("interior", json.GetProperty("cellKind").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("cellName").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("locationId").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("locationName").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("worldspaceId").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("worldspaceName").ValueKind);
    }

    /// <summary>Verifies that runtime FormIDs and localized names survive typed JSON round-tripping.</summary>
    [Fact]
    public void JsonRoundTrip_PreservesFormIdsAndLocalizedNames()
    {
        var expected = new PlayerLocation(
            0xFE001234,
            PlayerLocationCellKind.Interior,
            "La Grotte de l’ours",
            0xFE006789,
            "Pointe de l’aigle",
            null,
            null);

        string encoded = JsonSerializer.Serialize(expected, ProtocolOptions);
        PlayerLocation? decoded = JsonSerializer.Deserialize<PlayerLocation>(encoded, ProtocolOptions);

        Assert.Equal(expected, decoded);
    }
}
