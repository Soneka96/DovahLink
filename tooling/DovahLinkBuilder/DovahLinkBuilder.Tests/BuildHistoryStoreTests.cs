using DovahLink.DovahLinkBuilder.Persistence;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies <see cref="BuildHistoryStore"/>'s persistence behavior.</summary>
public sealed class BuildHistoryStoreTests
{
    /// <summary>Returns an empty history when no file has ever been saved.</summary>
    [Fact]
    public void GetRecentReturnsEmptyWhenNoFileExists()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new BuildHistoryStore(temporaryDirectory.Path);

        Assert.Empty(store.GetRecent());
    }

    /// <summary>Round-trips a recorded entry exactly.</summary>
    [Fact]
    public void AddThenGetRecentRoundTripsTheEntry()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new BuildHistoryStore(temporaryDirectory.Path);
        var entry = new BuildHistoryEntry(
            Timestamp: new DateTimeOffset(2026, 1, 5, 8, 31, 0, TimeSpan.Zero),
            Result: BuildHistoryResult.Succeeded,
            Version: "0.3.3",
            Profile: "Release",
            Duration: TimeSpan.FromSeconds(48.2),
            ArtifactPath: @"C:\repo\tooling\out\DovahLink-Adapter-0.3.3.zip",
            FailedStage: null,
            Sha256: "89ae2f0c",
            Note: "Testing pairing reconnect fix");

        store.Add(entry);

        Assert.Equal([entry], store.GetRecent());
    }

    /// <summary>Returns recorded entries most recent first.</summary>
    [Fact]
    public void GetRecentReturnsMostRecentFirst()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new BuildHistoryStore(temporaryDirectory.Path);
        BuildHistoryEntry older = Fixtures.BuildBuildHistoryEntry(version: "0.3.2");
        BuildHistoryEntry newer = Fixtures.BuildBuildHistoryEntry(version: "0.3.3");

        store.Add(older);
        store.Add(newer);

        Assert.Equal([newer, older], store.GetRecent());
    }

    /// <summary>Trims the oldest entry once the retained maximum of ten is exceeded.</summary>
    [Fact]
    public void AddTrimsToTheRetainedMaximumOfTen()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new BuildHistoryStore(temporaryDirectory.Path);
        for (int i = 0; i < 11; i++)
        {
            store.Add(Fixtures.BuildBuildHistoryEntry(version: $"0.3.{i}"));
        }

        IReadOnlyList<BuildHistoryEntry> recent = store.GetRecent();

        Assert.Equal(10, recent.Count);
        Assert.Equal("0.3.10", recent[0].Version);
        Assert.DoesNotContain(recent, entry => entry.Version == "0.3.0");
    }

    /// <summary>Round-trips a failed entry, whose <c>ArtifactPath</c> and <c>Sha256</c> are null, correctly.</summary>
    [Fact]
    public void RoundTripsAFailedEntryWithNullArtifactFields()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new BuildHistoryStore(temporaryDirectory.Path);
        var entry = new BuildHistoryEntry(
            Timestamp: new DateTimeOffset(2026, 1, 5, 8, 31, 0, TimeSpan.Zero),
            Result: BuildHistoryResult.Failed,
            Version: "0.3.3",
            Profile: "Release",
            Duration: TimeSpan.FromSeconds(3.8),
            ArtifactPath: null,
            FailedStage: "Publish Host",
            Sha256: null,
            Note: null);

        store.Add(entry);

        Assert.Equal([entry], store.GetRecent());
    }

    /// <summary>Returns an empty history, rather than throwing, when the saved file is corrupt.</summary>
    [Fact]
    public void GetRecentReturnsEmptyWhenTheFileIsCorrupt()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "build-history.json"), "{ not valid json");
        var store = new BuildHistoryStore(temporaryDirectory.Path);

        Assert.Empty(store.GetRecent());
    }
}
