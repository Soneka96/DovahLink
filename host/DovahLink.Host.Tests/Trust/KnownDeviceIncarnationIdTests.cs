using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="KnownDeviceIncarnationId"/>.</summary>
public class KnownDeviceIncarnationIdTests
{
    /// <summary>Verifies that each call to <see cref="KnownDeviceIncarnationId.NewId"/> produces a distinct value.</summary>
    [Fact]
    public void NewId_ReturnsDistinctValues()
    {
        KnownDeviceIncarnationId first = KnownDeviceIncarnationId.NewId();
        KnownDeviceIncarnationId second = KnownDeviceIncarnationId.NewId();

        Assert.NotEqual(first, second);
    }

    /// <summary>Verifies that two identifiers wrapping the same value are equal.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new KnownDeviceIncarnationId(value), new KnownDeviceIncarnationId(value));
    }

    /// <summary>Verifies that <see cref="KnownDeviceIncarnationId.ToString"/> round-trips the underlying value.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValueText()
    {
        Guid value = Guid.NewGuid();
        var incarnationId = new KnownDeviceIncarnationId(value);

        Assert.Equal(value.ToString(), incarnationId.ToString());
    }

    /// <summary>Verifies that the default value wraps an empty <see cref="Guid"/>, matching persistence's rejection check for it.</summary>
    [Fact]
    public void Default_WrapsEmptyGuid()
    {
        Assert.Equal(Guid.Empty, default(KnownDeviceIncarnationId).Value);
    }
}
