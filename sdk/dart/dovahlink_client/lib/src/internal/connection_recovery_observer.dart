/// Notified when ordinary transport loss finishes tearing down the connection, so bounded
/// automatic reconnect may begin -- see `ai/context/sdk/architecture.md`'s "Internal composition".
/// Implemented by `ReconnectCoordinator`. Never notified for a deliberate disconnect or an
/// administrative invalidation; both require explicit, user-initiated recovery instead.
abstract interface class ConnectionRecoveryObserver {
  /// Reports that ordinary transport loss finished tearing down the connection previously
  /// established at [uri].
  void onOrdinaryTransportLoss(Uri uri);
}
