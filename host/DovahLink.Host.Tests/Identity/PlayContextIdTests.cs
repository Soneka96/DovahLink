using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="PlayContextId"/>.</summary>
public class PlayContextIdTests
{
    /// <summary>Verifies that each call to <see cref="PlayContextId.NewId"/> produces a distinct value.</summary>
    [Fact]
    public void NewId_ReturnsDistinctValues()
    {
        PlayContextId first = PlayContextId.NewId();
        PlayContextId second = PlayContextId.NewId();

        Assert.NotEqual(first, second);
    }

    /// <summary>Verifies that two identifiers wrapping the same value are equal.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new PlayContextId(value), new PlayContextId(value));
    }

    /// <summary>Verifies that <see cref="PlayContextId.ToString"/> round-trips the underlying value.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValueText()
    {
        Guid value = Guid.NewGuid();
        var playContextId = new PlayContextId(value);

        Assert.Equal(value.ToString(), playContextId.ToString());
    }
}
