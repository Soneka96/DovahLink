namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A stream that delegates every operation to an inner stream while counting the bytes its reads
/// have delivered, so a test can wait until the reader has consumed specific inbound bytes and come
/// back for more -- an ordering signal that proves the reader finished processing that data instead
/// of guessing with a delay.
/// </summary>
public sealed class ReadObservingStream : Stream
{
    /// <summary>The stream every operation is ultimately delegated to.</summary>
    private readonly Stream inner;

    /// <summary>Guards <see cref="bytesRead"/> and <see cref="waiters"/> across the reader's and the test's tasks.</summary>
    private readonly object gate = new();

    /// <summary>Waiters for a read that starts once at least their threshold of bytes was delivered.</summary>
    private readonly List<(long Threshold, TaskCompletionSource Signal)> waiters = [];

    /// <summary>The total number of bytes every completed read has delivered.</summary>
    private long bytesRead;

    /// <summary>Creates a stream that observes reads delegated to <paramref name="inner"/>.</summary>
    /// <param name="inner">The stream every operation is ultimately delegated to.</param>
    public ReadObservingStream(Stream inner) => this.inner = inner;

    /// <summary>Gets the total number of bytes every completed read has delivered.</summary>
    public long BytesRead
    {
        get
        {
            lock (gate)
            {
                return bytesRead;
            }
        }
    }

    /// <inheritdoc/>
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => inner.CanWrite;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns a task that completes when a read starts after at least <paramref name="threshold"/>
    /// bytes in total were delivered: the reader has consumed everything up to that point and is
    /// asking for more.
    /// </summary>
    /// <param name="threshold">The total delivered byte count the starting read must have reached.</param>
    /// <returns>A task completed by the first qualifying read.</returns>
    public Task WaitForReadStartedAfterAsync(long threshold)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            waiters.Add((threshold, signal));
        }

        return signal.Task;
    }

    /// <inheritdoc/>
    public override void Flush() => inner.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        SignalReadStarted();
        int read = inner.Read(buffer, offset, count);
        RecordDelivered(read);
        return read;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        SignalReadStarted();
        int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        RecordDelivered(read);
        return read;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.WriteAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Completes every waiter whose threshold the bytes delivered so far have reached.</summary>
    private void SignalReadStarted()
    {
        lock (gate)
        {
            for (int index = waiters.Count - 1; index >= 0; index--)
            {
                if (bytesRead >= waiters[index].Threshold)
                {
                    waiters[index].Signal.TrySetResult();
                    waiters.RemoveAt(index);
                }
            }
        }
    }

    /// <summary>Adds one completed read's byte count to the delivered total.</summary>
    private void RecordDelivered(int read)
    {
        lock (gate)
        {
            bytesRead += read;
        }
    }
}
