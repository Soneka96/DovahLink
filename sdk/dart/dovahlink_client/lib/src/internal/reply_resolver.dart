import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';

/// Resolves one inbound reply against a pending request by correlation ID.
abstract interface class ReplyResolver {
  /// Completes the pending operation identified by [correlationId], if present.
  bool resolveReply(String correlationId, Envelope envelope);
}
