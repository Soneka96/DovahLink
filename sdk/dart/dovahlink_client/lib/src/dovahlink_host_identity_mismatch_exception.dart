/// Thrown when a session reported as trusted claims a different Host ID from the one this client
/// previously stored.
final class DovahLinkHostIdentityMismatchException implements Exception {
  /// Creates a mismatch error containing the stored and reported Host IDs.
  /// @param knownHostId The Host ID already stored by this client.
  /// @param reportedHostId The Host ID reported by the trusted session.
  const DovahLinkHostIdentityMismatchException({
    required this.knownHostId,
    required this.reportedHostId,
  });

  /// The Host ID this client previously stored.
  final String knownHostId;

  /// The Host ID reported by the trusted session.
  final String reportedHostId;

  /// Describes the expected and reported Host IDs.
  @override
  String toString() =>
      'DovahLinkHostIdentityMismatchException: known Host $knownHostId, '
      'reported Host $reportedHostId.';
}
