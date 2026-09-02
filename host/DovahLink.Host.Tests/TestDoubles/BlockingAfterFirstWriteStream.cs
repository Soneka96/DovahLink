namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A stream that delegates its first write to an inner stream, then blocks every subsequent write
/// until released or the write's own cancellation token fires, for exercising a peer that stops
/// draining a send after an initial handshake response has already gone out successfully.
/// </summary>
public sealed class BlockingAfterFirstWriteStream : Stream
{
    /// <summary>The stream reads and the first write are delegated to.</summary>
    private readonly Stream inner;

    /// <summary>Completes when a blocked write should be allowed to proceed.</summary>
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once a write after the first has actually started blocking.</summary>
    private readonly TaskCompletionSource blockedWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The number of writes attempted so far.</summary>
    private int writeCount;

    /// <summary>Creates a stream that delegates its first write to <paramref name="inner"/> but blocks every write after that.</summary>
    /// <param name="inner">The stream reads and the first write are delegated to.</param>
    public BlockingAfterFirstWriteStream(Stream inner) => this.inner = inner;

    /// <summary>Gets a task that completes once a write after the first has actually started blocking.</summary>
    public Task BlockedWriteStarted => blockedWriteStarted.Task;

    /// <summary>Allows a currently blocked write to proceed and actually reach <see cref="inner"/>.</summary>
    public void Release() => release.TrySetResult();

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

        blockedWriteStarted.TrySetResult();
        await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            release.TrySetResult();
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
