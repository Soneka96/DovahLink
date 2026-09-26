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
        var identity = new HostIdentity(id, "GONCALO-DESKTOP");

        Assert.Equal(id, identity.HostId);
        Assert.Equal("GONCALO-DESKTOP", identity.HostName);
    }
}
