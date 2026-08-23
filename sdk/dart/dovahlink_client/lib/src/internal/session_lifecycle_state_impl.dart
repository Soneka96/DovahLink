import 'dart:async';

import 'package:dovahlink_client_sdk/src/internal/session_lifecycle_state.dart';
import 'package:dovahlink_client_sdk/src/internal/session_state.dart';

/// Implements [SessionLifecycleState] by delegating to [SessionState], the single authoritative
/// session-state owner -- so `ConnectionTeardownCoordinator` (itself unchanged internally, and
/// still built against the pre-decomposition `SessionLifecycleState` port) can be handed a real,
/// distinct object under its own true identity, rather than `SessionServiceImpl` implementing this
/// port itself and passing `this`. `SessionState` itself stays a plain, no-interface supporting
/// object per `ai/context/sdk/architecture.md`'s "Naming"; this class exists so neither it nor
/// `SessionServiceImpl` has to carry that identity directly. Eliminated, along with
/// `SessionLifecycleState` and `ClientSession`, at the composition-root cutover.
class SessionLifecycleStateImpl implements SessionLifecycleState {
  /// The single authoritative session-state owner this view reads and mutates.
  final SessionState _state;

  /// Creates a lifecycle-state view over [state].
  SessionLifecycleStateImpl(this._state);

  /// Implements [SessionLifecycleState.isAdministrativelyInvalidated].
  @override
  bool get isAdministrativelyInvalidated =>
      _state.isAdministrativelyInvalidated;

  /// Implements [SessionLifecycleState.connectionGeneration].
  @override
  int get connectionGeneration => _state.connectionGeneration;

  /// Implements [SessionLifecycleState.bumpConnectionGeneration].
  @override
  void bumpConnectionGeneration() => _state.bumpGeneration();

  /// Implements [SessionLifecycleState.detachMessageSubscription].
  @override
  StreamSubscription<String>? detachMessageSubscription() =>
      _state.detachMessageSubscription();

  /// Implements [SessionLifecycleState.resetAfterConnectionTeardown].
  @override
  void resetAfterConnectionTeardown({required bool preserveReconnecting}) =>
      _state.resetAfterTeardown(preserveReconnecting: preserveReconnecting);
}
