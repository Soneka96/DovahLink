using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="HostId"/>.</summary>
public class HostIdTests
{
    /// <summary>Verifies that new IDs are non-empty UUID strings and are independently generated.</summary>
    [Fact]
    public void NewId_ReturnsDistinctNonEmptyUuidValues()
    {
        HostId first = HostId.NewId();
        HostId second = HostId.NewId();

        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.NotEqual(first, second);
        Assert.True(Guid.TryParseExact(first.ToString(), "D", out _));
    }

    /// <summary>Verifies that an empty UUID cannot represent a Host installation.</summary>
    [Fact]
    public void Constructor_EmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new HostId(Guid.Empty));
    }
}
