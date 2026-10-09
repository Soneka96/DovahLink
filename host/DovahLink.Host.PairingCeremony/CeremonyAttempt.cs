using DovahLink.Host.PairingCeremony.Native;

namespace DovahLink.Host.PairingCeremony;

/// <summary>The owner thread's mutable record of one Responder attempt; never shared with another thread.</summary>
/// <param name="id">The attempt's local identifier.</param>
/// <param name="run">The native run the attempt follows.</param>
internal sealed class CeremonyAttempt(CeremonyAttemptId id, NativeRunHandle run)
{
    /// <summary>The attempt's local identifier.</summary>
    public CeremonyAttemptId Id { get; } = id;

    /// <summary>The native run the attempt follows.</summary>
    public NativeRunHandle Run { get; } = run;

    /// <summary>Where the attempt stands.</summary>
    public CeremonyAttemptStage Stage { get; set; } = CeremonyAttemptStage.AwaitingInitiatorKey;

    /// <summary>Whether the native exposure authorization was already recorded, so a retried exposure does not repeat it.</summary>
    public bool ExposureAuthorized { get; set; }

    /// <summary>The ceremony identity of the presented SAS, once presented.</summary>
    public byte[]? PresentedCeremonyIdentity { get; set; }
}
