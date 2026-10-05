namespace DovahLink.Host.PairingCeremony.Native;

/// <summary>The translated events of one bounded drive, and the owner-loop failure that may accompany them.</summary>
/// <param name="Events">The events in native order; each must be processed before <paramref name="Failure"/> is acted on.</param>
/// <param name="Failure">
/// <see langword="null"/>, or <see cref="NativeFailureKind.OwnerLoopFailed"/> / <see cref="NativeFailureKind.ProcessFatal"/>
/// when the native owner loop failed closed after or beside the events.
/// </param>
internal sealed record NativeDriveResult(IReadOnlyList<NativeCeremonyEvent> Events, NativeFailureKind? Failure);
