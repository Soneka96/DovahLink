/// A safe client-boundary error for invalid protocol data.
class ProtocolFormatException extends FormatException {
  /// Creates an exception with a non-sensitive diagnostic message.
  ProtocolFormatException(super.message);
}
