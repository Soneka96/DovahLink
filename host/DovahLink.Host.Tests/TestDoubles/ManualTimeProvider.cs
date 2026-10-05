namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A <see cref="TimeProvider"/> whose timers never fire on their own: it records every timer created
/// through it, and a test fires one explicitly, so a deadline's anchoring can be proven without
/// depending on wall-clock scheduling.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>Guards <see cref="timers"/>, which production code appends to from its own tasks.</summary>
    private readonly object gate = new();

    /// <summary>Every timer created so far, in creation order.</summary>
    private readonly List<ManualTimer> timers = [];

    /// <summary>Gets a snapshot of every timer created so far, in creation order.</summary>
    public IReadOnlyList<ManualTimer> Timers
    {
        get
        {
            lock (gate)
            {
                return [.. timers];
            }
        }
    }

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, dueTime);
        lock (gate)
        {
            timers.Add(timer);
        }

        return timer;
    }
}

/// <summary>One timer created by <see cref="ManualTimeProvider"/>: it records reschedules and fires only through <see cref="Fire"/>.</summary>
public sealed class ManualTimer : ITimer
{
    /// <summary>The callback the timer's owner registered.</summary>
    private readonly TimerCallback callback;

    /// <summary>The state passed to <see cref="callback"/>.</summary>
    private readonly object? state;

    /// <summary>The number of <see cref="Change"/> calls made so far.</summary>
    private int changeCount;

    /// <summary>Whether the timer's owner has disposed it.</summary>
    private int disposed;

    /// <summary>Creates a timer that runs <paramref name="callback"/> only when fired.</summary>
    /// <param name="callback">The callback the timer's owner registered.</param>
    /// <param name="state">The state passed to <paramref name="callback"/>.</param>
    /// <param name="dueTime">The due time the timer was created with.</param>
    public ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime)
    {
        this.callback = callback;
        this.state = state;
        DueTime = dueTime;
    }

    /// <summary>Gets the due time the timer was created with.</summary>
    public TimeSpan DueTime { get; }

    /// <summary>Gets the number of times the timer's owner rescheduled it.</summary>
    public int ChangeCount => Volatile.Read(ref changeCount);

    /// <summary>Gets a value indicating whether the timer's owner has disposed it.</summary>
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>Runs the timer's callback once, as its due time elapsing would, unless it was disposed.</summary>
    public void Fire()
    {
        if (!IsDisposed)
        {
            callback(state);
        }
    }

    /// <inheritdoc/>
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref changeCount);
        return !IsDisposed;
    }

    /// <inheritdoc/>
    public void Dispose() => Interlocked.Exchange(ref disposed, 1);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
