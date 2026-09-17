using System.Text.Json;

namespace DovahLink.Host.Tests.State;

/// <summary>
/// Verifies that the host's hardcoded live-state capture enums remain synchronized with the
/// shared <c>adapter-host-ipc/fixtures/live-state-catalog.json</c> contract fixture, the same way
/// <see cref="Adapter.Ipc.AdapterIpcConnectionTests"/> already does for the private-IPC rate limit.
/// </summary>
public class LiveStateCatalogFixtureTests
{
    private static JsonElement ReadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "adapter-host-ipc", "fixtures", "live-state-catalog.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void SampleTokens_MatchSharedCatalogFixture()
    {
        JsonElement sampleTokens = ReadFixture().GetProperty("sampleTokens");

        Assert.Equal((uint)CharacterSampleToken.CharacterVitals, sampleTokens.GetProperty("characterVitals").GetUInt32());
        Assert.Equal((uint)CharacterSampleToken.CharacterXp, sampleTokens.GetProperty("characterXp").GetUInt32());
        Assert.Equal((uint)CharacterSampleToken.CharacterLevelBaseline, sampleTokens.GetProperty("characterLevelBaseline").GetUInt32());
    }

    [Fact]
    public void EventKeys_MatchSharedCatalogFixture()
    {
        JsonElement eventKeys = ReadFixture().GetProperty("eventKeys");

        Assert.Equal((uint)CharacterEventKey.CharacterLevelChanged, eventKeys.GetProperty("characterLevelChanged").GetUInt32());
    }
}
