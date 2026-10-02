import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Defines navigation for an admitted Host session.
abstract interface class ISessionShellMiddleware {
  /// Handles one Redux [action] after it reaches the reducer.
  void call(Store<AppState> store, dynamic action, NextDispatcher next);
}

/// Enters the Session Shell only after the selected Known Host is admitted as connected.
class SessionShellMiddleware extends MiddlewareClass<AppState>
    implements ISessionShellMiddleware {
  /// The app's approved navigation boundary.
  final NavigatorService _navigator;

  /// Host whose successful trusted session is waiting for SDK admitted-state evidence.
  String? _pendingHostId;

  /// Creates session navigation middleware.
  SessionShellMiddleware(this._navigator);

  /// See [MiddlewareClass.call].
  @override
  void call(Store<AppState> store, dynamic action, NextDispatcher next) {
    next(action);
    switch (action) {
      case ConnectionHostSelectedAction _:
        _connectionHostSelected();
      case PairingStartedAction _:
        _pairingStarted();
      case PairingSessionTrustedAction _:
        _pairingSessionTrusted(store);
      case ConnectionKnownHostsChangedAction _:
        _connectionKnownHostsChanged(store);
      case PairingFailedAction _:
        _pairingFailed();
      case PairingDisposedAction(wasTrusted: false):
        _pairingDisposed();
      case SessionShellBackRequestedAction _:
        _sessionShellBackRequested();
      case final ConnectionHostReentryRequestedAction reentryAction:
        _connectionHostReentryRequested(store, reentryAction);
    }
  }

  /// Clears a pending shell handoff when the user selects a Host.
  void _connectionHostSelected() => _pendingHostId = null;

  /// Clears a pending handoff when a new pairing flow begins.
  void _pairingStarted() => _pendingHostId = null;

  /// Records the trusted Host and waits for SDK admission before navigation.
  /// @param store The application store containing the selected Host.
  void _pairingSessionTrusted(Store<AppState> store) {
    _pendingHostId = ConnectionSelectors.selectedHostSelector(
      store.state,
    )?.hostId;
    _enterWhenConnected(store);
  }

  /// Rechecks pending navigation after the SDK publishes Known Host state.
  /// @param store The application store containing the latest SDK projection.
  void _connectionKnownHostsChanged(Store<AppState> store) =>
      _enterWhenConnected(store);

  /// Cancels pending shell navigation when pairing fails.
  void _pairingFailed() => _pendingHostId = null;

  /// Cancels pending shell navigation when pairing is disposed before trust.
  void _pairingDisposed() => _pendingHostId = null;

  /// Returns to Connections without disconnecting an admitted session.
  void _sessionShellBackRequested() {
    _pendingHostId = null;
    _navigator.go(AppRoutes.home);
  }

  /// Opens the existing Session Shell without starting another authentication lifecycle.
  /// @param store The application store containing SDK-owned Known Host session state.
  /// @param action The Connected card request for its Host ID.
  void _connectionHostReentryRequested(
    Store<AppState> store,
    ConnectionHostReentryRequestedAction action,
  ) {
    if (!_isKnownHostConnected(store, action.hostId)) {
      return;
    }
    _pendingHostId = null;
    _navigator.go(AppRoutes.sessionFor(action.hostId));
  }

  /// Replaces Connections with the shell only when SDK state admits the exact trusted Host.
  /// @param store The application store containing Known Host session state.
  void _enterWhenConnected(Store<AppState> store) {
    final String? hostId = _pendingHostId;
    if (hostId == null) {
      return;
    }
    if (!_isKnownHostConnected(store, hostId)) {
      return;
    }
    _pendingHostId = null;
    _navigator.go(AppRoutes.sessionFor(hostId));
  }

  /// Returns whether the SDK projection admits [hostId] as the connected Known Host.
  /// @param store The application store containing the latest SDK projection.
  /// @param hostId The stable Host ID to check.
  /// @return Whether the matching Known Host session is connected.
  bool _isKnownHostConnected(Store<AppState> store, String hostId) =>
      store.state.connection.knownHosts.any(
        (knownHost) =>
            knownHost.host.hostId == hostId &&
            knownHost.sessionState == KnownHostSessionState.connected,
      );
}
