using System.Diagnostics;

namespace DovahLink.Host.Diagnostics;

/// <summary>
/// A developer-only trace sink for investigating runtime behavior in a Host that has no console. The
/// public <see cref="Write"/> entry point is marked <see cref="ConditionalAttribute"/> for
/// <c>DEBUG</c>, so a Release build compiles every call site away: no string is built, nothing is
/// written, and no file is created. Tracing never throws into the code being traced.
/// </summary>
public static class DiagnosticTrace
{
    /// <summary>Serializes appends so lines from concurrent Host threads never interleave.</summary>
    private static readonly object Gate = new();

    /// <summary>The per-user trace file, beside the Host's other local files.</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DovahLink", "host", "diagnostic-trace.log");

    /// <summary>Appends one timestamped line to <see cref="DefaultFilePath"/> in Debug builds only.</summary>
    /// <param name="message">The line to record.</param>
    [Conditional("DEBUG")]
    public static void Write(string message) => WriteTo(DefaultFilePath, message);

    /// <summary>Appends one timestamped line to <paramref name="filePath"/>, creating its directory; any I/O failure is swallowed.</summary>
    /// <param name="filePath">The trace file to append to.</param>
    /// <param name="message">The line to record.</param>
    internal static void WriteTo(string filePath, string message)
    {
        try
        {
            lock (Gate)
            {
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(filePath, $"{DateTimeOffset.UtcNow:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A diagnostic must never affect the Host behavior it observes.
        }
    }
}
