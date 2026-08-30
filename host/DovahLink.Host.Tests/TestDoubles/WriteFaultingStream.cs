namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A stream that fails every write with a selected exception while delegating reads to an inner stream, for exercising write-fault handling.</summary>
public sealed class WriteFaultingStream : Stream
{
    /// <summary>The stream reads are delegated to.</summary>
    private readonly Stream inner;

    /// <summary>The exception raised by every write.</summary>
    private readonly Exception failure;

    /// <summary>Creates a stream that reads from <paramref name="inner"/> but fails every write.</summary>
    /// <param name="inner">The stream reads are delegated to.</param>
    /// <param name="failure">The exception raised by every write.</param>
    public WriteFaultingStream(Stream inner, Exception? failure = null)
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
    public override void Write(byte[] buffer, int offset, int count) => throw failure;

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        throw failure;

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
