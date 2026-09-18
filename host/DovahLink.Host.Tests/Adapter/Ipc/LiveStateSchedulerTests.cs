using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="LiveStateScheduler"/>.</summary>
public class LiveStateSchedulerTests
{
    /// <summary>A tiny interval map so tests run fast instead of waiting on production Fast/Medium cadences.</summary>
    private static readonly IReadOnlyDictionary<RateClass, TimeSpan> FastIntervals = new Dictionary<RateClass, TimeSpan>
    {
        [RateClass.Fast] = TimeSpan.FromMilliseconds(10),
        [RateClass.Medium] = TimeSpan.FromMilliseconds(25),
    };

    /// <summary>Verifies that a Fast-classed capture unit is sent repeatedly while an adapter is connected.</summary>
    [Fact]
    public async Task RunAsync_WhileConnected_RepeatedlySendsTheFastCaptureUnit()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream());
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(55));
        cancellation.Cancel();
        await run;

        Assert.True(connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2);
    }

    /// <summary>Verifies that the Medium-classed capture unit is sent on its own, slower cadence.</summary>
    [Fact]
    public async Task RunAsync_WhileConnected_SendsTheMediumCaptureUnitOnItsOwnCadence()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream());
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(55));
        cancellation.Cancel();
        await run;

        Assert.True(connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterXp) >= 1);
    }

    /// <summary>Verifies that only rate-classed capture units are ever sent -- never the event-sourced or baseline-only units, which the adapter's own resynchronization sequence handles instead.</summary>
    [Fact]
    public async Task RunAsync_WhileConnected_NeverSendsUnratedCaptureUnits()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream());
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(55));
        cancellation.Cancel();
        await run;

        Assert.DoesNotContain((uint)CharacterSampleToken.CharacterLevelBaseline, connection.ReadSampleCalls);
        Assert.Empty(connection.PairingDisplayCalls); // sanity: TrySendListenEvent is never wired through TrySendPairingDisplay
    }

    /// <summary>Verifies that no send is attempted while no adapter is connected.</summary>
    [Fact]
    public async Task RunAsync_WithNoConnection_SendsNothing()
    {
        FakeAdapterIpcListener listener = new() { CurrentConnection = null };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(30));
        cancellation.Cancel();
        await run;
    }

    /// <summary>Verifies that <see cref="LiveStateScheduler.RunAsync"/> completes once its token is cancelled, rather than hanging.</summary>
    [Fact]
    public async Task RunAsync_Cancelled_Completes()
    {
        FakeAdapterIpcListener listener = new() { CurrentConnection = null };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        cancellation.Cancel();

        Task completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(run, completed);
    }

    /// <summary>Verifies that sends start once a connection appears mid-run, having sent nothing before it did.</summary>
    [Fact]
    public async Task RunAsync_ConnectionAppearsMidRun_StartsSendingOnceAvailable()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true };
        FakeAdapterIpcListener listener = new() { CurrentConnection = null };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(30));
        Assert.Empty(connection.ReadSampleCalls);

        listener.CurrentConnection = connection;
        await Task.Delay(TimeSpan.FromMilliseconds(30));
        cancellation.Cancel();
        await run;

        Assert.NotEmpty(connection.ReadSampleCalls);
    }

    /// <summary>
    /// Verifies that sends stop once the active connection is cleared mid-run: at most one already
    /// in-flight tick (read <see cref="FakeAdapterIpcListener.CurrentConnection"/> as non-null a
    /// moment before it was cleared) may still land, but sends never keep arriving on every
    /// subsequent tick afterward.
    /// </summary>
    [Fact]
    public async Task RunAsync_ConnectionClearedMidRun_StopsSending()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true };
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(15));
        listener.CurrentConnection = null;
        int callsShortlyAfterClear = connection.ReadSampleCalls.Count;
        await Task.Delay(TimeSpan.FromMilliseconds(10));
        callsShortlyAfterClear = Math.Max(callsShortlyAfterClear, connection.ReadSampleCalls.Count);
        await Task.Delay(TimeSpan.FromMilliseconds(60));
        cancellation.Cancel();
        await run;

        Assert.Equal(callsShortlyAfterClear, connection.ReadSampleCalls.Count);
    }

    /// <summary>Verifies that a catalog with no rate-classed capture units completes immediately instead of hanging.</summary>
    [Fact]
    public async Task RunAsync_NoRateClassedCaptureUnits_CompletesImmediately()
    {
        LiveStateCatalog emptyCatalog = new(
            captureUnits: [new CaptureUnitDefinition(CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, RateClass: null, SynchronizationRole.PersistentEvent, [new StateAreaId(Constants.CharacterLevelStateArea)])],
            stateAreas: [new StateAreaDefinition(new StateAreaId(Constants.CharacterLevelStateArea), UpdateMode.Event)]);
        FakeAdapterIpcListener listener = new() { CurrentConnection = null };
        LiveStateScheduler scheduler = new(listener, emptyCatalog, new FakeLiveCaptureSink(), FastIntervals);

        Task completed = await Task.WhenAny(scheduler.RunAsync(CancellationToken.None), Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(completed.IsCompletedSuccessfully);
    }

    // ---- One-in-flight outstanding-request tracking ----

    /// <summary>Verifies that a unit with an outstanding, unanswered request skips every subsequent tick rather than sending a second, overlapping request.</summary>
    [Fact]
    public async Task RunAsync_RequestOutstanding_SkipsSubsequentTicksUntilReleased()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream())
        {
            TrySendReadSampleResult = true,
            TrySendReadSampleCorrelationId = 42,
            ConnectionGeneration = 1,
        };
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        // Three Fast ticks' worth of time, well under the five-tick timeout budget: only the
        // first tick's send should ever land, since every later tick finds the slot still
        // outstanding with no reply ever reported.
        await Task.Delay(TimeSpan.FromMilliseconds(35));
        cancellation.Cancel();
        await run;

        Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
    }

    /// <summary>Verifies that a matching capture result immediately releases the outstanding slot, letting the very next tick send a fresh request instead of waiting out the timeout.</summary>
    [Fact]
    public async Task RunAsync_MatchingCaptureResultReceived_ReleasesSlotForNextTick()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream())
        {
            TrySendReadSampleResult = true,
            TrySendReadSampleCorrelationId = 42,
            ConnectionGeneration = 1,
        };
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        var liveCaptureSink = new FakeLiveCaptureSink { ConnectionGeneration = 1 };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

        liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
            42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []));

        // Released immediately: the next Fast tick (well before the five-tick timeout would have
        // released it on its own) already sends again.
        await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2, run);
        cancellation.Cancel();
        await run;
    }

    /// <summary>Verifies that a result carrying a stale connection generation never releases the current slot, even though its sample token and correlation id both match.</summary>
    [Fact]
    public async Task RunAsync_ResultFromOlderConnectionGeneration_DoesNotReleaseSlot()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream())
        {
            TrySendReadSampleResult = true,
            TrySendReadSampleCorrelationId = 42,
            ConnectionGeneration = 2,
        };
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        var liveCaptureSink = new FakeLiveCaptureSink { ConnectionGeneration = 1 }; // stale: the slot was sent under generation 2
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

        liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
            42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []));
        await Task.Delay(TimeSpan.FromMilliseconds(35)); // well under the timeout budget

        cancellation.Cancel();
        await run;
        Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
    }

    /// <summary>
    /// Verifies that a Sample-source result for a token this scheduler does not poll (for example
    /// the level baseline sample, which is Sample-sourced but not rate-classed, so it has no
    /// outstanding-slot entry at all) is a harmless no-op that never touches an unrelated unit's slot.
    /// </summary>
    [Fact]
    public async Task RunAsync_SampleResultForNonRateClassedToken_IsHarmlessNoOp()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream())
        {
            TrySendReadSampleResult = true,
            TrySendReadSampleCorrelationId = 42,
            ConnectionGeneration = 1,
        };
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        var liveCaptureSink = new FakeLiveCaptureSink { ConnectionGeneration = 1 };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

        Exception? exception = Record.Exception(() => liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, default, [0, 1])));
        Assert.Null(exception);
        await Task.Delay(TimeSpan.FromMilliseconds(35)); // well under the timeout budget

        cancellation.Cancel();
        await run;
        Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
    }

    /// <summary>Verifies that a result carrying a foreign correlation id never releases the current slot, even though its sample token and connection generation both match.</summary>
    [Fact]
    public async Task RunAsync_ResultWithMismatchedCorrelationId_DoesNotReleaseSlot()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream())
        {
            TrySendReadSampleResult = true,
            TrySendReadSampleCorrelationId = 42,
            ConnectionGeneration = 1,
        };
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        var liveCaptureSink = new FakeLiveCaptureSink { ConnectionGeneration = 1 };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

        liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
            999, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []));
        await Task.Delay(TimeSpan.FromMilliseconds(35)); // well under the timeout budget

        cancellation.Cancel();
        await run;
        Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
    }

    /// <summary>
    /// Verifies that a result reported for an Event-sourced key never releases a Sample unit's slot,
    /// even when the raw key values happen to collide (<see cref="CharacterSampleToken.CharacterVitals"/>
    /// and <see cref="CharacterEventKey.CharacterLevelChanged"/> both share the raw value 1).
    /// </summary>
    [Fact]
    public async Task RunAsync_EventResultWithCollidingRawKey_DoesNotReleaseSampleSlot()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream())
        {
            TrySendReadSampleResult = true,
            TrySendReadSampleCorrelationId = 42,
            ConnectionGeneration = 1,
        };
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        var liveCaptureSink = new FakeLiveCaptureSink { ConnectionGeneration = 1 };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

        liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
            42, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, CaptureAvailability.Available, default, [0, 1]));
        await Task.Delay(TimeSpan.FromMilliseconds(35)); // well under the timeout budget

        cancellation.Cancel();
        await run;
        Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
    }

    /// <summary>Verifies that an outstanding slot with no reply times out, best-effort cancels the stale correlation, and releases for exactly the next tick -- never a burst of catch-up sends.</summary>
    [Fact]
    public async Task RunAsync_NoReplyWithinTimeout_CancelsAndReleasesForNextTickOnly()
    {
        FakeAdapterIpcConnection connection = new(new MemoryStream())
        {
            TrySendReadSampleResult = true,
            TrySendReadSampleCorrelationId = 42,
            ConnectionGeneration = 1,
        };
        FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), FastIntervals);
        using CancellationTokenSource cancellation = new();

        Task run = scheduler.RunAsync(cancellation.Token);
        // Five ticks (the timeout budget) plus margin, with no reply ever reported.
        await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2, run);
        int countJustAfterRetry = connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals);
        await Task.Delay(TimeSpan.FromMilliseconds(15)); // one more tick's worth: must not burst past the single retry

        cancellation.Cancel();
        await run;

        Assert.Equal(countJustAfterRetry, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        Assert.Contains(42UL, connection.CancelCalls);
    }

    /// <summary>
    /// Polls a condition until it becomes true, failing the test if it never does within a bounded
    /// time. If <paramref name="guardTask"/> completes first, awaits it so a fault in the scheduler's
    /// own run loop surfaces directly instead of being masked by a confusing timeout failure.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, Task guardTask)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (guardTask.IsCompleted)
            {
                await guardTask;
            }

            Assert.True(DateTime.UtcNow < deadline, "Condition was not met within the expected time.");
            await Task.Delay(2);
        }
    }
}
