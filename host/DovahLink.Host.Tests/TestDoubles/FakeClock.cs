using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IClock"/> whose current time a test can set and advance.</summary>
public sealed class FakeClock : IClock
{
    /// <summary>Guards the mutable clock value when production code reads it from another task.</summary>
    private readonly object gate = new();

    /// <summary>The current fake time.</summary>
    private DateTimeOffset utcNow = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (gate)
            {
                return utcNow;
            }
        }
        set
        {
            lock (gate)
            {
                utcNow = value;
            }
        }
    }

    /// <summary>Moves the clock's current time forward.</summary>
    /// <param name="duration">How far to advance the clock.</param>
    public void Advance(TimeSpan duration)
    {
        lock (gate)
        {
            utcNow += duration;
        }
    }
}
