using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IClock"/> whose current time a test can set and advance.</summary>
public sealed class FakeClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Moves the clock's current time forward.</summary>
    /// <param name="duration">How far to advance the clock.</param>
    public void Advance(TimeSpan duration) => UtcNow += duration;
}
