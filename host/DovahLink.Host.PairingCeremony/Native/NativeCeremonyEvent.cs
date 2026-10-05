namespace DovahLink.Host.PairingCeremony.Native;

/// <summary>One drive event translated into DovahLink-owned values.</summary>
/// <param name="Kind">The event kind.</param>
/// <param name="ProtocolEvent">The protocol message a step concerns, or <see cref="NativeProtocolEvent.Other"/>.</param>
/// <param name="Run">The run the event names, or <see langword="null"/> when it names none.</param>
/// <param name="RunEnded">Whether the event made that run's end visible.</param>
/// <param name="Result">The detached local result the event delivered, or <see langword="null"/>.</param>
internal sealed record NativeCeremonyEvent(
    NativeCeremonyEventKind Kind,
    NativeProtocolEvent ProtocolEvent,
    NativeRunHandle? Run,
    bool RunEnded,
    CeremonyResultSnapshot? Result);
