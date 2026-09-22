using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Time;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>A capture result together with the exact provenance and context validated by the sink.</summary>
/// <param name="CaptureResult">The decoded adapter result that passed generic sink validation.</param>
/// <param name="Source">The exact adapter connection that delivered the result.</param>
/// <param name="CaptureUnit">The catalog unit matching the result's source and key.</param>
/// <param name="AdapterSnapshot">The adapter availability snapshot used to validate the source.</param>
/// <param name="PlayContextId">The current play context matched against the result.</param>
/// <param name="PlayContextGeneration">The transition generation observed with the current play context.</param>
/// <param name="OccurredAt">The time the sink accepted the result for dispatch.</param>
public sealed record LiveCaptureContext(
    IpcCaptureResultMessage CaptureResult,
    AdapterCaptureSource Source,
    CaptureUnitDefinition CaptureUnit,
    AdapterAvailabilitySnapshot AdapterSnapshot,
    PlayContextId PlayContextId,
    long PlayContextGeneration,
    DateTimeOffset OccurredAt);
