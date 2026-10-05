using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests the tracked-quest value shape shared with public state fixtures.</summary>
public class TrackedQuestsTests
{
    /// <summary>Uses the public protocol's camel-case properties and snake-case enum names.</summary>
    private static readonly JsonSerializerOptions ProtocolOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Reads one public state fixture copied beside the test assembly.</summary>
    /// <param name="fileName">The file under <c>protocol/fixtures/state</c>.</param>
    /// <returns>The parsed complete protocol envelope.</returns>
    private static JsonDocument ReadStateFixture(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", "state", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>Verifies fixture decoding and re-encoding preserves every quest and objective field.</summary>
    [Fact]
    public void JsonRoundTrip_MatchesAvailableProtocolFixture()
    {
        using JsonDocument fixture = ReadStateFixture("state-snapshot-tracked-quests.json");
        JsonElement fixtureValue = fixture.RootElement.GetProperty("payload").GetProperty("data").GetProperty("value");
        TrackedQuests? decoded = JsonSerializer.Deserialize<TrackedQuests>(fixtureValue, ProtocolOptions);

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded.Quests.Count);
        Assert.Equal(10u, decoded.Quests[0].QuestId);
        Assert.Equal("Cenário — Localized", decoded.Quests[0].Title);
        Assert.Equal((byte)254, decoded.Quests[0].Type);
        Assert.Collection(
            decoded.Quests[0].Objectives,
            objective => AssertObjective(objective, 10, 5, "Dormant authored text", TrackedQuestObjectiveState.Dormant),
            objective => AssertObjective(objective, 20, 5, "Localized displayed objective", TrackedQuestObjectiveState.Displayed),
            objective => AssertObjective(objective, 30, 5, null, TrackedQuestObjectiveState.Completed),
            objective => AssertObjective(objective, 40, 5, "Completed and displayed", TrackedQuestObjectiveState.CompletedAndDisplayed),
            objective => AssertObjective(objective, 50, 5, "Failed objective", TrackedQuestObjectiveState.Failed),
            objective => AssertObjective(objective, 60, 5, "Failed and displayed", TrackedQuestObjectiveState.FailedAndDisplayed));
        Assert.Equal(20u, decoded.Quests[1].QuestId);

        using JsonDocument encoded = JsonDocument.Parse(JsonSerializer.Serialize(decoded, ProtocolOptions));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(fixtureValue.GetRawText()), JsonNode.Parse(encoded.RootElement.GetRawText())));
    }

    /// <summary>Verifies empty tracked state and source unavailability remain distinct fixture values.</summary>
    [Fact]
    public void JsonFixtures_DistinguishAvailableEmptyFromUnavailable()
    {
        using JsonDocument emptyFixture = ReadStateFixture("state-snapshot-tracked-quests-empty.json");
        JsonElement emptyValue = emptyFixture.RootElement.GetProperty("payload").GetProperty("data").GetProperty("value");
        TrackedQuests? empty = JsonSerializer.Deserialize<TrackedQuests>(emptyValue, ProtocolOptions);
        Assert.NotNull(empty);
        Assert.Empty(empty.Quests);

        using JsonDocument unavailableFixture = ReadStateFixture("state-snapshot-tracked-quests-unavailable.json");
        JsonElement unavailableValue = unavailableFixture.RootElement.GetProperty("payload").GetProperty("data").GetProperty("value");
        Assert.Equal(JsonValueKind.Null, unavailableValue.ValueKind);
        Assert.Null(JsonSerializer.Deserialize<TrackedQuests>(unavailableValue, ProtocolOptions));
    }

    /// <summary>Verifies nested immutable values compare by content for stable snapshot revisions.</summary>
    [Fact]
    public void Equals_UsesOrderedQuestAndObjectiveContents()
    {
        var first = new TrackedQuests(
        [
            new TrackedQuest(10, "Quest", 8, [new QuestObjective(1, 2, null, TrackedQuestObjectiveState.Displayed)]),
        ]);
        var same = new TrackedQuests(
        [
            new TrackedQuest(10, "Quest", 8, [new QuestObjective(1, 2, null, TrackedQuestObjectiveState.Displayed)]),
        ]);
        var changed = new TrackedQuests(
        [
            new TrackedQuest(10, "Quest", 8, [new QuestObjective(1, 2, null, TrackedQuestObjectiveState.Completed)]),
        ]);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, changed);
    }

    /// <summary>Checks one fixture objective against the complete typed objective fact.</summary>
    /// <param name="objective">The decoded objective.</param>
    /// <param name="index">The authored objective index.</param>
    /// <param name="instanceId">The runtime quest-instance ID.</param>
    /// <param name="text">The nullable localized authored text.</param>
    /// <param name="state">The raw Skyrim state mapping.</param>
    private static void AssertObjective(
        QuestObjective objective,
        ushort index,
        uint instanceId,
        string? text,
        TrackedQuestObjectiveState state)
    {
        Assert.Equal(index, objective.Index);
        Assert.Equal(instanceId, objective.InstanceId);
        Assert.Equal(text, objective.Text);
        Assert.Equal(state, objective.State);
    }
}
