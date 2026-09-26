using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for the paired Host identity snapshot.</summary>
public class HostIdentityTests
{
    /// <summary>Verifies the identity snapshot keeps installation identity separate from display metadata.</summary>
    [Fact]
    public void Constructor_PreservesHostIdAndHostName()
    {
        var id = new HostId(Guid.Parse("81869993-955c-4ba3-a7d0-d35ca86078ea"));
        var identity = new HostIdentity(id, "SONEKA-DESKTOP");

        Assert.Equal(id, identity.HostId);
        Assert.Equal("SONEKA-DESKTOP", identity.HostName);
    }

    /// <summary>Verifies empty IDs and unsafe or oversized names cannot cross the Host boundary.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid\nname")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Constructor_InvalidHostName_Throws(string hostName)
    {
        var id = new HostId(Guid.Parse("81869993-955c-4ba3-a7d0-d35ca86078ea"));

        Assert.Throws<ArgumentException>(() => new HostIdentity(id, hostName));
    }

    /// <summary>Verifies a default Host ID cannot be emitted in a handshake.</summary>
    [Fact]
    public void Constructor_DefaultHostId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new HostIdentity(default, "SONEKA-DESKTOP"));
    }

    /// <summary>Verifies the name limit is measured in UTF-8 bytes, including its exact boundary.</summary>
    [Fact]
    public void Constructor_NameAtUtf8LimitIsAcceptedAndMultibyteOverflowIsRejected()
    {
        var id = new HostId(Guid.Parse("81869993-955c-4ba3-a7d0-d35ca86078ea"));
        string atLimit = new('é', Constants.MaxDisplayNameLengthBytes / 2);

        Assert.Equal(atLimit, new HostIdentity(id, atLimit).HostName);
        Assert.Throws<ArgumentException>(() => new HostIdentity(id, $"{atLimit}é"));
    }

    /// <summary>Verifies a missing computer name is rejected explicitly.</summary>
    [Fact]
    public void Constructor_NullHostName_Throws()
    {
        var id = new HostId(Guid.Parse("81869993-955c-4ba3-a7d0-d35ca86078ea"));

        Assert.Throws<ArgumentNullException>(() => new HostIdentity(id, null!));
    }
}
