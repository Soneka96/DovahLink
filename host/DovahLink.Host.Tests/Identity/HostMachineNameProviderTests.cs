using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for the Windows operating-system computer-name adapter.</summary>
public class HostMachineNameProviderTests
{
    /// <summary>Verifies the adapter returns the name supplied by the operating system.</summary>
    [Fact]
    public void GetMachineName_ReturnsOperatingSystemName()
    {
        var provider = new SystemHostMachineNameProvider();

        Assert.Equal(Environment.MachineName, provider.GetMachineName());
    }
}
