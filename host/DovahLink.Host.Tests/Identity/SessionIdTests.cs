using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="SessionId"/>.</summary>
public class SessionIdTests
{
    /// <summary>Verifies that each call to <see cref="SessionId.NewId"/> produces a distinct value.</summary>
    [Fact]
    public void NewId_ReturnsDistinctValues()
    {
        SessionId first = SessionId.NewId();
        SessionId second = SessionId.NewId();

        Assert.NotEqual(first, second);
    }

    /// <summary>Verifies that two identifiers wrapping the same value are equal.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new SessionId(value), new SessionId(value));
    }

    /// <summary>Verifies that <see cref="SessionId.ToString"/> round-trips the underlying value.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValueText()
    {
        Guid value = Guid.NewGuid();
        var sessionId = new SessionId(value);

        Assert.Equal(value.ToString(), sessionId.ToString());
    }
}
