using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="AdapterInstanceId"/>.</summary>
public class AdapterInstanceIdTests
{
    /// <summary>Verifies that each call to <see cref="AdapterInstanceId.NewId"/> produces a distinct value.</summary>
    [Fact]
    public void NewId_ReturnsDistinctValues()
    {
        AdapterInstanceId first = AdapterInstanceId.NewId();
        AdapterInstanceId second = AdapterInstanceId.NewId();

        Assert.NotEqual(first, second);
    }

    /// <summary>Verifies that two identifiers wrapping the same value are equal.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new AdapterInstanceId(value), new AdapterInstanceId(value));
    }

    /// <summary>Verifies that <see cref="AdapterInstanceId.ToString"/> round-trips the underlying value.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValueText()
    {
        Guid value = Guid.NewGuid();
        var adapterInstanceId = new AdapterInstanceId(value);

        Assert.Equal(value.ToString(), adapterInstanceId.ToString());
    }
}
