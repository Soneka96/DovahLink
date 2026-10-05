using System.Text.Json;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// Locates the pinned sas-pairing dependency and the acquisition record that
/// <c>tooling/sas_pairing_dependency.py acquire --native</c> writes under the repository's ignored
/// <c>out/sas-pairing</c> directory. Nothing is searched for beyond this repository.
/// </summary>
internal static class SasPairingTestArtifacts
{
    /// <summary>The command that produces the artifacts these tests need.</summary>
    public const string AcquireCommand = "python tooling/sas_pairing_dependency.py acquire --native";

    /// <summary>The repository root: the nearest ancestor of the test output that holds the committed pin.</summary>
    public static string RepositoryRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "host", "sas-pairing-dependency.json")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException("The DovahLink repository root was not found above the test output.");
        }
    }

    /// <summary>Reads the committed dependency pin.</summary>
    /// <returns>The parsed pin.</returns>
    public static JsonElement LoadPin() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "host", "sas-pairing-dependency.json"))).RootElement.Clone();

    /// <summary>Reads the acquisition record, failing with the command that creates it when it is missing.</summary>
    /// <returns>The parsed record.</returns>
    public static JsonElement LoadRecord()
    {
        string path = Path.Combine(OutputDirectory, "acquisition.json");
        Assert.True(File.Exists(path), $"The sas-pairing dependency has not been acquired. Run: {AcquireCommand}");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    /// <summary>Returns the absolute path of the acquired native library, failing when it was not built.</summary>
    /// <returns>The native library path.</returns>
    public static string NativeLibraryPath()
    {
        JsonElement record = LoadRecord();
        Assert.True(
            record.GetProperty("native_library").ValueKind == JsonValueKind.String,
            $"The sas-pairing native library has not been built. Run: {AcquireCommand}");
        return Path.GetFullPath(Path.Combine(OutputDirectory, record.GetProperty("native_library").GetString()!));
    }

    /// <summary>The ignored directory every acquired artifact lives under.</summary>
    private static string OutputDirectory => Path.Combine(RepositoryRoot, "out", "sas-pairing");
}
