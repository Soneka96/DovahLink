namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Reports one captured value, or its unavailability, from the adapter. <paramref name="CorrelationId"/>
/// matches the originating <see cref="IpcReadSampleMessage"/> for a sampled capture, or is zero for a
/// capture with no originating host request (for example a future spontaneous native-event capture).
/// </summary>
/// <param name="CorrelationId">Matches the originating request, or zero. See the type documentation.</param>
/// <param name="Source">Which host-owned key namespace <paramref name="CaptureKey"/> belongs to.</param>
/// <param name="CaptureKey">The host-owned sample token or event key this result was captured for.</param>
/// <param name="Availability">Whether <paramref name="Payload"/> holds a real captured value.</param>
/// <param name="Payload">The captured value, already copied out of Skyrim state; empty when <paramref name="Availability"/> is <see cref="CaptureAvailability.Unavailable"/>.</param>
public sealed record IpcCaptureResultMessage(
    ulong CorrelationId,
    CaptureSourceKind Source,
    uint CaptureKey,
    CaptureAvailability Availability,
    byte[] Payload) : IpcMessage(CorrelationId);
