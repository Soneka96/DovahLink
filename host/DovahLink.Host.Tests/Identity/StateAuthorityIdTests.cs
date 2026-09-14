using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="StateAuthorityId"/>.</summary>
public class StateAuthorityIdTests
{
    /// <summary>Verifies that two identifiers wrapping the same value are equal.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new StateAuthorityId(value), new StateAuthorityId(value));
    }

    /// <summary>Verifies that <see cref="StateAuthorityId.ToString"/> round-trips the underlying value.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValueText()
    {
        Guid value = Guid.NewGuid();
        var stateAuthorityId = new StateAuthorityId(value);

        Assert.Equal(value.ToString(), stateAuthorityId.ToString());
    }
}
