namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// Names one Responder ceremony attempt within one <see cref="PairingCeremonyHost"/> before its
/// ceremony identity exists. It is a local, process-lifetime counter value: never a request ID, peer
/// identity, network location, or anything that authorizes trust.
/// </summary>
/// <param name="Value">The attempt's sequence number within its host.</param>
public readonly record struct CeremonyAttemptId(long Value);
