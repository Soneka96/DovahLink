namespace DovahLink.Host.Tests.Adapter.Ipc
{
    using DovahLink.Host.Adapter;
    using DovahLink.Host.Adapter.Ipc;
    using DovahLink.Host.Identity;
    using DovahLink.Host.PlayContext;
    using DovahLink.Host.State;
    using DovahLink.Host.Tests.TestDoubles;

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
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
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
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
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
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
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
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            cancellation.Cancel();
            await run;
        }

        /// <summary>Verifies that pending resynchronization suppresses both Fast and Medium ordinary samples.</summary>
        [Fact]
        public async Task RunAsync_ResynchronizationPending_SuppressesFastAndMediumSamples()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildPendingAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(70));
            cancellation.Cancel();
            await run;

            Assert.Empty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that a play-context re-arm which commits before final queue admission rejects a sample prepared from an older snapshot.</summary>
        [Fact]
        public async Task RunAsync_RearmWinsWhileSampleIsPrepared_DoesNotEnqueueSample()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                ConnectionGeneration = 1,
            };
            using ManualResetEventSlim releasePreparation = new();
            TaskCompletionSource preparationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int blockedPreparations = 0;
            connection.OnPrepareReadSample = sampleToken =>
            {
                if (sampleToken == (uint)CharacterSampleToken.CharacterVitals && Interlocked.Exchange(ref blockedPreparations, 1) == 0)
                {
                    preparationStarted.TrySetResult();
                    releasePreparation.Wait();
                }
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker tracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, SingleVitalsCatalog(), new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), tracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            try
            {
                await preparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                tracker.RearmResynchronizationForPlayContextTransition();
            }
            finally
            {
                releasePreparation.Set();
                cancellation.Cancel();
                await run;
            }

            Assert.Empty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that a generation change after preparation rejects the old connection's sample and polling resumes on the resynchronized generation.</summary>
        [Fact]
        public async Task RunAsync_ConnectionGenerationChangesWhileSampleIsPrepared_RejectsOldConnection()
        {
            FakeAdapterIpcConnection oldConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                ConnectionGeneration = 1,
            };
            using ManualResetEventSlim releasePreparation = new();
            TaskCompletionSource preparationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int blockedPreparations = 0;
            oldConnection.OnPrepareReadSample = sampleToken =>
            {
                if (sampleToken == (uint)CharacterSampleToken.CharacterVitals && Interlocked.Exchange(ref blockedPreparations, 1) == 0)
                {
                    preparationStarted.TrySetResult();
                    releasePreparation.Wait();
                }
            };
            FakeAdapterIpcConnection newConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                ConnectionGeneration = 2,
            };
            TaskCompletionSource newConnectionPreparationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            newConnection.OnPrepareReadSample = _ => newConnectionPreparationStarted.TrySetResult();
            FakeAdapterIpcListener listener = new() { CurrentConnection = oldConnection };
            AdapterAvailabilityTracker tracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, SingleVitalsCatalog(), new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), tracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            try
            {
                await preparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                AdapterAvailabilitySnapshot oldSnapshot = tracker.GetSnapshot();
                AdapterInstanceId oldInstanceId = oldSnapshot.CurrentInstanceId
                    ?? throw new InvalidOperationException("The connected Adapter identity is missing.");
                Assert.NotNull(tracker.CommitDisconnected(oldInstanceId, oldSnapshot.ConnectionGeneration));
                Assert.NotNull(tracker.CommitConnected(AdapterInstanceId.NewId(), 2));
                listener.CurrentConnection = newConnection;
                CompleteAdapterResynchronization(tracker);
                Assert.False(tracker.NeedsResynchronization);
                releasePreparation.Set();
                await newConnectionPreparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitUntilAsync(() => newConnection.ReadSampleCalls.Count > 0, run);
            }
            finally
            {
                releasePreparation.Set();
                cancellation.Cancel();
                await run;
            }

            Assert.Empty(oldConnection.ReadSampleCalls);
            Assert.NotEmpty(newConnection.ReadSampleCalls);
        }

        /// <summary>Verifies that an admitted send holds the tracker gate until queue admission and slot registration finish, so a re-arm commits afterward.</summary>
        [Fact]
        public async Task RunAsync_SendAdmissionWinsAgainstRearm_RearmWaitsForAdmission()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            using ManualResetEventSlim releaseAdmission = new();
            TaskCompletionSource admissionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource rearmStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int blockedAdmissions = 0;
            connection.OnTrySendReadSample = () =>
            {
                if (Interlocked.Exchange(ref blockedAdmissions, 1) == 0)
                {
                    admissionStarted.TrySetResult();
                    releaseAdmission.Wait();
                }
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker tracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, SingleVitalsCatalog(), new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), tracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            Task? rearm = null;
            try
            {
                await admissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                rearm = Task.Run(() =>
                {
                    rearmStarted.TrySetResult();
                    tracker.RearmResynchronizationForPlayContextTransition();
                });
                await rearmStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(TimeSpan.FromMilliseconds(20));
                Assert.False(rearm.IsCompleted);
            }
            finally
            {
                releaseAdmission.Set();
                if (rearm is not null)
                {
                    await rearm;
                }

                cancellation.Cancel();
                await run;
            }

            Assert.Equal([(uint)CharacterSampleToken.CharacterVitals], connection.ReadSampleCalls);
            Assert.True(tracker.NeedsResynchronization);
        }

        /// <summary>Verifies that an unavailable Adapter suppresses ordinary samples with an active play context.</summary>
        [Fact]
        public async Task RunAsync_AdapterUnavailable_SendsNothing()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = new();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(70));
            cancellation.Cancel();
            await run;

            Assert.Empty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that Fast and Medium polling resume after successful resynchronization.</summary>
        [Fact]
        public async Task RunAsync_ResynchronizationCompleted_ResumesFastAndMediumSamples()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildPendingAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(70));
            Assert.Empty(connection.ReadSampleCalls);

            CompleteAdapterResynchronization(adapterAvailabilityTracker);
            await WaitUntilAsync(
                () => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals)
                    && connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterXp),
                run);
            cancellation.Cancel();
            await run;

            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterXp));
        }

        /// <summary>Verifies that a long resynchronization pause resumes without replaying missed Fast or Medium ticks.</summary>
        [Fact]
        public async Task RunAsync_LongResynchronization_DoesNotCatchUpBurst()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildPendingAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(275));
            Assert.Empty(connection.ReadSampleCalls);

            CompleteAdapterResynchronization(adapterAvailabilityTracker);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 1, run);
            await Task.Delay(TimeSpan.FromMilliseconds(15));
            cancellation.Cancel();
            await run;

            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
            Assert.InRange(connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterXp), 0, 1);
        }

        /// <summary>Verifies that resynchronization leaves a pre-existing ordinary request outstanding and sends no replacement.</summary>
        [Fact]
        public async Task RunAsync_ResynchronizationStartsWithOutstandingRequest_PreservesItsSlot()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            FakeLiveCaptureSink liveCaptureSink = new();
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);
            adapterAvailabilityTracker.RearmResynchronizationForPlayContextTransition();
            await Task.Delay(TimeSpan.FromMilliseconds(120));

            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
            Assert.DoesNotContain(42UL, connection.CancelCalls);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
            await Task.Delay(TimeSpan.FromMilliseconds(60));

            cancellation.Cancel();
            await run;
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>Verifies that a pre-resynchronization request can time out without sending its retry until resynchronization completes.</summary>
        [Fact]
        public async Task RunAsync_ResynchronizationPending_ProcessesOutstandingTimeoutWithoutRetry()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);
            adapterAvailabilityTracker.RearmResynchronizationForPlayContextTransition();

            await WaitUntilAsync(() => connection.CancelCalls.Contains(42UL), run);
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));

            CompleteAdapterResynchronization(adapterAvailabilityTracker);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) > 1, run);
            cancellation.Cancel();
            await run;

            Assert.Equal(2, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>Verifies that no send is attempted while no play context is active, even with a connection ready to accept one.</summary>
        [Fact]
        public async Task RunAsync_NoActivePlayContext_SendsNothing()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), new FakePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(55));
            cancellation.Cancel();
            await run;

            Assert.Empty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that sends start once a play context becomes active mid-run, having sent nothing before it did.</summary>
        [Fact]
        public async Task RunAsync_PlayContextBecomesActiveMidRun_StartsSendingOnceEstablished()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var playContextTracker = new FakePlayContextTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), playContextTracker, BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            Assert.Empty(connection.ReadSampleCalls);

            playContextTracker.NotifyTransition(new PlayContextId(Guid.NewGuid()));
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            cancellation.Cancel();
            await run;

            Assert.NotEmpty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that <see cref="LiveStateScheduler.RunAsync"/> completes once its token is cancelled, rather than hanging.</summary>
        [Fact]
        public async Task RunAsync_Cancelled_Completes()
        {
            FakeAdapterIpcListener listener = new() { CurrentConnection = null };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
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
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = null };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
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
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
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
            LiveStateScheduler scheduler = new(listener, emptyCatalog, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);

            Task completed = await Task.WhenAny(scheduler.RunAsync(CancellationToken.None), Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(completed.IsCompletedSuccessfully);
        }

        // ---- One-in-flight outstanding-request tracking ----

        /// <summary>
        /// A larger interval map for the outstanding-slot/timeout tests below, so their timing margins
        /// comfortably tolerate scheduler jitter under a loaded test run instead of racing a tight window
        /// against <see cref="Constants.LiveStateSampleTimeoutTicks"/> ticks of <see cref="FastIntervals"/>'
        /// much shorter cadence. Unrelated cadence tests above keep using <see cref="FastIntervals"/>, since
        /// they only need to prove a send happened at all, not race a fixed timeout window.
        /// </summary>
        private static readonly IReadOnlyDictionary<RateClass, TimeSpan> SlotIntervals = new Dictionary<RateClass, TimeSpan>
        {
            [RateClass.Fast] = TimeSpan.FromMilliseconds(50),
            [RateClass.Medium] = TimeSpan.FromMilliseconds(100),
        };

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
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            // About two Fast ticks' worth of time, comfortably under the five-tick (250ms) timeout
            // budget: only the first tick's send should ever land, since every later tick finds the
            // slot still outstanding with no reply ever reported.
            await Task.Delay(TimeSpan.FromMilliseconds(120));
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
            var liveCaptureSink = new FakeLiveCaptureSink();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));

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
            var liveCaptureSink = new FakeLiveCaptureSink(); // stale: the slot was sent under generation 2
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(2), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one tick, comfortably under the five-tick (250ms) timeout budget

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
            var liveCaptureSink = new FakeLiveCaptureSink();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            Exception? exception = Record.Exception(() => liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, default, [0, 1]), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1)));
            Assert.Null(exception);
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one tick, comfortably under the five-tick (250ms) timeout budget

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
            var liveCaptureSink = new FakeLiveCaptureSink();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                999, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one tick, comfortably under the five-tick (250ms) timeout budget

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
            var liveCaptureSink = new FakeLiveCaptureSink();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                42, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, CaptureAvailability.Available, default, [0, 1]), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one tick, comfortably under the five-tick (250ms) timeout budget

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
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            // Five ticks (the timeout budget) plus margin, with no reply ever reported.
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2, run);
            int countJustAfterRetry = connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals);
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one more tick's worth: must not burst past the single retry

            cancellation.Cancel();
            await run;

            Assert.Equal(countJustAfterRetry, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
            Assert.Contains(42UL, connection.CancelCalls);
        }

        /// <summary>
        /// Verifies that a timed-out slot's best-effort cancel is never sent on a different connection
        /// generation than the one the timed-out request was actually sent on: a reconnect between the
        /// send and the timeout leaves the listener's current connection on a newer generation whose own
        /// correlation ids restart from the same small integers, so cancelling on it with the stale id
        /// could otherwise hit an unrelated request that connection genuinely has outstanding.
        /// </summary>
        [Fact]
        public async Task RunAsync_NoReplyWithinTimeoutButConnectionGenerationChanged_NeverCancelsOnTheNewerGeneration()
        {
            FakeAdapterIpcConnection originalConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = originalConnection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => originalConnection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            // A reconnect: a new connection object reusing the same small correlation id, on a newer
            // generation, before the original request's own timeout has elapsed.
            FakeAdapterIpcConnection newConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 2,
            };
            listener.CurrentConnection = newConnection;

            // Comfortably past the original request's five-tick (250ms) timeout, but short of a second one.
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            cancellation.Cancel();
            await run;

            Assert.DoesNotContain(42UL, newConnection.CancelCalls);
        }

        /// <summary>
        /// Verifies that a timed-out slot whose connection was cleared entirely -- not merely replaced by
        /// a newer generation -- before the timeout elapsed never throws attempting its best-effort
        /// cancel, and still resumes sending normally once a connection becomes available again.
        /// </summary>
        [Fact]
        public async Task RunAsync_NoReplyWithinTimeoutAndConnectionClearedEntirely_DoesNotThrowAndResumesOnceReconnected()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            // Cleared entirely, not merely replaced, before the outstanding request's own timeout elapses.
            listener.CurrentConnection = null;
            AdapterAvailabilitySnapshot disconnectedSnapshot = adapterAvailabilityTracker.GetSnapshot();
            AdapterInstanceId disconnectedInstanceId = disconnectedSnapshot.CurrentInstanceId
                ?? throw new InvalidOperationException("The connected Adapter identity is missing.");
            adapterAvailabilityTracker.CommitDisconnected(disconnectedInstanceId, disconnectedSnapshot.ConnectionGeneration);

            Exception? exception = await Record.ExceptionAsync(() => Task.Delay(TimeSpan.FromMilliseconds(350)));
            Assert.Null(exception);

            // Reconnecting lets the released slot send again.
            FakeAdapterIpcConnection newConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 99,
                ConnectionGeneration = 2,
            };
            adapterAvailabilityTracker.CommitConnected(AdapterInstanceId.NewId(), 2);
            CompleteAdapterResynchronization(adapterAvailabilityTracker);
            listener.CurrentConnection = newConnection;
            await WaitUntilAsync(() => newConnection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            cancellation.Cancel();
            await run;
        }

        /// <summary>
        /// Verifies that an immediate reply racing the scheduler's own send cannot be missed. The reply is
        /// applied from a genuinely different, already-started thread -- not a same-thread reentrant call,
        /// which the scheduler's own lock is reentrant against and so would not exercise this at all --
        /// deliberately given a full window to reach and attempt its own lock acquisition while the send
        /// call itself is held open, mirroring the real inbound read-loop thread racing the outbound send.
        /// Under the fix, that background attempt must block on the scheduler's own held lock for the
        /// whole window and only then correctly release the slot; without it, the background thread finds
        /// nothing holding the lock, observes the slot not yet marked outstanding, and misses it -- so this
        /// deterministically distinguishes the two, rather than depending on raw thread-scheduling luck.
        /// The slot must still end up released for the very next tick, rather than being silently dropped
        /// and stranding it until its own five-tick (250ms) timeout.
        /// </summary>
        [Fact]
        public async Task RunAsync_ImmediateReplyRacesQueueAdmissionOnAnotherThread_StillReleasesSlotForNextTick()
        {
            var liveCaptureSink = new FakeLiveCaptureSink();
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            using SemaphoreSlim backgroundReplyStarted = new(0, 1);
            // Filtered to the Vitals token specifically: this fake's single hook fires for every
            // queue-admission call, including the catalog's independent Xp/Medium loop's own ticks, and
            // must not race-release the Vitals slot for an unrelated unit's send.
            connection.OnTrySendReadSample = () =>
            {
                if (connection.ReadSampleCalls[^1] != (uint)CharacterSampleToken.CharacterVitals)
                {
                    return;
                }

                var backgroundReplyThread = new Thread(() =>
                {
                    backgroundReplyStarted.Release();
                    liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                        42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
                });
                backgroundReplyThread.Start();
                // Waits for the background thread to have at least started, then holds this call open for
                // a further deliberate window: long enough that thread-scheduling jitter cannot explain
                // either outcome -- under the fix, this call itself runs inside the scheduler's own held
                // lock (see LiveStateScheduler.RunSampleLoopAsync), so the background thread spends this
                // whole window genuinely blocked on it rather than racing to finish first.
                backgroundReplyStarted.Wait(TimeSpan.FromMilliseconds(200));
                Thread.Sleep(TimeSpan.FromMilliseconds(20));
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            // Comfortably past the two ticks plus hook-sleep overhead a promptly-released slot needs to
            // send a second time, but well short of the five-tick (250ms) timeout a stuck slot would
            // instead need: a second send landing in this window is only possible if the raced reply
            // actually released the slot promptly, not via the timeout.
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            cancellation.Cancel();
            await run;

            Assert.True(connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2);
        }

        /// <summary>
        /// Verifies that an outstanding slot pauses rather than losing its state when the play context
        /// clears mid-request: no timeout/retry burst happens while cleared, and normal ticking (up to
        /// and including the timeout-driven retry) resumes once a play context is active again.
        /// </summary>
        [Fact]
        public async Task RunAsync_PlayContextClearedWhileSlotOutstanding_PausesThenResumesOnceReestablished()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var playContextTracker = new FakePlayContextTracker();
            playContextTracker.NotifyTransition(new PlayContextId(Guid.NewGuid()));
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), playContextTracker, BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);
            int countWhileOutstanding = connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals);

            playContextTracker.ClearCurrent();
            // Comfortably longer than the five-tick timeout budget: paused, so no retry burst can happen
            // even though the outstanding slot was never released.
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.Equal(countWhileOutstanding, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));

            playContextTracker.NotifyTransition(new PlayContextId(Guid.NewGuid()));
            // Normal ticking resumes: the timeout-driven retry this same slot was always going to reach
            // eventually still lands, proving the pause never lost or corrupted its state.
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) > countWhileOutstanding, run);

            cancellation.Cancel();
            await run;
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

        /// <summary>Creates connected Adapter state with a completed resynchronization.</summary>
        /// <returns>Availability state that permits ordinary scheduled samples.</returns>
        private static AdapterAvailabilityTracker BuildAvailableAdapterAvailabilityTracker(long generation = 1)
        {
            AdapterAvailabilityTracker tracker = BuildPendingAdapterAvailabilityTracker(generation);
            CompleteAdapterResynchronization(tracker);
            return tracker;
        }

        /// <summary>Creates a catalog containing only the Fast Vitals unit for deterministic scheduler race tests.</summary>
        /// <returns>The default catalog's Vitals unit and state-area definitions.</returns>
        private static LiveStateCatalog SingleVitalsCatalog()
        {
            CaptureUnitDefinition vitals = LiveStateCatalog.Default.CaptureUnits
                .Single(unit => unit.Source == CaptureSourceKind.Sample
                    && unit.CaptureKey == (uint)CharacterSampleToken.CharacterVitals);
            return new LiveStateCatalog([vitals], LiveStateCatalog.Default.StateAreas);
        }

        /// <summary>Creates connected Adapter state with a pending resynchronization.</summary>
        /// <returns>Availability state that suppresses ordinary scheduled samples.</returns>
        private static AdapterAvailabilityTracker BuildPendingAdapterAvailabilityTracker(long generation = 1)
        {
            AdapterAvailabilityTracker tracker = new();
            tracker.CommitConnected(AdapterInstanceId.NewId(), generation);
            return tracker;
        }

        /// <summary>Completes the current Adapter resynchronization.</summary>
        /// <param name="tracker">The connected Adapter availability tracker to update.</param>
        /// <exception cref="InvalidOperationException">Thrown when the tracker has no pending connected resynchronization.</exception>
        private static void CompleteAdapterResynchronization(AdapterAvailabilityTracker tracker)
        {
            AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
            AdapterInstanceId instanceId = snapshot.CurrentInstanceId
                ?? throw new InvalidOperationException("The connected Adapter identity is missing.");
            IAdapterResynchronizationToken token = tracker.TryClaimResynchronizationToken()
                ?? throw new InvalidOperationException("No Adapter resynchronization is pending.");
            tracker.NotifyResynchronized(instanceId, snapshot.ConnectionGeneration, token);
        }
    }
}
