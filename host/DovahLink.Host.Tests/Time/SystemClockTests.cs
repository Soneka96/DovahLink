using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.Time;

/// <summary>Tests for <see cref="SystemClock"/>.</summary>
public class SystemClockTests
{
    /// <summary>Verifies that UtcNow reports the real current time, within a generous tolerance.</summary>
    [Fact]
    public void UtcNow_ReturnsCurrentTime()
    {
        var clock = new SystemClock();

        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset reported = clock.UtcNow;
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.InRange(reported, before, after);
    }
}
