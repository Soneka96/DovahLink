using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="ClientId"/>.</summary>
public class ClientIdTests
{
    /// <summary>Verifies that each call to <see cref="ClientId.NewId"/> produces a distinct value.</summary>
    [Fact]
    public void NewId_ReturnsDistinctValues()
    {
        ClientId first = ClientId.NewId();
        ClientId second = ClientId.NewId();

        Assert.NotEqual(first, second);
    }

    /// <summary>Verifies that two identifiers wrapping the same value are equal.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new ClientId(value), new ClientId(value));
    }

    /// <summary>Verifies that <see cref="ClientId.ToString"/> round-trips the underlying value.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValueText()
    {
        Guid value = Guid.NewGuid();
        var clientId = new ClientId(value);

        Assert.Equal(value.ToString(), clientId.ToString());
    }
}
