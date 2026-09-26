/// Thrown when establishing or maintaining the transport or WebSocket handshake fails.
class DovahLinkConnectionException implements Exception {
  /// Creates a connection exception with diagnostic [message] and optional HTTP response status.
  /// @param message A diagnostic description of the failure.
  /// @param httpStatusCode The status returned by a peer that rejected a WebSocket upgrade.
  const DovahLinkConnectionException(this.message, {this.httpStatusCode});

  /// A diagnostic description of the connection failure. Never a raw exception or infrastructure
  /// detail; safe to log.
  final String message;

  /// The HTTP status returned while negotiating a WebSocket upgrade, when one was received.
  final int? httpStatusCode;

  /// Implements [Object.toString].
  @override
  String toString() => 'DovahLinkConnectionException: $message';
}
