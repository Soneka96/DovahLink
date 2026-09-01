namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A stream that delegates its first write to an inner stream, then fails every subsequent write, for
/// exercising a write fault that occurs only after an initial handshake response has already been
/// sent successfully.
/// </summary>
public sealed class FailAfterFirstWriteStream : Stream
{
    /// <summary>The stream reads and the first write are delegated to.</summary>
    private readonly Stream inner;

    /// <summary>The exception raised by every write after the first.</summary>
    private readonly Exception failure;

    /// <summary>The number of writes attempted so far.</summary>
    private int writeCount;

    /// <summary>Creates a stream that delegates its first write to <paramref name="inner"/> but fails every write after that.</summary>
    /// <param name="inner">The stream reads and the first write are delegated to.</param>
    /// <param name="failure">The exception raised by every write after the first.</param>
    public FailAfterFirstWriteStream(Stream inner, Exception? failure = null)
    {
        this.inner = inner;
        this.failure = failure ?? new IOException("Simulated write fault.");
    }

    /// <inheritdoc/>
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (Interlocked.Increment(ref writeCount) == 1)
        {
            inner.Write(buffer, offset, count);
            return;
        }

        throw failure;
    }

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref writeCount) == 1)
        {
            return inner.WriteAsync(buffer, cancellationToken);
        }

        throw failure;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
