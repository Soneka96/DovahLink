/// Fails pending operations during connection teardown without exposing their ownership mechanism.
abstract interface class PendingOperationFailureHandler {
  /// Fails or orphans pending operations according to [orphanRetrySafeOperations].
  void failAll(Exception reason, {required bool orphanRetrySafeOperations});
}
