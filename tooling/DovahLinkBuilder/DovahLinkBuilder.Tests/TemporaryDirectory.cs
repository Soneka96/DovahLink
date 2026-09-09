using System.Threading;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Creates and removes an isolated temporary directory for a test.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    /// <summary>The number of deletion attempts before giving up and letting the exception propagate.</summary>
    private const int MaxDeleteAttempts = 5;

    /// <summary>The delay between deletion attempts.</summary>
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Creates a unique temporary directory.</summary>
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "DovahLinkBuilderTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets the temporary directory path.</summary>
    public string Path { get; }

    /// <summary>
    /// Removes the temporary directory when the test completes, retrying briefly on a transient
    /// lock: a process this test just killed (see the real process-tree cancellation tests) can
    /// hold a file handle open for a short window while the OS finishes tearing it down, and
    /// Windows Defender's real-time scan can do the same to a freshly written file. A directory
    /// that is still locked after every attempt rethrows, so a genuine cleanup failure still
    /// surfaces instead of being silently swallowed.
    /// </summary>
    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        for (int attempt = 1; attempt <= MaxDeleteAttempts; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < MaxDeleteAttempts)
            {
                Thread.Sleep(DeleteRetryDelay);
            }
        }
    }
}
