using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="ConnectionId"/>.</summary>
public class ConnectionIdTests
{
    /// <summary>Verifies that separately generated connection identities are distinct.</summary>
    [Fact]
    public void NewId_ReturnsDistinctValues()
    {
        ConnectionId first = ConnectionId.NewId();
        ConnectionId second = ConnectionId.NewId();

        Assert.NotEqual(first, second);
    }

    /// <summary>Verifies that equal underlying values produce equal connection identities.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new ConnectionId(value), new ConnectionId(value));
    }

    /// <summary>Verifies that string conversion returns the underlying identifier text.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValueText()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(value.ToString(), new ConnectionId(value).ToString());
    }
}
