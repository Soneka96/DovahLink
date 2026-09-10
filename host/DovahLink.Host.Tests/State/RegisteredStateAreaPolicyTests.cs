using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for <see cref="RegisteredStateAreaPolicy"/>.</summary>
public class RegisteredStateAreaPolicyTests
{
    /// <summary>Verifies that a fresh policy reports no registered areas.</summary>
    [Fact]
    public void Count_FreshPolicy_IsZero()
    {
        var policy = new RegisteredStateAreaPolicy();

        Assert.Equal(0, policy.Count);
    }

    /// <summary>Verifies that an area not yet registered is reported as unregistered.</summary>
    [Fact]
    public void IsRegistered_NeverRegistered_ReturnsFalse()
    {
        var policy = new RegisteredStateAreaPolicy();

        Assert.False(policy.IsRegistered(new StateAreaId("example_area")));
    }

    /// <summary>Verifies that registering an area succeeds and is reflected by both <see cref="RegisteredStateAreaPolicy.IsRegistered"/> and <see cref="RegisteredStateAreaPolicy.Count"/>.</summary>
    [Fact]
    public void TryRegister_NewArea_SucceedsAndIsReflected()
    {
        var policy = new RegisteredStateAreaPolicy();
        var areaId = new StateAreaId("example_area");

        bool result = policy.TryRegister(areaId);

        Assert.True(result);
        Assert.True(policy.IsRegistered(areaId));
        Assert.Equal(1, policy.Count);
    }

    /// <summary>Verifies that registering the same area twice succeeds without consuming a second slot.</summary>
    [Fact]
    public void TryRegister_AlreadyRegisteredArea_SucceedsWithoutConsumingCapacity()
    {
        var policy = new RegisteredStateAreaPolicy();
        var areaId = new StateAreaId("example_area");
        policy.TryRegister(areaId);

        bool result = policy.TryRegister(areaId);

        Assert.True(result);
        Assert.True(policy.IsRegistered(areaId));
        Assert.Equal(1, policy.Count);
    }

    /// <summary>Verifies that registering exactly up to the bound succeeds for every area.</summary>
    [Fact]
    public void TryRegister_UpToBound_AllSucceed()
    {
        var policy = new RegisteredStateAreaPolicy();

        for (int index = 0; index < Constants.MaxRegisteredStateAreas; index++)
        {
            Assert.True(policy.TryRegister(new StateAreaId($"area_{index}")));
        }

        Assert.Equal(Constants.MaxRegisteredStateAreas, policy.Count);
    }

    /// <summary>Verifies that a new, distinct area past the bound is rejected rather than evicting an existing one.</summary>
    [Fact]
    public void TryRegister_NewAreaPastBound_FailsWithoutEvictingExisting()
    {
        var policy = new RegisteredStateAreaPolicy();
        for (int index = 0; index < Constants.MaxRegisteredStateAreas; index++)
        {
            policy.TryRegister(new StateAreaId($"area_{index}"));
        }

        bool result = policy.TryRegister(new StateAreaId("one_too_many"));

        Assert.False(result);
        Assert.Equal(Constants.MaxRegisteredStateAreas, policy.Count);
        Assert.False(policy.IsRegistered(new StateAreaId("one_too_many")));
        Assert.True(policy.IsRegistered(new StateAreaId("area_0")));
    }

    /// <summary>Verifies that re-registering an already-registered area still succeeds even while the policy is at the bound.</summary>
    [Fact]
    public void TryRegister_AlreadyRegisteredAreaWhileAtBound_StillSucceeds()
    {
        var policy = new RegisteredStateAreaPolicy();
        for (int index = 0; index < Constants.MaxRegisteredStateAreas; index++)
        {
            policy.TryRegister(new StateAreaId($"area_{index}"));
        }

        bool result = policy.TryRegister(new StateAreaId("area_0"));

        Assert.True(result);
        Assert.True(policy.IsRegistered(new StateAreaId("area_0")));
        Assert.Equal(Constants.MaxRegisteredStateAreas, policy.Count);
    }
}
