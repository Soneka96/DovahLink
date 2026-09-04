using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;
using DovahLink.Host.Trust;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="TrustResetService"/>.</summary>
public class TrustResetServiceTests
{
    /// <summary>Verifies that confirming the correct, unexpired code deletes every known device and invalidates every session.</summary>
    [Fact]
    public async Task ConfirmResetAsync_CorrectCode_ResetsAllDevicesAndInvalidatesAllSessions()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        var pairingCoordinator = new FakePairingCoordinator();
        var clock = new FakeClock();
        ClientId firstClient = ClientId.NewId();
        ClientId secondClient = ClientId.NewId();
        trustStore.Seed(new TrustRecord(firstClient, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", clock.UtcNow));
        trustStore.Seed(new TrustRecord(secondClient, "CD34", "Bedroom Tablet", KnownDeviceState.Trusted, "beefdead", clock.UtcNow));
        SessionId firstSession = sessionRegistry.Create(firstClient);
        SessionId secondSession = sessionRegistry.Create(secondClient);
        var service = new TrustResetService(trustStore, Invalidator(sessionRegistry), pairingCoordinator, clock);
        FactoryResetChallenge challenge = service.BeginReset();

        bool result = await service.ConfirmResetAsync(challenge.Code);

        Assert.True(result);
        Assert.Null(trustStore.TryGet(firstClient));
        Assert.Null(trustStore.TryGet(secondClient));
        Assert.Equal(1, trustStore.ClearCallCount);
        Assert.Equal(1, pairingCoordinator.CancelAllCallCount);
        Assert.False(sessionRegistry.IsActive(firstSession, sessionRegistry.ConnectionIdFor(firstSession)));
        Assert.False(sessionRegistry.IsActive(secondSession, sessionRegistry.ConnectionIdFor(secondSession)));
        Assert.Equal(1, sessionRegistry.InvalidateAllCallCount);
    }

    /// <summary>Verifies that confirming with the wrong code rejects the reset and leaves trust and sessions untouched.</summary>
    [Fact]
    public async Task ConfirmResetAsync_WrongCode_RejectsAndLeavesStateUntouched()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        var pairingCoordinator = new FakePairingCoordinator();
        var clock = new FakeClock();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", clock.UtcNow));
        var service = new TrustResetService(trustStore, Invalidator(sessionRegistry), pairingCoordinator, clock);
        FactoryResetChallenge challenge = service.BeginReset();

        bool result = await service.ConfirmResetAsync("wrong-code");

        Assert.False(result);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
        Assert.Equal(0, sessionRegistry.InvalidateAllCallCount);
        Assert.Equal(0, pairingCoordinator.CancelAllCallCount);

        Assert.False(await service.ConfirmResetAsync(challenge.Code));
    }

    /// <summary>Verifies that confirming after the 60-second challenge lifetime rejects the reset.</summary>
    [Fact]
    public async Task ConfirmResetAsync_ExpiredChallenge_Rejects()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        var clock = new FakeClock();
        var service = new TrustResetService(trustStore, Invalidator(sessionRegistry), new FakePairingCoordinator(), clock);
        FactoryResetChallenge challenge = service.BeginReset();

        clock.Advance(TimeSpan.FromSeconds(61));
        bool result = await service.ConfirmResetAsync(challenge.Code);

        Assert.False(result);
        Assert.Equal(0, sessionRegistry.InvalidateAllCallCount);
    }

    /// <summary>Verifies that a factory-reset challenge is exactly six decimal digits and lasts 60 seconds.</summary>
    [Fact]
    public void BeginReset_UsesSixDigitCodeAndSixtySecondLifetime()
    {
        var clock = new FakeClock();
        var service = new TrustResetService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator(), clock);

        FactoryResetChallenge challenge = service.BeginReset();

        Assert.Equal(6, challenge.Code.Length);
        Assert.All(challenge.Code, character => Assert.InRange(character, '0', '9'));
        Assert.Equal(TimeSpan.FromSeconds(60), challenge.ExpiresAtUtc - clock.UtcNow);
    }

    /// <summary>Verifies that confirming with no challenge ever having been issued is rejected rather than throwing.</summary>
    [Fact]
    public async Task ConfirmResetAsync_NoChallengeIssued_Rejects()
    {
        var service = new TrustResetService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator(), new FakeClock());

        bool result = await service.ConfirmResetAsync("anything");

        Assert.False(result);
    }

    /// <summary>Verifies that a challenge can only be confirmed once; a repeat attempt with the same code fails.</summary>
    [Fact]
    public async Task ConfirmResetAsync_CalledTwiceWithSameCode_SecondCallRejects()
    {
        var trustStore = new FakeTrustStore();
        var service = new TrustResetService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator(), new FakeClock());
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
        var service = new TrustResetService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator(), new FakeClock());
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
        var service = new TrustResetService(new FakeTrustStore(), Invalidator(sessionRegistry), new FakePairingCoordinator(), new FakeClock());
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
        var service = new TrustResetService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator(), clock);
        FactoryResetChallenge challenge = service.BeginReset();

        clock.UtcNow = challenge.ExpiresAtUtc;
        bool result = await service.ConfirmResetAsync(challenge.Code);

        Assert.True(result);
    }

    /// <summary>Verifies that a null code is rejected explicitly rather than crashing inside the encoding/comparison logic.</summary>
    [Fact]
    public async Task ConfirmResetAsync_NullCode_ThrowsArgumentNullException()
    {
        var service = new TrustResetService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator(), new FakeClock());
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
        var pairingCoordinator = new FakePairingCoordinator();
        var service = new TrustResetService(trustStore, Invalidator(sessionRegistry), pairingCoordinator, new FakeClock());
        FactoryResetChallenge challenge = service.BeginReset();

        trustStore.ThrowOnClear = new IOException("disk full");
        await Assert.ThrowsAsync<IOException>(() => service.ConfirmResetAsync(challenge.Code));
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
        Assert.Equal(0, sessionRegistry.InvalidateAllCallCount);
        Assert.Equal(0, pairingCoordinator.CancelAllCallCount);

        trustStore.ThrowOnClear = null;
        bool retryResult = await service.ConfirmResetAsync(challenge.Code);

        Assert.True(retryResult);
        Assert.Null(trustStore.TryGet(clientId));
        Assert.Equal(1, pairingCoordinator.CancelAllCallCount);
        Assert.Equal(1, sessionRegistry.InvalidateAllCallCount);
    }

    /// <summary>Verifies that concurrent confirmation attempts can execute the reset only once.</summary>
    [Fact]
    public async Task ConfirmResetAsync_ConcurrentConfirmations_OnlyOneSucceeds()
    {
        var trustStore = new FakeTrustStore();
        var enteredClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        trustStore.BeforeClear = async () =>
        {
            enteredClear.SetResult();
            await releaseClear.Task;
        };
        var service = new TrustResetService(
            trustStore,
            Invalidator(new FakeSessionRegistry()),
            new FakePairingCoordinator(),
            new FakeClock());
        FactoryResetChallenge challenge = service.BeginReset();

        Task<bool> first = service.ConfirmResetAsync(challenge.Code);
        await enteredClear.Task;
        Task<bool> second = service.ConfirmResetAsync(challenge.Code);

        releaseClear.SetResult();
        bool[] results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
        Assert.Equal(1, trustStore.ClearCallCount);
    }

    /// <summary>
    /// Proves the confirmed Factory Reset replacement race is closed: once an in-flight
    /// <see cref="TrustResetService.ConfirmResetAsync"/> has irrevocably claimed the active challenge
    /// and moved on to its destructive work, a concurrent <see cref="TrustResetService.BeginReset"/>
    /// must not silently replace it -- the claimed challenge still completes the reset exactly once,
    /// and the freshly issued challenge from the racing <c>BeginReset</c> never becomes usable.
    /// </summary>
    [Fact]
    public async Task BeginReset_DuringInFlightConfirm_DoesNotReplaceClaimedChallengeAndConfirmStillSucceeds()
    {
        var trustStore = new FakeTrustStore();
        var enteredClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        trustStore.BeforeClear = async () =>
        {
            enteredClear.SetResult();
            await releaseClear.Task;
        };
        var pairingCoordinator = new FakePairingCoordinator();
        var service = new TrustResetService(trustStore, Invalidator(new FakeSessionRegistry()), pairingCoordinator, new FakeClock());
        FactoryResetChallenge claimedChallenge = service.BeginReset();

        Task<bool> confirm = service.ConfirmResetAsync(claimedChallenge.Code);
        await enteredClear.Task;

        // Races a fresh BeginReset while the confirm above already holds the claim.
        FactoryResetChallenge replacementChallenge = service.BeginReset();

        releaseClear.SetResult();
        bool confirmResult = await confirm;

        Assert.True(confirmResult);
        Assert.Equal(1, trustStore.ClearCallCount);
        Assert.Equal(1, pairingCoordinator.CancelAllCallCount);
        // The replacement challenge from the racing BeginReset never became the active one, so it
        // cannot itself be confirmed.
        Assert.False(await service.ConfirmResetAsync(replacementChallenge.Code));
    }

    /// <summary>
    /// Verifies the exact security-mandated ordering for a confirmed Factory Reset: the trust store is
    /// cleared before every session becomes unauthorized in the registry, before pairing is cancelled,
    /// before its best-effort terminal notification carries the exact <c>factory_reset</c> reason --
    /// per <c>ai/context/protocol/security.md</c>'s "authoritative state change, credential
    /// invalidation where applicable, future authentication/pairing enforcement, ... then forced
    /// close" ordering. Session invalidation is placed immediately after the Clear, ahead of pairing
    /// cancellation, to minimize the post-mutation window in which a concurrent request could still
    /// find <see cref="ISessionRegistry.IsActive"/> true.
    /// </summary>
    [Fact]
    public async Task ConfirmResetAsync_CorrectCode_AppliesSideEffectsInTheMandatedOrder()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var sessionRegistry = new FakeSessionRegistry { OnMutationApplied = order.Add };
        var pairingCoordinator = new FakePairingCoordinator { OnMutationApplied = order.Add };
        var notifier = new FakeSessionTerminationNotifier { OnNotify = target => order.Add($"Notified:{target.Reason}") };
        var clock = new FakeClock();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", clock.UtcNow));
        sessionRegistry.Create(clientId);
        var service = new TrustResetService(trustStore, new ClientSessionInvalidator(sessionRegistry, notifier), pairingCoordinator, clock);
        FactoryResetChallenge challenge = service.BeginReset();

        Assert.True(await service.ConfirmResetAsync(challenge.Code));

        Assert.Equal(["Clear", "InvalidateAll", "Notified:FactoryReset", "CancelAll"], order);
    }

    /// <summary>
    /// Wraps <paramref name="sessionRegistry"/> in a real <see cref="ClientSessionInvalidator"/> with a
    /// discardable notifier, for tests that only need actual session removal proven -- not the
    /// notifier itself, which the ordering test wires up explicitly where it matters.
    /// </summary>
    private static IClientSessionInvalidator Invalidator(FakeSessionRegistry sessionRegistry) =>
        new ClientSessionInvalidator(sessionRegistry, new FakeSessionTerminationNotifier());
}
