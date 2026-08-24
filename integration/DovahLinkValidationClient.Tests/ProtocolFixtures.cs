using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>
/// Loads canonical protocol fixtures from <c>protocol/fixtures/</c>, the language-neutral source of
/// truth this project shares with the Bridge and Dart SDK test suites (see
/// <c>protocol/fixtures/README.md</c>). Mirrors <c>bridge/protocol/fixture_test_support.hpp</c>'s
/// role for the Bridge's own Catch2 tests.
/// </summary>
public static class ProtocolFixtures
{
    /// <summary>
    /// Reads a protocol fixture file as its complete text contents.
    /// </summary>
    /// <param name="relativePath">The fixture's path relative to <c>protocol/fixtures/</c>.</param>
    /// <returns>The fixture file's raw JSON text.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the fixture cannot be found.</exception>
    public static string ReadFixture(string relativePath)
    {
        string path = Path.Combine(LocateFixturesDirectory(), relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Protocol fixture not found: {relativePath}", path);
        }
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Reads and decodes one protocol fixture's envelope.
    /// </summary>
    /// <param name="relativePath">The fixture's path relative to <c>protocol/fixtures/</c>.</param>
    /// <returns>The decoded envelope.</returns>
    public static Envelope DecodeFixtureEnvelope(string relativePath) => Envelope.Decode(ReadFixture(relativePath));

    /// <summary>
    /// Reads one protocol fixture's <c>payload</c> object directly, for tests that decode a specific
    /// message payload without needing the full envelope.
    /// </summary>
    /// <param name="relativePath">The fixture's path relative to <c>protocol/fixtures/</c>.</param>
    /// <returns>The fixture's <c>payload</c> field.</returns>
    public static JsonObject ReadFixturePayload(string relativePath) =>
        DecodeFixtureEnvelope(relativePath).Payload;

    /// <summary>
    /// Locates the repository's <c>protocol/fixtures/</c> directory by walking up from the test
    /// assembly's base directory, mirroring <see cref="HarnessProcess"/>'s own ancestor search for
    /// the bridge harness executable.
    /// </summary>
    /// <returns>The absolute path to <c>protocol/fixtures/</c>.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when no ancestor contains it.</exception>
    private static string LocateFixturesDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "protocol", "fixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException(
            $"Could not find protocol/fixtures under any ancestor of {AppContext.BaseDirectory}.");
    }
}
