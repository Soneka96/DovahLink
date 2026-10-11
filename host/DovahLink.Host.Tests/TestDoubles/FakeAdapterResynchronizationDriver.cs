using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// Plays the Adapter side of a complete, real resynchronization over a raw private-IPC stream so a
/// composed Host reaches gameplay-ready: every baseline sample is answered, the resynchronization
/// request is accepted, and the Host-orchestrated tracked-quest page is answered. Every capture is
/// reported <see cref="CaptureAvailability.Unavailable"/>, which the Host accepts as a legitimate
/// baseline, so no per-area payload encoding is needed. Once complete, the Host begins ordinary
/// sampling; <see cref="ReadPairingDisplayAsync"/> answers those reads while waiting for a frame the
/// test actually cares about.
/// </summary>
public sealed class FakeAdapterResynchronizationDriver
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);

    private readonly Stream stream;

    private readonly IIpcFrameCodec codec;

    private readonly PlayContextId playContextId;

    /// <summary>Creates a driver for one connected, already-handshaken adapter stream.</summary>
    /// <param name="stream">The Adapter side of the private IPC connection.</param>
    /// <param name="codec">The codec used for private IPC frames.</param>
    /// <param name="playContextId">The play context the Adapter reported.</param>
    public FakeAdapterResynchronizationDriver(Stream stream, IIpcFrameCodec codec, PlayContextId playContextId)
    {
        this.stream = stream;
        this.codec = codec;
        this.playContextId = playContextId;
    }

    /// <summary>
    /// Completes the resynchronization the Host just requested and returns once the Host has begun
    /// ordinary sampling -- the point at which it proves the adapter available, resynchronized, and
    /// on the current connection.
    /// </summary>
    /// <param name="request">The resynchronization request the Host sent.</param>
    public async Task CompleteResynchronizationAsync(IpcResynchronizeRequestMessage request)
    {
        foreach (uint token in request.BaselineSampleTokens)
        {
            await SendUnavailableSampleAsync(0, token);
        }

        await stream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true)));

        var questPage = Assert.IsType<IpcReadTrackedQuestPageMessage>(await ReadFrameAsync());
        await SendUnavailableSampleAsync(questPage.CorrelationId, (uint)TrackedQuestCaptureKey.Page);

        var firstSample = Assert.IsType<IpcReadSampleMessage>(await ReadFrameAsync());
        await SendUnavailableSampleAsync(firstSample.CorrelationId, firstSample.SampleToken);
    }

    /// <summary>Reads frames, answering ordinary sample reads, until the pairing display request arrives.</summary>
    /// <returns>The Host's pairing display request.</returns>
    public async Task<IpcPairingDisplayMessage> ReadPairingDisplayAsync()
    {
        while (true)
        {
            IpcMessage frame = await ReadFrameAsync();
            switch (frame)
            {
                case IpcPairingDisplayMessage display:
                    return display;
                case IpcReadSampleMessage sample:
                    await SendUnavailableSampleAsync(sample.CorrelationId, sample.SampleToken);
                    break;
                default:
                    Assert.Fail($"Unexpected frame while waiting for a pairing display request: {frame.GetType().Name}.");
                    break;
            }
        }
    }

    private Task SendUnavailableSampleAsync(ulong correlationId, uint captureKey) =>
        stream.WriteAsync(codec.Encode(new IpcCaptureResultMessage(
            correlationId, CaptureSourceKind.Sample, captureKey, CaptureAvailability.Unavailable, playContextId, []))).AsTask();

    private async Task<IpcMessage> ReadFrameAsync()
    {
        byte[] lengthPrefix = new byte[sizeof(uint)];
        await stream.ReadExactlyAsync(lengthPrefix).AsTask().WaitAsync(FrameTimeout);
        Assert.True(codec.TryReadFrameLength(lengthPrefix, out int frameLength));
        byte[] frame = new byte[frameLength];
        await stream.ReadExactlyAsync(frame).AsTask().WaitAsync(FrameTimeout);
        IpcDecodeResult result = codec.Decode(frame);
        Assert.Null(result.FailureReason);
        return result.Message!;
    }
}
