namespace DovahLink.Host.Time;

/// <summary>A source of the current time, so expiry logic can be tested against a controllable clock.</summary>
public interface IClock
{
    /// <summary>The current time in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>An <see cref="IClock"/> backed by the real system clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
