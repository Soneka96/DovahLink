import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Thrown when the bridge reports a wire-level `error` message, or replies with an unexpected
/// message type.
class DovahLinkProtocolException implements Exception {
  /// Creates a protocol exception from the bridge's own reported failure fields, or a
  /// client-synthesized equivalent for an unexpected reply.
  const DovahLinkProtocolException({
    required this.code,
    required this.message,
    required this.retryable,
  });

  /// The canonical machine-readable failure code. For branching; never [message].
  final ProtocolErrorCode code;

  /// Diagnostic text. Never used for branching.
  final String message;

  /// Whether a fresh connection may retry.
  final bool retryable;

  /// Implements [Object.toString].
  @override
  String toString() =>
      'DovahLinkProtocolException($code, retryable: $retryable): $message';
}
