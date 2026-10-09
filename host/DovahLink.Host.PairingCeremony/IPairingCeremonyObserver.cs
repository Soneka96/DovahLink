namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// Receives what a <see cref="PairingCeremonyHost"/> needs a decision on or has to report. Every
/// method is called on the host's owner thread and must return quickly without blocking; a decision is
/// returned later through <see cref="IPairingCeremonyHost"/>, never by blocking a call. An exception a
/// method throws is ignored by the host.
/// </summary>
public interface IPairingCeremonyObserver
{
    /// <summary>
    /// A peer's key arrived for an attempt, so this Host may now reveal its own; the host exposes
    /// nothing until <see cref="IPairingCeremonyHost.TryAuthorizeExposure"/> names this attempt.
    /// </summary>
    /// <param name="attempt">The attempt awaiting fresh, explicit local authorization.</param>
    void OnExposureAuthorizationRequested(CeremonyAttemptId attempt);

    /// <summary>
    /// A SAS is ready for human comparison; the host approves nothing until
    /// <see cref="IPairingCeremonyHost.TrySubmitSasDecision"/> names its exact ceremony identity.
    /// </summary>
    /// <param name="request">The SAS to compare and the ceremony identity a decision must name.</param>
    void OnSasComparisonRequested(SasComparisonRequest request);

    /// <summary>This endpoint completed one ceremony locally; the snapshot is detached and establishes no trust.</summary>
    /// <param name="attempt">The attempt the result belongs to, or <see langword="null"/> when its run was no longer tracked.</param>
    /// <param name="result">The detached result data.</param>
    void OnCeremonyCompletedLocally(CeremonyAttemptId? attempt, CeremonyResultSnapshot result);

    /// <summary>An attempt ended without a local result.</summary>
    /// <param name="attempt">The ended attempt.</param>
    void OnAttemptEnded(CeremonyAttemptId attempt);

    /// <summary>The host failed closed and will do no more pairing work.</summary>
    /// <param name="failure">Why it failed.</param>
    /// <param name="processRestartRequired">Whether new pairing work needs an OS process restart.</param>
    void OnFailed(PairingCeremonyFailure failure, bool processRestartRequired);
}
