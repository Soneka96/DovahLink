namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A stream that delegates its first write to an inner stream untracked, then tracks how many writes
/// after that are ever concurrently in flight, for proving a writer never issues overlapping
/// application-level writes. The first write is excluded because it is the HTTP <c>101</c> handshake
/// response, not an application frame, and must not contaminate the measurement.
/// </summary>
public sealed class ConcurrentWriteTrackingStream : Stream
{
    /// <summary>The stream every write is ultimately delegated to.</summary>
    private readonly Stream inner;

    /// <summary>The number of writes attempted so far, including the untracked first one.</summary>
    private int writeCount;

    /// <summary>The number of tracked writes currently in flight.</summary>
    private int activeWrites;

    /// <summary>The highest number of tracked writes ever observed concurrently in flight.</summary>
    private int maxConcurrentWrites;

    /// <summary>Creates a stream that delegates its first write to <paramref name="inner"/> untracked, then tracks concurrency on every write after that.</summary>
    /// <param name="inner">The stream every write is ultimately delegated to.</param>
    public ConcurrentWriteTrackingStream(Stream inner) => this.inner = inner;

    /// <summary>Gets the highest number of tracked writes ever observed concurrently in flight.</summary>
    public int MaxConcurrentWrites => Volatile.Read(ref maxConcurrentWrites);

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

    /// <summary>Not exercised by the WebSocket layer under test, which writes only through <see cref="WriteAsync"/>.</summary>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref writeCount) == 1)
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            return;
        }

        int active = Interlocked.Increment(ref activeWrites);
        InterlockedMax(ref maxConcurrentWrites, active);
        try
        {
            // Yields the scheduler a real gap between recording this write as active and actually
            // performing it, so a hypothetical concurrent second write would land and be observed
            // here rather than the proof depending on incidental timing.
            await Task.Yield();
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref activeWrites);
        }
    }

    /// <summary>Atomically sets <paramref name="target"/> to the larger of its current value and <paramref name="value"/>.</summary>
    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
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
