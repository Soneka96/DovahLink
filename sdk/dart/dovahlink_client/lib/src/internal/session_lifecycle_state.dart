import 'dart:async';

/// The connection-state commands a teardown coordinator needs from its owning session, without
/// taking ownership of the session's socket-scoped state.
abstract interface class SessionLifecycleState {
  /// Whether the current session has already reached terminal administrative invalidation.
  bool get isAdministrativelyInvalidated;

  /// The generation currently associated with the active or tearing-down connection.
  int get connectionGeneration;

  /// Advances the connection generation so callbacks from an older connection become stale.
  void bumpConnectionGeneration();

  /// Detaches and returns the one subscription currently owned by the session, if any.
  StreamSubscription<String>? detachMessageSubscription();

  /// Resets connection-scoped state after transport resources have been closed.
  void resetAfterConnectionTeardown();
}
