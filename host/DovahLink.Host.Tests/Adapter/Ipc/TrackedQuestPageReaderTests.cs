using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests private tracked-quest page request ownership and response provenance.</summary>
public class TrackedQuestPageReaderTests
{
    /// <summary>Verifies the correlation is registered before synchronous queue delivery.</summary>
    [Fact]
    public async Task ReadPageAsync_RegistersCorrelationBeforeQueueAdmission()
    {
        var fixture = new ReaderFixture();
        LiveCaptureContext? deliveredContext = null;
        fixture.Connection.OnTrySendTrackedQuestPage = request =>
        {
            fixture.SentRequest = request;
            deliveredContext = fixture.BuildContext(request.CorrelationId);
            fixture.Reader.AcceptPageCapture(deliveredContext);
        };

        LiveCaptureContext? result = await fixture.ReadPageAsync();

        Assert.Same(deliveredContext, result);
        Assert.Equal(TrackedQuestPageKind.Objectives, fixture.SentRequest?.PageKind);
        Assert.Equal(9u, fixture.SentRequest?.QuestId);
        Assert.Equal((ushort)0, fixture.SentRequest?.Cursor);
    }

    /// <summary>Verifies unrelated correlations and mismatched source/context replies do not complete a read.</summary>
    [Fact]
    public async Task AcceptPageCapture_IgnoresMismatchedCorrelationAndProvenance()
    {
        var fixture = new ReaderFixture();
        LiveCaptureContext? matchingContext = null;
        fixture.Connection.OnTrySendTrackedQuestPage = request =>
        {
            fixture.Reader.AcceptPageCapture(fixture.BuildContext(
                request.CorrelationId + 1,
                captureSource: CaptureSourceKind.Sample,
                captureKey: (uint)TrackedQuestCaptureKey.Page));
            fixture.Reader.AcceptPageCapture(fixture.BuildContext(
                request.CorrelationId,
                source: new AdapterCaptureSource(AdapterInstanceId.NewId(), fixture.Source.ConnectionGeneration)));
            fixture.Reader.AcceptPageCapture(fixture.BuildContext(
                request.CorrelationId,
                playContextId: PlayContextId.NewId()));
            fixture.Reader.AcceptPageCapture(fixture.BuildContext(
                request.CorrelationId,
                playContextGeneration: fixture.PlayContext.TransitionGeneration + 1));
            fixture.Reader.AcceptPageCapture(fixture.BuildContext(
                request.CorrelationId,
                captureSource: CaptureSourceKind.Event));
            fixture.Reader.AcceptPageCapture(fixture.BuildContext(
                request.CorrelationId,
                captureKey: 0));
            fixture.Reader.AcceptPageCapture(fixture.BuildContext(0));
            matchingContext = fixture.BuildContext(request.CorrelationId);
            fixture.Reader.AcceptPageCapture(matchingContext);
        };

        LiveCaptureContext? result = await fixture.ReadPageAsync();

        Assert.Same(matchingContext, result);
    }

    /// <summary>Verifies a concurrent second page read cannot replace the outstanding correlation.</summary>
    [Fact]
    public async Task ReadPageAsync_RejectsASecondOutstandingRequest()
    {
        var fixture = new ReaderFixture();
        Task<LiveCaptureContext?> firstRead = fixture.ReadPageAsync();

        LiveCaptureContext? secondResult = await fixture.ReadPageAsync();
        fixture.Reader.AcceptPageCapture(fixture.BuildContext(100));

        Assert.Null(secondResult);
        Assert.NotNull(await firstRead);
    }

    /// <summary>Verifies queue rejection fails without waiting for a page response.</summary>
    [Fact]
    public async Task ReadPageAsync_QueueAdmissionFailureReturnsUnavailable()
    {
        var fixture = new ReaderFixture();
        fixture.Connection.TrySendReadSampleResult = false;

        LiveCaptureContext? result = await fixture.ReadPageAsync();

        Assert.Null(result);
        Assert.Empty(fixture.Connection.CancelCalls);
    }

    /// <summary>Verifies the reader does not send through a connection from another generation.</summary>
    [Fact]
    public async Task ReadPageAsync_ConnectionGenerationMismatchReturnsUnavailable()
    {
        var fixture = new ReaderFixture();
        fixture.Connection.ConnectionGeneration = 2;

        LiveCaptureContext? result = await fixture.ReadPageAsync();

        Assert.Null(result);
        Assert.Null(fixture.SentRequest);
    }

    /// <summary>Verifies the reader fails without sending when no Adapter connection is active.</summary>
    [Fact]
    public async Task ReadPageAsync_MissingConnectionReturnsUnavailable()
    {
        var fixture = new ReaderFixture();
        fixture.SetCurrentConnection(null);

        LiveCaptureContext? result = await fixture.ReadPageAsync();

        Assert.Null(result);
        Assert.Null(fixture.SentRequest);
    }

    /// <summary>Verifies an unavailable request factory result leaves no page request in flight.</summary>
    [Fact]
    public async Task ReadPageAsync_RequestPreparationFailureReturnsUnavailable()
    {
        var fixture = new ReaderFixture();
        fixture.Connection.TrackedQuestPagePreparationUnavailable = true;

        LiveCaptureContext? result = await fixture.ReadPageAsync();

        Assert.Null(result);
        Assert.Null(fixture.SentRequest);
    }

    /// <summary>Verifies a request factory cannot start a read without a usable correlation.</summary>
    [Fact]
    public async Task ReadPageAsync_ZeroCorrelationReturnsUnavailable()
    {
        var fixture = new ReaderFixture();
        fixture.Connection.PrepareTrackedQuestPageOverride = (kind, questId, cursor) =>
            new IpcReadTrackedQuestPageMessage(0, kind, questId, cursor);

        LiveCaptureContext? result = await fixture.ReadPageAsync();

        Assert.Null(result);
        Assert.Null(fixture.SentRequest);
    }

    /// <summary>Verifies a page read cannot begin before a play context is active.</summary>
    [Fact]
    public async Task ReadPageAsync_MissingPlayContextReturnsUnavailable()
    {
        var fixture = new ReaderFixture();

        LiveCaptureContext? result = await fixture.Reader.ReadPageAsync(
            fixture.Source,
            new PlayContextSnapshot(null, fixture.PlayContext.TransitionGeneration),
            TrackedQuestPageKind.Objectives,
            9,
            0,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Null(fixture.SentRequest);
    }

    /// <summary>Verifies a lost page request is canceled after the bounded response timeout.</summary>
    [Fact]
    public async Task ReadPageAsync_TimeoutCancelsTheOutstandingCorrelation()
    {
        var fixture = new ReaderFixture();

        LiveCaptureContext? result = await fixture.ReadPageAsync();

        Assert.Null(result);
        Assert.Equal([100UL], fixture.Connection.CancelCalls);
    }

    /// <summary>Verifies lifetime cancellation cancels its outstanding correlation and propagates.</summary>
    [Fact]
    public async Task ReadPageAsync_CancellationCancelsTheOutstandingCorrelation()
    {
        var fixture = new ReaderFixture();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Connection.OnTrySendTrackedQuestPage = _ => sent.TrySetResult();
        using var cancellation = new CancellationTokenSource();
        Task<LiveCaptureContext?> read = fixture.ReadPageAsync(cancellation.Token);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read);
        Assert.Equal([100UL], fixture.Connection.CancelCalls);
    }

    /// <summary>Builds one page-reader test graph with a single fake Adapter connection.</summary>
    private sealed class ReaderFixture
    {
        /// <summary>The expected Adapter instance used by this test.</summary>
        private readonly AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        /// <summary>The expected play-context identity used by this test.</summary>
        private readonly PlayContextId playContextId = PlayContextId.NewId();

        /// <summary>The capture-catalog entry accepted by the generic sink.</summary>
        private readonly CaptureUnitDefinition captureUnit = LiveStateCatalog.Default.CaptureUnits.Single(
            candidate => candidate.Source == CaptureSourceKind.Sample
                && candidate.CaptureKey == (uint)TrackedQuestCaptureKey.Page);

        /// <summary>The Host's current fake private-IPC connection.</summary>
        private readonly FakeAdapterIpcListener listener = new();

        /// <summary>The current Adapter identity and connection generation.</summary>
        private readonly AdapterAvailabilityTracker adapterTracker = new();

        /// <summary>The current play-context identity and transition generation.</summary>
        private readonly FakePlayContextTracker playContextTracker = new();

        /// <summary>The page reader under test.</summary>
        public TrackedQuestPageReader Reader { get; }

        /// <summary>The fake connection used to observe queue admission and cancellation.</summary>
        public FakeAdapterIpcConnection Connection { get; }

        /// <summary>The exact Adapter source expected for this read.</summary>
        public AdapterCaptureSource Source { get; }

        /// <summary>The exact play context expected for this read.</summary>
        public PlayContextSnapshot PlayContext { get; }

        /// <summary>The request accepted by the fake connection, if one was sent.</summary>
        public IpcReadTrackedQuestPageMessage? SentRequest { get; set; }

        /// <summary>Creates the fake reader and matching connection authority.</summary>
        public ReaderFixture()
        {
            Source = new AdapterCaptureSource(instanceId, 1);
            adapterTracker.CommitConnected(instanceId, 1);
            playContextTracker.NotifyTransition(playContextId);
            PlayContext = playContextTracker.GetSnapshot();
            Connection = new FakeAdapterIpcConnection(new MemoryStream())
            {
                ConnectionGeneration = 1,
                TrySendReadSampleResult = true,
                OnTrySendTrackedQuestPage = request => SentRequest = request,
            };
            listener.CurrentConnection = Connection;
            Reader = new TrackedQuestPageReader(() => listener, adapterTracker, playContextTracker);
        }

        /// <summary>Sends one objective page request using this fixture's capture authority.</summary>
        /// <param name="cancellationToken">The optional request cancellation token.</param>
        /// <returns>The matching response context, or <see langword="null"/> on failure.</returns>
        public Task<LiveCaptureContext?> ReadPageAsync(CancellationToken cancellationToken = default) =>
            Reader.ReadPageAsync(Source, PlayContext, TrackedQuestPageKind.Objectives, 9, 0, cancellationToken);

        /// <summary>Changes the listener's current connection for an early-exit scenario.</summary>
        /// <param name="connection">The connection to expose, or <see langword="null"/> when disconnected.</param>
        public void SetCurrentConnection(IAdapterIpcConnection? connection) => listener.CurrentConnection = connection;

        /// <summary>Builds a response context with selected provenance values.</summary>
        /// <param name="correlationId">The response correlation ID.</param>
        /// <param name="source">The Adapter connection that delivered the result.</param>
        /// <param name="playContextId">The result's play-context identity.</param>
        /// <param name="playContextGeneration">The observed play-context generation.</param>
        /// <param name="captureSource">The private IPC capture source kind.</param>
        /// <param name="captureKey">The private IPC capture key.</param>
        /// <returns>A complete generic-sink capture context.</returns>
        public LiveCaptureContext BuildContext(
            ulong correlationId,
            AdapterCaptureSource? source = null,
            PlayContextId? playContextId = null,
            long? playContextGeneration = null,
            CaptureSourceKind captureSource = CaptureSourceKind.Sample,
            uint captureKey = (uint)TrackedQuestCaptureKey.Page)
        {
            AdapterCaptureSource actualSource = source ?? Source;
            PlayContextId actualPlayContextId = playContextId ?? this.playContextId;
            long actualPlayContextGeneration = playContextGeneration ?? PlayContext.TransitionGeneration;
            var captureResult = new IpcCaptureResultMessage(
                correlationId, captureSource, captureKey, CaptureAvailability.Available,
                actualPlayContextId, [1]);
            var adapterSnapshot = new AdapterAvailabilitySnapshot(
                AdapterAvailability.Available, actualSource.InstanceId, true, actualSource.ConnectionGeneration);
            return new LiveCaptureContext(
                captureResult, actualSource, captureUnit, adapterSnapshot, actualPlayContextId,
                actualPlayContextGeneration, DateTimeOffset.UtcNow);
        }
    }
}
