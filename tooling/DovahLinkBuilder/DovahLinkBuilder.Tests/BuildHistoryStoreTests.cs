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

    /// <summary>Returns an empty history, rather than throwing, when the saved file cannot be read due to a sharing violation.</summary>
    [Fact]
    public void GetRecentReturnsEmptyWhenTheFileCannotBeRead()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(temporaryDirectory.Path, "build-history.json");
        File.WriteAllText(filePath, "[]");
        var store = new BuildHistoryStore(temporaryDirectory.Path);
        using var lockingStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Empty(store.GetRecent());
    }

    /// <summary>
    /// Throws, rather than silently discarding the entry, when the file cannot be written -- unlike
    /// <see cref="GetRecentReturnsEmptyWhenTheFileCannotBeRead"/>: recording a new build is not
    /// optional in the same way reading history back is, so a caller for whom that distinction
    /// matters (see <see cref="Ui.BuildPageViewModel"/>'s own handling) must be able to observe it.
    /// Also proves the temporary file used for the atomic write is cleaned up rather than left behind
    /// when the destination cannot be replaced.
    /// </summary>
    [Fact]
    public void AddThrowsWhenTheFileCannotBeWritten()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(temporaryDirectory.Path, "build-history.json");
        File.WriteAllText(filePath, "[]");
        var store = new BuildHistoryStore(temporaryDirectory.Path);
        using var lockingStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Exception? exception = Record.Exception(() => store.Add(Fixtures.BuildBuildHistoryEntry()));

        Assert.True(exception is IOException or UnauthorizedAccessException, $"Expected an IOException or UnauthorizedAccessException but got {exception?.GetType()}.");
        Assert.False(File.Exists(filePath + ".tmp"));
    }

    /// <summary>Leaves only the final history file behind, proving the write goes through a temporary file rather than truncating it in place.</summary>
    [Fact]
    public void AddDoesNotLeaveATemporaryFileBehind()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new BuildHistoryStore(temporaryDirectory.Path);

        store.Add(Fixtures.BuildBuildHistoryEntry());

        Assert.Equal(["build-history.json"], Directory.GetFiles(temporaryDirectory.Path).Select(Path.GetFileName));
    }
}
