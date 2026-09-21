using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>
/// Full-stack proof that the real listener, connection, session, codec, availability tracker, and
/// peer-proof verifier work together end to end over a real loopback socket, driven by a small
/// in-test stand-in for the adapter. Individual collaborators already have their own isolated unit
/// tests; this class proves only that the production composition is wired correctly.
/// </summary>
public class AdapterIpcChannelIntegrationTests
{
    /// <summary>Verifies that a valid Hello followed by an accepted resynchronization result leaves the tracker available and resynchronized.</summary>
    [Fact]
    public async Task Connect_ValidHelloThenAcceptedResync_TrackerBecomesAvailableAndResynchronized()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, instanceId, verifier.ExpectedToken)));
        IpcMessage ack = await ReadOneFrameAsync(adapterStream, codec);
        var request = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);

        Assert.True(Assert.IsType<IpcHelloAckMessage>(ack).Accepted);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);
        Assert.Equal(instanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);

        // The coordinator's required-area set is empty (see CreateRealStack), so the accepted plan
        // alone completes the resynchronization once the first play context has been reported.
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true)));
        await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a declined resynchronization result leaves the tracker still needing resynchronization rather than clearing it.</summary>
    [Fact]
    public async Task Connect_ValidHelloThenDeclinedResync_ClosesConnectionAndTrackerStillNeedsResynchronization()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        var request = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: false)));

        // A declined result must never leave the connection stuck forever with no retry: the host
        // closes it, observed here as the tracker reporting the adapter unavailable again -- the
        // adapter's own normal reconnect is what drives a fresh initial resynchronization from there.
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);
        Assert.True(tracker.NeedsResynchronization);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a Hello with the wrong peer-ownership proof is rejected and never reports the tracker as available.</summary>
    [Fact]
    public async Task Connect_WrongProofHello_RejectsAndTrackerStaysUnavailable()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, _, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [9, 9, 9])));
        IpcMessage ack = await ReadOneFrameAsync(adapterStream, codec);

        Assert.False(Assert.IsType<IpcHelloAckMessage>(ack).Accepted);
        Assert.Equal(IpcHelloRejectReason.InvalidProof, ((IpcHelloAckMessage)ack).RejectReason);
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that the adapter disconnecting after a full resynchronization marks the tracker unavailable and needing a fresh resynchronization.</summary>
    [Fact]
    public async Task Disconnect_AfterResynchronized_MarksTrackerUnavailableAndNeedingResync()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: true);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        var request = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true)));
        await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);

        adapterStream.Dispose();

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);
        Assert.True(tracker.NeedsResynchronization);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that reconnecting after a disconnect is assigned a fresh connection generation and again requires a fresh baseline.</summary>
    [Fact]
    public async Task Reconnect_AfterDisconnect_GetsFreshGenerationAndRequiresFreshBaseline()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        using (Socket firstSocket = await ConnectClientAsync(listener.BoundPort))
        using (var firstStream = new NetworkStream(firstSocket, ownsSocket: false))
        {
            await firstStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, instanceId, verifier.ExpectedToken)));
            await ReadOneFrameAsync(firstStream, codec); // acknowledgement
            var firstRequest = await SendActiveContextAndReadResynchronizeAsync(firstStream, codec);
            await firstStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(firstRequest.CorrelationId, Accepted: true)));
            await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);
        }

        long firstGeneration = tracker.CurrentConnectionGeneration;
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);

        using Socket secondSocket = await ConnectClientAsync(listener.BoundPort);
        using var secondStream = new NetworkStream(secondSocket, ownsSocket: false);
        await secondStream.WriteAsync(codec.Encode(new IpcHelloMessage(2, instanceId, verifier.ExpectedToken)));
        await ReadOneFrameAsync(secondStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(secondStream, codec);

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);
        Assert.Equal(firstGeneration + 1, tracker.CurrentConnectionGeneration);
        Assert.True(tracker.NeedsResynchronization);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a same-context replay on a new connection still sends one fresh baseline request.</summary>
    [Fact]
    public async Task Reconnect_SameActiveContextReplay_SendsExactlyOneFreshResynchronizeRequest()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _, IPlayContextTracker playContextTracker) = CreateRealStackWithContext();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PlayContextId context = PlayContextId.NewId();

        using (Socket firstSocket = await ConnectClientAsync(listener.BoundPort))
        using (var firstStream = new NetworkStream(firstSocket, ownsSocket: false))
        {
            await firstStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, instanceId, verifier.ExpectedToken)));
            await ReadOneFrameAsync(firstStream, codec);
            var firstRequest = await SendActiveContextAndReadResynchronizeAsync(firstStream, codec, context);
            await firstStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(firstRequest.CorrelationId, Accepted: true)));
            await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);
        }

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);

        using Socket secondSocket = await ConnectClientAsync(listener.BoundPort);
        using var secondStream = new NetworkStream(secondSocket, ownsSocket: false);
        await secondStream.WriteAsync(codec.Encode(new IpcHelloMessage(2, instanceId, verifier.ExpectedToken)));
        await ReadOneFrameAsync(secondStream, codec);
        var secondRequest = await SendActiveContextAndReadResynchronizeAsync(secondStream, codec, context);

        Assert.Equal(context, playContextTracker.Current);
        Assert.True(tracker.NeedsResynchronization);
        await secondStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(secondRequest.CorrelationId, Accepted: true)));
        await CloseAndAssertNoFrameAsync(secondStream, codec);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that an inactive first replay sends no baseline request.</summary>
    [Fact]
    public async Task Connect_InactiveFirstReplay_SendsNoResynchronizeRequest()
    {
        (IAdapterIpcListener listener, _, IAdapterPeerProofVerifier verifier, _, IPlayContextTracker playContextTracker) = CreateRealStackWithContext();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec);
        await adapterStream.WriteAsync(codec.Encode(new IpcPlayContextEndedMessage(0)));

        await CloseAndAssertNoFrameAsync(adapterStream, codec);
        Assert.Null(playContextTracker.Current);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that an inactive replay on a reconnect also sends no baseline request.</summary>
    [Fact]
    public async Task Reconnect_InactiveFirstReplay_SendsNoResynchronizeRequest()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _, IPlayContextTracker playContextTracker) = CreateRealStackWithContext();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        using (Socket firstSocket = await ConnectClientAsync(listener.BoundPort))
        using (var firstStream = new NetworkStream(firstSocket, ownsSocket: false))
        {
            await firstStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, instanceId, verifier.ExpectedToken)));
            await ReadOneFrameAsync(firstStream, codec);
            var firstRequest = await SendActiveContextAndReadResynchronizeAsync(firstStream, codec);
            await firstStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(firstRequest.CorrelationId, Accepted: true)));
            await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);
            await firstStream.WriteAsync(codec.Encode(new IpcPlayContextEndedMessage(0)));
            await CloseAndAssertNoFrameAsync(firstStream, codec);
        }

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);

        using Socket secondSocket = await ConnectClientAsync(listener.BoundPort);
        using var secondStream = new NetworkStream(secondSocket, ownsSocket: false);
        await secondStream.WriteAsync(codec.Encode(new IpcHelloMessage(2, instanceId, verifier.ExpectedToken)));
        await ReadOneFrameAsync(secondStream, codec);
        await secondStream.WriteAsync(codec.Encode(new IpcPlayContextEndedMessage(0)));

        await CloseAndAssertNoFrameAsync(secondStream, codec);
        Assert.Null(playContextTracker.Current);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a real later context transition sends one fresh request while the connection remains alive.</summary>
    [Fact]
    public async Task ConnectedContextTransition_SendsExactlyOneFreshResynchronizeRequest()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _, IPlayContextTracker playContextTracker) = CreateRealStackWithContext();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec);
        var firstRequest = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(firstRequest.CorrelationId, Accepted: true)));
        await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);

        PlayContextId secondContext = PlayContextId.NewId();
        var secondRequest = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec, secondContext);

        Assert.Equal(secondContext, playContextTracker.Current);
        Assert.True(tracker.NeedsResynchronization);
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(secondRequest.CorrelationId, Accepted: true)));
        await CloseAndAssertNoFrameAsync(adapterStream, codec);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a later context supersedes a still-pending earlier resynchronization request.</summary>
    [Fact]
    public async Task ConnectedContextTransition_WhilePreviousResyncPending_SendsFreshRequest()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _, _) = CreateRealStackWithContext();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec);
        var firstRequest = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);

        var secondRequest = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);

        Assert.NotEqual(firstRequest.CorrelationId, secondRequest.CorrelationId);
        Assert.True(tracker.NeedsResynchronization);
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(secondRequest.CorrelationId, Accepted: true)));
        await CloseAndAssertNoFrameAsync(adapterStream, codec);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that ending a context sends no request and a later active context sends one.</summary>
    [Fact]
    public async Task ContextEndThenNewContext_SendsOnlyTheNewContextResynchronizeRequest()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _, IPlayContextTracker playContextTracker) = CreateRealStackWithContext();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec);
        var firstRequest = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(firstRequest.CorrelationId, Accepted: true)));
        await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);

        await adapterStream.WriteAsync(codec.Encode(new IpcPlayContextEndedMessage(0)));

        PlayContextId newContext = PlayContextId.NewId();
        var newRequest = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec, newContext);
        Assert.Equal(newContext, playContextTracker.Current);
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(newRequest.CorrelationId, Accepted: true)));
        await CloseAndAssertNoFrameAsync(adapterStream, codec);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a subscriber reacting to <see cref="AdapterAvailability.Available"/> synchronously
    /// can already send normal Host-directed work, and that the wire still carries it strictly after
    /// the acknowledgement and the initial resynchronization request.
    /// </summary>
    [Fact]
    public async Task Connect_AvailableSubscriber_CanImmediatelySendNormalWorkAfterEstablishmentFrames()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        bool? sendAccepted = null;
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Available)
            {
                sendAccepted = listener.CurrentConnection!.TrySendListenEvent(42, out _);
            }
        };

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));

        IpcMessage ack = await ReadOneFrameAsync(adapterStream, codec);
        IpcMessage normalWork = await ReadOneFrameAsync(adapterStream, codec);
        IpcMessage resync = await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);

        Assert.True(Assert.IsType<IpcHelloAckMessage>(ack).Accepted);
        var listenEvent = Assert.IsType<IpcListenEventMessage>(normalWork);
        Assert.IsType<IpcResynchronizeRequestMessage>(resync);
        Assert.Equal(42u, listenEvent.EventKey);
        Assert.True(sendAccepted);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a subscriber reacting to <see cref="AdapterAvailability.Unavailable"/>
    /// synchronously cannot send any new Host-directed work, and that a protocol close's graceful
    /// writer drain never delivers a frame that was rejected during that window.
    /// </summary>
    [Fact]
    public async Task Disconnect_UnavailableSubscriber_CannotSendAndGracefulDrainWritesNoStaleFrame()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

        IAdapterIpcConnection disconnectingConnection = listener.CurrentConnection!;
        bool? listenEventAccepted = null;
        bool? readSampleAccepted = null;
        bool? cancelAccepted = null;
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Unavailable)
            {
                listenEventAccepted = disconnectingConnection.TrySendListenEvent(1, out _);
                readSampleAccepted = disconnectingConnection.TrySendReadSample(1, out _);
                cancelAccepted = disconnectingConnection.TryCancel(1);
            }
        };

        await adapterStream.WriteAsync(codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal)));

        // The graceful writer drain runs after this; reading past it to EOF proves it never wrote a
        // frame the subscriber's rejected sends above would have queued.
        int trailingByte = await adapterStream.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, trailingByte);
        Assert.False(listenEventAccepted);
        Assert.False(readSampleAccepted);
        Assert.False(cancelAccepted);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a subscriber which always throws does not desync the tracker from the
    /// connection's lease: the connection still becomes available, and a later disconnect still
    /// correctly reaches unavailable.
    /// </summary>
    [Fact]
    public async Task Connect_ThrowingAvailableSubscriber_TrackerStillCoherentThroughConnectAndDisconnect()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        tracker.AvailabilityChanged += _ => throw new InvalidOperationException("Simulated subscriber failure.");

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);
        Assert.Equal(1, tracker.CurrentConnectionGeneration);

        await adapterStream.WriteAsync(codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal)));

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a subscriber which always throws does not block connection teardown or the
    /// listener's own recovery: the first connection still reaches unavailable, and the listener
    /// still accepts and fully serves a second connection afterward.
    /// </summary>
    [Fact]
    public async Task Disconnect_ThrowingUnavailableSubscriber_StillCompletesTeardownAndListenerAcceptsNextConnection()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        tracker.AvailabilityChanged += _ => throw new InvalidOperationException("Simulated subscriber failure.");

        using (Socket firstSocket = await ConnectClientAsync(listener.BoundPort))
        using (var firstStream = new NetworkStream(firstSocket, ownsSocket: false))
        {
            await firstStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
            await ReadOneFrameAsync(firstStream, codec); // acknowledgement
            await SendActiveContextAndReadResynchronizeAsync(firstStream, codec);
            await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);
        }

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);

        // The listener's own accept loop directly awaits one connection's RunAsync before accepting
        // the next, so this second handshake succeeding at all is itself proof the first connection's
        // full teardown -- including the throwing subscriber inside it -- already returned rather than hanging.
        using Socket secondSocket = await ConnectClientAsync(listener.BoundPort);
        using var secondStream = new NetworkStream(secondSocket, ownsSocket: false);
        await secondStream.WriteAsync(codec.Encode(new IpcHelloMessage(2, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        IpcMessage ack = await ReadOneFrameAsync(secondStream, codec);

        Assert.True(Assert.IsType<IpcHelloAckMessage>(ack).Accepted);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a captured reference to an old connection cannot send after a newer connection
    /// for the same adapter instance id has become active -- eligibility belongs to the exact
    /// connection lease, not merely the adapter instance id. By this point the old connection's own
    /// outbound channel is already completed from its prior teardown, which independently blocks the
    /// write too; <see cref="AdapterIpcSessionTests"/>'s superseded-lease test isolates the lease
    /// check itself, without any connection or channel involved.
    /// </summary>
    [Fact]
    public async Task Reconnect_OldConnectionSameInstanceId_CannotSendAfterNewerGenerationActivates()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        IAdapterIpcConnection firstConnection;
        using (Socket firstSocket = await ConnectClientAsync(listener.BoundPort))
        using (var firstStream = new NetworkStream(firstSocket, ownsSocket: false))
        {
            await firstStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, instanceId, verifier.ExpectedToken)));
            await ReadOneFrameAsync(firstStream, codec); // acknowledgement
            await SendActiveContextAndReadResynchronizeAsync(firstStream, codec);
            await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);
            firstConnection = listener.CurrentConnection!;
        }

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);

        using Socket secondSocket = await ConnectClientAsync(listener.BoundPort);
        using var secondStream = new NetworkStream(secondSocket, ownsSocket: false);
        await secondStream.WriteAsync(codec.Encode(new IpcHelloMessage(2, instanceId, verifier.ExpectedToken)));
        await ReadOneFrameAsync(secondStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(secondStream, codec);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

        Assert.False(firstConnection.TrySendListenEvent(1, out _));
        Assert.False(firstConnection.TrySendReadSample(1, out _));
        Assert.False(firstConnection.TryCancel(1));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a trust-admin request sent over a real authenticated socket reaches the
    /// injected handler and that its formatted result is sent back correlated, covering the exact
    /// operation/argument matrix the plan requires.
    /// </summary>
    [Fact]
    public async Task Connect_ValidHelloThenTrustAdminRequest_HandlerResultIsSentBackCorrelated()
    {
        (IAdapterIpcListener listener, _, IAdapterPeerProofVerifier verifier, FakeAdapterTrustAdminRequestHandler trustAdminRequestHandler) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        trustAdminRequestHandler.Result = "Revoked client 12345 (My PC).";

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);

        var request = new IpcTrustAdminRequestMessage(7, TrustAdminOperation.Revoke, ShortId: "12345");
        await adapterStream.WriteAsync(codec.Encode(request));
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(adapterStream, codec));

        Assert.Equal(7ul, result.CorrelationId);
        Assert.Equal("Revoked client 12345 (My PC).", result.ResultText);
        Assert.Single(trustAdminRequestHandler.HandledRequests);
        Assert.Equal(request, trustAdminRequestHandler.HandledRequests[0]);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that an initial pairing-display request sent through the real production
    /// <see cref="AdapterPairingNotifier"/> reaches a connected adapter over the wire and that an
    /// accepted acknowledgement resolves the notifier's returned task <see langword="true"/>.
    /// </summary>
    [Fact]
    public async Task Connect_PairingDisplayInitialThenAccepted_NotifierResolvesTrue()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        var notifier = new AdapterPairingNotifier(listener);

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

        Task<bool> notifyTask = notifier.TryNotifyCodeAvailableAsync("123456", CancellationToken.None);
        var displayRequest = Assert.IsType<IpcPairingDisplayMessage>(await ReadOneFrameAsync(adapterStream, codec));
        Assert.Equal("123456", displayRequest.Code);
        Assert.Equal(PairingDisplayMode.Initial, displayRequest.Mode);

        await adapterStream.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(displayRequest.CorrelationId, Accepted: true)));

        Assert.True(await notifyTask.WaitAsync(TimeSpan.FromSeconds(5)));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a declined acknowledgement resolves the notifier's returned task <see langword="false"/>.</summary>
    [Fact]
    public async Task Connect_PairingDisplayDeclined_NotifierResolvesFalse()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        var notifier = new AdapterPairingNotifier(listener);

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

        Task<bool> notifyTask = notifier.TryNotifyRedisplayAsync("654321", CancellationToken.None);
        var displayRequest = Assert.IsType<IpcPairingDisplayMessage>(await ReadOneFrameAsync(adapterStream, codec));
        Assert.Equal(PairingDisplayMode.ManualRedisplay, displayRequest.Mode);

        await adapterStream.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(displayRequest.CorrelationId, Accepted: false)));

        Assert.False(await notifyTask.WaitAsync(TimeSpan.FromSeconds(5)));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that the no-code attempts-exhausted notification reaches a connected adapter over the wire.</summary>
    [Fact]
    public async Task Connect_PairingAttemptsExhausted_DeliversNoCodeNotification()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        var notifier = new AdapterPairingNotifier(listener);

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(adapterStream, codec);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

        await notifier.NotifyAttemptsExhaustedAsync(CancellationToken.None);

        Assert.IsType<IpcPairingAttemptsExhaustedMessage>(await ReadOneFrameAsync(adapterStream, codec));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a pairing-display acknowledgement can never resolve a different connection's
    /// pending request even when both connections' correlation ids coincide, by disconnecting one
    /// adapter mid-request (which resolves its own pending wait <see langword="false"/> rather than
    /// hanging) and reconnecting a fresh adapter whose own request reuses the exact same correlation
    /// id -- each connection owns an independent pending-acknowledgement table, so id reuse across a
    /// reconnect can never cross-resolve a newer request with an older one's outcome.
    /// </summary>
    [Fact]
    public async Task Reconnect_AfterUnacknowledgedPairingDisplay_NewConnectionReusingSameCorrelationIdResolvesIndependently()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        var notifier = new AdapterPairingNotifier(listener);
        ulong firstCorrelationId;

        Task<bool> firstNotifyTask;
        using (Socket firstSocket = await ConnectClientAsync(listener.BoundPort))
        using (var firstStream = new NetworkStream(firstSocket, ownsSocket: false))
        {
            await firstStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
            await ReadOneFrameAsync(firstStream, codec); // acknowledgement
            await SendActiveContextAndReadResynchronizeAsync(firstStream, codec);
            await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

            firstNotifyTask = notifier.TryNotifyCodeAvailableAsync("111111", CancellationToken.None);
            var firstDisplayRequest = Assert.IsType<IpcPairingDisplayMessage>(await ReadOneFrameAsync(firstStream, codec));
            firstCorrelationId = firstDisplayRequest.CorrelationId;
            // The socket closes here without ever sending an acknowledgement.
        }

        Assert.False(await firstNotifyTask.WaitAsync(TimeSpan.FromSeconds(5)));
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);

        using Socket secondSocket = await ConnectClientAsync(listener.BoundPort);
        using var secondStream = new NetworkStream(secondSocket, ownsSocket: false);
        await secondStream.WriteAsync(codec.Encode(new IpcHelloMessage(2, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(secondStream, codec); // acknowledgement
        await SendActiveContextAndReadResynchronizeAsync(secondStream, codec);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

        Task<bool> secondNotifyTask = notifier.TryNotifyCodeAvailableAsync("222222", CancellationToken.None);
        var secondDisplayRequest = Assert.IsType<IpcPairingDisplayMessage>(await ReadOneFrameAsync(secondStream, codec));
        Assert.Equal(firstCorrelationId, secondDisplayRequest.CorrelationId);

        await secondStream.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(secondDisplayRequest.CorrelationId, Accepted: true)));

        Assert.True(await secondNotifyTask.WaitAsync(TimeSpan.FromSeconds(5)));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Composes the real production private-IPC graph over a listener bound to an OS-assigned loopback port.</summary>
    private static (IAdapterIpcListener Listener, IAdapterAvailabilityTracker Tracker, IAdapterPeerProofVerifier Verifier, FakeAdapterTrustAdminRequestHandler TrustAdminRequestHandler) CreateRealStack()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier, FakeAdapterTrustAdminRequestHandler trustAdminRequestHandler, _) = CreateRealStackWithContext();
        return (listener, tracker, verifier, trustAdminRequestHandler);
    }

    /// <summary>Composes the real stack while exposing its Host-lifetime play-context tracker for sequencing tests.</summary>
    private static (IAdapterIpcListener Listener, IAdapterAvailabilityTracker Tracker, IAdapterPeerProofVerifier Verifier, FakeAdapterTrustAdminRequestHandler TrustAdminRequestHandler, IPlayContextTracker PlayContextTracker) CreateRealStackWithContext()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        var verifier = new AdapterPeerProofVerifier();
        var codec = new IpcFrameCodec();
        var playContextTracker = new PlayContextTracker();
        var trustAdminRequestHandler = new FakeAdapterTrustAdminRequestHandler();
        // An empty catalog's required-area set is trivially satisfied, so the real coordinator
        // completes resynchronization from the wire-level accept alone -- this class proves
        // handshake/connection-level wiring, not the catalog-specific baseline-transaction semantics
        // LiveCaptureSinkTests and ResynchronizationTransactionCoordinatorTests already cover.
        var catalog = new LiveStateCatalog([], []);
        var coordinator = new ResynchronizationTransactionCoordinator(catalog, tracker, new FakeAdapterContinuityRecovery(), TimeSpan.FromSeconds(30));
        ResynchronizationPlan plan = catalog.BuildResynchronizationPlan();
        var listener = new AdapterIpcListener(0, stream =>
            new AdapterIpcConnection(
                stream,
                codec,
                new AdapterIpcSession(
                    lifecycle,
                    verifier,
                    trustAdminRequestHandler,
                    playContextTracker,
                    new FakeLiveCaptureSink(),
                    coordinator,
                    plan),
                new SystemClock()));
        _ = new PlayContextResynchronizationTrigger(playContextTracker, tracker, listener, new FakeResynchronizationTransactionCoordinator());
        return (listener, tracker, verifier, trustAdminRequestHandler, playContextTracker);
    }

    /// <summary>Reports an active Adapter context and reads the one request it must cause.</summary>
    private static Task<IpcResynchronizeRequestMessage> SendActiveContextAndReadResynchronizeAsync(Stream stream, IIpcFrameCodec codec) =>
        SendActiveContextAndReadResynchronizeAsync(stream, codec, PlayContextId.NewId());

    /// <summary>Reports a specific active Adapter context and reads the one request it must cause.</summary>
    private static async Task<IpcResynchronizeRequestMessage> SendActiveContextAndReadResynchronizeAsync(Stream stream, IIpcFrameCodec codec, PlayContextId context)
    {
        await stream.WriteAsync(codec.Encode(new IpcPlayContextChangedMessage(0, context)));
        return Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(stream, codec));
    }

    /// <summary>Connects a plain client socket to the listener's bound loopback port, standing in for the adapter.</summary>
    private static async Task<Socket> ConnectClientAsync(int port)
    {
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(IPAddress.Loopback, port);
        return client;
    }

    /// <summary>Reads and decodes exactly one frame from the fake adapter's side of the connection.</summary>
    private static async Task<IpcMessage> ReadOneFrameAsync(Stream stream, IIpcFrameCodec codec)
    {
        byte[] lengthPrefix = new byte[sizeof(uint)];
        await ReadExactAsync(stream, lengthPrefix);
        Assert.True(codec.TryReadFrameLength(lengthPrefix, out int frameLength));
        byte[] frame = new byte[frameLength];
        await ReadExactAsync(stream, frame);
        IpcDecodeResult result = codec.Decode(frame);
        Assert.Null(result.FailureReason);
        return result.Message!;
    }

    /// <summary>Fills a buffer completely from a raw transport, tolerating partial reads.</summary>
    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead)).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(read > 0, "Unexpected end of stream while reading a test frame.");
            totalRead += read;
        }
    }

    /// <summary>Closes the peer and asserts that no additional outbound frame was queued first.</summary>
    private static async Task CloseAndAssertNoFrameAsync(Stream stream, IIpcFrameCodec codec)
    {
        await stream.WriteAsync(codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal)));
        byte[] lengthPrefix = new byte[sizeof(uint)];
        int read = await stream.ReadAsync(lengthPrefix).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, read);
    }

    /// <summary>
    /// Polls a condition until it becomes true, failing the test if it never does within a bounded
    /// time. If <paramref name="guardTask"/> completes first, awaits it so a fault in the listener's
    /// run loop surfaces directly instead of being masked by a confusing timeout failure.
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
            await Task.Delay(10);
        }
    }
}
