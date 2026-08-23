/// Thrown when establishing or maintaining the transport connection fails -- a socket-level
/// problem, not a bridge-reported failure.
class DovahLinkConnectionException implements Exception {
  /// Creates a connection exception with a diagnostic [message].
  const DovahLinkConnectionException(this.message);

  /// A diagnostic description of the connection failure. Never a raw exception or infrastructure
  /// detail; safe to log.
  final String message;

  /// Implements [Object.toString].
  @override
  String toString() => 'DovahLinkConnectionException: $message';
}
