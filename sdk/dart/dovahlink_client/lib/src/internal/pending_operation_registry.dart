import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';

/// Owns registration and terminal failure of pending-operation entries.
abstract interface class PendingOperationRegistry {
  /// Associates [messageId] with [operation] before its wire attempt is sent.
  void register(String messageId, PendingOperation operation);

  /// Removes [messageId] when [operation] fails before a correlated reply arrives.
  void fail(String messageId, PendingOperation operation, Exception reason);
}
