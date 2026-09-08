namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Creates and removes an isolated temporary directory for a test.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
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

    /// <summary>Removes the temporary directory when the test completes.</summary>
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
