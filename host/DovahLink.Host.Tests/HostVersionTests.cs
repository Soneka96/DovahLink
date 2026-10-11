using System.Text.Json;

namespace DovahLink.Host.Tests;

/// <summary>Tests that the Host's wire-reported release version is the repository release version.</summary>
public class HostVersionTests
{
    /// <summary>Verifies the version <c>hello_ack</c> and the Host probe report equals the root <c>VERSION</c> file.</summary>
    [Fact]
    public void PublicProtocolHostVersion_MatchesRepositoryVersion()
    {
        Assert.Equal(ReadRepositoryVersion(), Constants.PublicProtocolHostVersion);
    }

    /// <summary>Verifies the adapter package manifest declares the repository release version.</summary>
    [Fact]
    public void AdapterPackageManifest_MatchesRepositoryVersion()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(RepositoryPath("adapter", "vcpkg.json")));

        Assert.Equal(ReadRepositoryVersion(), manifest.RootElement.GetProperty("version-string").GetString());
    }

    /// <summary>Verifies every canonical <c>hello_ack</c> fixture carries the repository release version.</summary>
    [Fact]
    public void HelloAckFixtures_MatchRepositoryVersion()
    {
        string[] fixtures = Directory.GetFiles(RepositoryPath("protocol", "fixtures", "connection"), "hello-ack*.json");

        Assert.NotEmpty(fixtures);
        foreach (string fixture in fixtures)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fixture));
            Assert.Equal(
                ReadRepositoryVersion(),
                document.RootElement.GetProperty("payload").GetProperty("hostVersion").GetString());
        }
    }

    /// <summary>Reads the root <c>VERSION</c> file.</summary>
    private static string ReadRepositoryVersion() => File.ReadAllText(RepositoryPath("VERSION")).Trim();

    /// <summary>Resolves <paramref name="relativeParts"/> under the repository root, found by walking up from the test output directory.</summary>
    private static string RepositoryPath(params string[] relativeParts)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VERSION")))
            {
                return Path.Combine([directory.FullName, .. relativeParts]);
            }
        }

        throw new FileNotFoundException("The repository root was not found above the test output directory.");
    }
}
