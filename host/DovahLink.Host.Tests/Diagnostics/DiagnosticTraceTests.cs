using DovahLink.Host.Diagnostics;

namespace DovahLink.Host.Tests.Diagnostics;

/// <summary>Tests for <see cref="DiagnosticTrace"/>.</summary>
public class DiagnosticTraceTests : IDisposable
{
    /// <summary>A unique scratch directory removed after each test.</summary>
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dovahlink-trace-" + Guid.NewGuid().ToString("N"));

    /// <summary>Removes the scratch directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies that lines are appended in order and the missing directory is created.</summary>
    [Fact]
    public void WriteTo_MissingDirectory_CreatesItAndAppendsLinesInOrder()
    {
        string path = Path.Combine(directory, "nested", "trace.log");

        DiagnosticTrace.WriteTo(path, "first");
        DiagnosticTrace.WriteTo(path, "second");

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" first", lines[0]);
        Assert.EndsWith(" second", lines[1]);
    }

    /// <summary>Verifies that carriage returns and line feeds remain inside one timestamped trace line.</summary>
    [Fact]
    public void WriteTo_MultilineMessage_EscapesLineBreaksWithinOneTimestampedLine()
    {
        string path = Path.Combine(directory, "trace.log");

        DiagnosticTrace.WriteTo(path, "first\r\nsecond\nthird\rfourth");

        string line = Assert.Single(File.ReadAllLines(path));
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3} ", line);
        Assert.EndsWith(" first\\r\\nsecond\\nthird\\rfourth", line);
    }

    /// <summary>Verifies that concurrent writers never lose or interleave a line.</summary>
    [Fact]
    public void WriteTo_ConcurrentWriters_KeepsEveryLineIntact()
    {
        string path = Path.Combine(directory, "trace.log");

        Parallel.For(0, 200, index => DiagnosticTrace.WriteTo(path, $"line-{index}"));

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(200, lines.Length);
        Assert.Equal(200, lines.Select(line => line[(line.IndexOf(' ') + 1)..]).Distinct().Count());
    }

    /// <summary>Verifies that an unwritable target never throws into the traced code.</summary>
    [Fact]
    public void WriteTo_UnwritableTarget_DoesNotThrow()
    {
        Directory.CreateDirectory(directory);

        // A directory path cannot be opened as a file.
        Exception? thrown = Record.Exception(() => DiagnosticTrace.WriteTo(directory, "ignored"));

        Assert.Null(thrown);
    }

    /// <summary>Verifies that an invalid path is swallowed rather than thrown.</summary>
    [Fact]
    public void WriteTo_InvalidPath_DoesNotThrow()
    {
        Exception? thrown = Record.Exception(() => DiagnosticTrace.WriteTo("\0:invalid", "ignored"));

        Assert.Null(thrown);
    }
}
