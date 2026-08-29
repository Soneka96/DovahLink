using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for <see cref="StateAreaId"/>.</summary>
public class StateAreaIdTests
{
    /// <summary>Verifies that two identifiers wrapping the same value are equal.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Assert.Equal(new StateAreaId("Character"), new StateAreaId("Character"));
    }

    /// <summary>Verifies that identifiers wrapping different values are not equal.</summary>
    [Fact]
    public void Equals_DifferentUnderlyingValues_AreNotEqual()
    {
        Assert.NotEqual(new StateAreaId("Character"), new StateAreaId("Inventory"));
    }

    /// <summary>Verifies that ToString returns the underlying value.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValue()
    {
        Assert.Equal("Character", new StateAreaId("Character").ToString());
    }
}
