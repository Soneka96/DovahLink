using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises the process-lifetime client identity.</summary>
public class ClientIdentityTests
{
    /// <summary>Verifies that the client identity is a valid, non-empty GUID.</summary>
    [Fact]
    public void CurrentIsANonEmptyGuid()
    {
        Assert.NotEqual(Guid.Empty, ClientIdentity.Current);
    }

    /// <summary>Verifies that reading the client identity twice returns the same value.</summary>
    [Fact]
    public void CurrentIsStableAcrossRepeatedReads()
    {
        Guid first = ClientIdentity.Current;
        Guid second = ClientIdentity.Current;

        Assert.Equal(first, second);
    }
}
