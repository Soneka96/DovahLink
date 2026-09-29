/// Thrown when an operation names a Host that is not in the SDK's Known Hosts.
final class DovahLinkKnownHostNotFoundException implements Exception {
  /// The identifier that did not resolve to a Known Host.
  final String hostId;

  /// Creates an exception for an unknown [hostId].
  /// @param hostId The identifier that did not resolve.
  const DovahLinkKnownHostNotFoundException(this.hostId);

  /// Implements [Object.toString].
  @override
  String toString() => 'DovahLinkKnownHostNotFoundException($hostId)';
}
