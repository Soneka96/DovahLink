using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;
using DovahLink.Host.Trust;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="TrustResetService"/>.</summary>
public class TrustResetServiceTests
{
    /// <summary>Verifies that confirming the correct, unexpired code resets every known device and invalidates every session.</summary>
    [Fact]
    public async Task ConfirmResetAsync_CorrectCode_ResetsAllDevicesAndInvalidatesAllSessions()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        var clock = new FakeClock();
        ClientId firstClient = ClientId.NewId();
        ClientId secondClient = ClientId.NewId();
        trustStore.Seed(new TrustRecord(firstClient, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", clock.UtcNow));
        trustStore.Seed(new TrustRecord(secondClient, "CD34", "Bedroom Tablet", KnownDeviceState.Trusted, "beefdead", clock.UtcNow));
        SessionId firstSession = sessionRegistry.Create(firstClient);
        SessionId secondSession = sessionRegistry.Create(secondClient);
        var service = new TrustResetService(trustStore, sessionRegistry, clock);
        FactoryResetChallenge challenge = service.BeginReset();

        bool result = await service.ConfirmResetAsync(challenge.Code);

        Assert.True(result);
        Assert.Equal(KnownDeviceState.Unpaired, trustStore.TryGet(firstClient)!.State);
        Assert.Equal(KnownDeviceState.Unpaired, trustStore.TryGet(secondClient)!.State);
        Assert.False(sessionRegistry.IsActive(firstSession));
        Assert.False(sessionRegistry.IsActive(secondSession));
        Assert.Equal(1, sessionRegistry.InvalidateAllCallCount);
    }

    /// <summary>Verifies that confirming with the wrong code rejects the reset and leaves trust and sessions untouched.</summary>
    [Fact]
    public async Task ConfirmResetAsync_WrongCode_RejectsAndLeavesStateUntouched()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        var clock = new FakeClock();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", clock.UtcNow));
        var service = new TrustResetService(trustStore, sessionRegistry, clock);
        service.BeginReset();

        bool result = await service.ConfirmResetAsync("wrong-code");

        Assert.False(result);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
        Assert.Equal(0, sessionRegistry.InvalidateAllCallCount);
    }

    /// <summary>Verifies that confirming after the challenge has expired rejects the reset.</summary>
    [Fact]
    public async Task ConfirmResetAsync_ExpiredChallenge_Rejects()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        var clock = new FakeClock();
        var service = new TrustResetService(trustStore, sessionRegistry, clock);
        FactoryResetChallenge challenge = service.BeginReset();

        clock.Advance(TimeSpan.FromMinutes(6));
        bool result = await service.ConfirmResetAsync(challenge.Code);

        Assert.False(result);
        Assert.Equal(0, sessionRegistry.InvalidateAllCallCount);
    }

    /// <summary>Verifies that confirming with no challenge ever having been issued is rejected rather than throwing.</summary>
    [Fact]
    public async Task ConfirmResetAsync_NoChallengeIssued_Rejects()
    {
        var service = new TrustResetService(new FakeTrustStore(), new FakeSessionRegistry(), new FakeClock());

        bool result = await service.ConfirmResetAsync("anything");

        Assert.False(result);
    }

    /// <summary>Verifies that a challenge can only be confirmed once; a repeat attempt with the same code fails.</summary>
    [Fact]
    public async Task ConfirmResetAsync_CalledTwiceWithSameCode_SecondCallRejects()
    {
        var trustStore = new FakeTrustStore();
        var service = new TrustResetService(trustStore, new FakeSessionRegistry(), new FakeClock());
        FactoryResetChallenge challenge = service.BeginReset();

        bool first = await service.ConfirmResetAsync(challenge.Code);
        bool second = await service.ConfirmResetAsync(challenge.Code);

        Assert.True(first);
        Assert.False(second);
    }

    /// <summary>Verifies that beginning a new reset replaces a still-active prior challenge, invalidating its code.</summary>
    [Fact]
    public async Task BeginReset_CalledAgain_InvalidatesThePriorChallengesCode()
    {
        var service = new TrustResetService(new FakeTrustStore(), new FakeSessionRegistry(), new FakeClock());
        FactoryResetChallenge firstChallenge = service.BeginReset();
        service.BeginReset();

        bool result = await service.ConfirmResetAsync(firstChallenge.Code);

        Assert.False(result);
    }

    /// <summary>Verifies that a reset with no known devices still succeeds and still invalidates sessions.</summary>
    [Fact]
    public async Task ConfirmResetAsync_NoKnownDevices_StillSucceedsAndInvalidatesSessions()
    {
        var sessionRegistry = new FakeSessionRegistry();
        var service = new TrustResetService(new FakeTrustStore(), sessionRegistry, new FakeClock());
        FactoryResetChallenge challenge = service.BeginReset();

        bool result = await service.ConfirmResetAsync(challenge.Code);

        Assert.True(result);
        Assert.Equal(1, sessionRegistry.InvalidateAllCallCount);
    }

    /// <summary>Verifies that a challenge confirmed at the exact moment it expires is still accepted (expiry is exclusive).</summary>
    [Fact]
    public async Task ConfirmResetAsync_AtExactExpiryMoment_StillAccepted()
    {
        var clock = new FakeClock();
        var service = new TrustResetService(new FakeTrustStore(), new FakeSessionRegistry(), clock);
        FactoryResetChallenge challenge = service.BeginReset();

        clock.UtcNow = challenge.ExpiresAtUtc;
        bool result = await service.ConfirmResetAsync(challenge.Code);

        Assert.True(result);
    }

    /// <summary>Verifies that a null code is rejected explicitly rather than crashing inside the encoding/comparison logic.</summary>
    [Fact]
    public async Task ConfirmResetAsync_NullCode_ThrowsArgumentNullException()
    {
        var service = new TrustResetService(new FakeTrustStore(), new FakeSessionRegistry(), new FakeClock());
        service.BeginReset();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ConfirmResetAsync(null!));
    }

    /// <summary>
    /// Verifies that a reset which fails partway through can be retried with the same code, and
    /// that the retry completes the devices the first attempt didn't reach.
    /// </summary>
    [Fact]
    public async Task ConfirmResetAsync_RetryAfterPartialFailure_CompletesOnSecondAttempt()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var service = new TrustResetService(trustStore, sessionRegistry, new FakeClock());
        FactoryResetChallenge challenge = service.BeginReset();

        trustStore.ThrowOnUpsert = new IOException("disk full");
        await Assert.ThrowsAsync<IOException>(() => service.ConfirmResetAsync(challenge.Code));
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
        Assert.Equal(0, sessionRegistry.InvalidateAllCallCount);

        trustStore.ThrowOnUpsert = null;
        bool retryResult = await service.ConfirmResetAsync(challenge.Code);

        Assert.True(retryResult);
        Assert.Equal(KnownDeviceState.Unpaired, trustStore.TryGet(clientId)!.State);
        Assert.Equal(1, sessionRegistry.InvalidateAllCallCount);
    }
}
