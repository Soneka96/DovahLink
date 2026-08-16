/// Thrown when a wire message fails to decode as its expected shape.
class ProtocolFormatException implements Exception {
  /// Creates a protocol format exception with a diagnostic [message].
  const ProtocolFormatException(this.message);

  /// A diagnostic description of the decoding failure. Never a raw exception
  /// or infrastructure detail; safe to log.
  final String message;

  @override
  String toString() => 'ProtocolFormatException: $message';
}
