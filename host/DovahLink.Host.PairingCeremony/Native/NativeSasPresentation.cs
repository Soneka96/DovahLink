namespace DovahLink.Host.PairingCeremony.Native;

/// <summary>A run's current SAS presentation as detached values.</summary>
/// <param name="CeremonyIdentity">The 32-byte ceremony identity the presentation is bound to.</param>
/// <param name="DecimalDisplay">The <c>NNNN NNNN NNNN</c> display.</param>
internal sealed record NativeSasPresentation(byte[] CeremonyIdentity, string DecimalDisplay);
