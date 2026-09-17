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
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, FastIntervals);
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
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, FastIntervals);
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
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, FastIntervals);
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
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, FastIntervals);
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
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, FastIntervals);
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
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, FastIntervals);
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
        LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, FastIntervals);
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
        LiveStateScheduler scheduler = new(listener, emptyCatalog, FastIntervals);

        Task completed = await Task.WhenAny(scheduler.RunAsync(CancellationToken.None), Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(completed.IsCompletedSuccessfully);
    }
}
