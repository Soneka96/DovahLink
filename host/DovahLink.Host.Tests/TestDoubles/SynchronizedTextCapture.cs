using System.Text;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A <see cref="TextWriter"/> that captures every write behind one lock, so a test can observe its
/// accumulated text from a different task than the one writing to it without racing the backing
/// buffer. <see cref="Snapshot"/> is the only supported way to read the captured text: it always
/// returns a coherent, immutable copy taken at one point in time under that same lock, never a
/// value that can still change underneath the caller.
/// </summary>
public sealed class SynchronizedTextCapture : TextWriter
{
    /// <summary>Guards <see cref="buffer"/> against concurrent access from the writing and reading tasks.</summary>
    private readonly object gate = new();

    /// <summary>The text written so far.</summary>
    private readonly StringBuilder buffer = new();

    /// <inheritdoc/>
    public override Encoding Encoding => Encoding.UTF8;

    /// <inheritdoc/>
    public override void Write(char value)
    {
        lock (gate)
        {
            buffer.Append(value);
        }
    }

    /// <inheritdoc/>
    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }

        lock (gate)
        {
            buffer.Append(value);
        }
    }

    /// <summary>Returns a coherent, immutable copy of every character written so far.</summary>
    public string Snapshot()
    {
        lock (gate)
        {
            return buffer.ToString();
        }
    }
}
