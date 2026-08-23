import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';

/// Owns registration of pending-operation entries before their wire attempt is sent.
abstract interface class PendingOperationRegistry {
  /// Associates [messageId] with [operation] before its wire attempt is sent.
  void register(String messageId, PendingOperation operation);
}
