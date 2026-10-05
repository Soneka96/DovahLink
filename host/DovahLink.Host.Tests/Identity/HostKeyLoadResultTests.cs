using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests that a Host key load result's key presence always matches its status.</summary>
public sealed class HostKeyLoadResultTests
{
    /// <summary>Verifies the two success results carry the key and report it available.</summary>
    [Fact]
    public void SuccessFactories_CarryKeyAndStatus()
    {
        P256PublicKey key = P256PublicKey.FromSubjectPublicKeyInfo(Fixtures.BuildP256SubjectPublicKeyInfo());

        HostKeyLoadResult provisioned = HostKeyLoadResult.Provisioned(key);
        HostKeyLoadResult loaded = HostKeyLoadResult.Loaded(key);

        Assert.Equal(HostKeyStatus.Provisioned, provisioned.Status);
        Assert.Equal(HostKeyStatus.Loaded, loaded.Status);
        Assert.Same(key, provisioned.PublicKey);
        Assert.Same(key, loaded.PublicKey);
        Assert.True(provisioned.IsAvailable);
        Assert.True(loaded.IsAvailable);
    }

    /// <summary>Verifies a success result cannot be created without a key.</summary>
    [Fact]
    public void SuccessFactories_NullKey_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => HostKeyLoadResult.Provisioned(null!));
        Assert.Throws<ArgumentNullException>(() => HostKeyLoadResult.Loaded(null!));
    }

    /// <summary>Verifies every failure status produces an unavailable result with no key.</summary>
    /// <param name="status">A failure status.</param>
    [Theory]
    [InlineData(HostKeyStatus.ProvisionedKeyMissing)]
    [InlineData(HostKeyStatus.KeyInaccessible)]
    [InlineData(HostKeyStatus.KeyPolicyInvalid)]
    [InlineData(HostKeyStatus.PublicKeyMismatch)]
    [InlineData(HostKeyStatus.RecordCorrupt)]
    [InlineData(HostKeyStatus.StaleIdentityRetirementFailed)]
    [InlineData(HostKeyStatus.KeyLockTimedOut)]
    public void Unavailable_FailureStatus_HasNoKey(HostKeyStatus status)
    {
        HostKeyLoadResult result = HostKeyLoadResult.Unavailable(status);

        Assert.Equal(status, result.Status);
        Assert.Null(result.PublicKey);
        Assert.False(result.IsAvailable);
    }

    /// <summary>Verifies an unavailable result can never claim a success or undefined status.</summary>
    /// <param name="status">A success or undefined status.</param>
    [Theory]
    [InlineData(HostKeyStatus.Provisioned)]
    [InlineData(HostKeyStatus.Loaded)]
    [InlineData((HostKeyStatus)999)]
    public void Unavailable_SuccessOrUndefinedStatus_Throws(HostKeyStatus status)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HostKeyLoadResult.Unavailable(status));
    }
}
