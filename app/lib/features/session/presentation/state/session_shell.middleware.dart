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
      case PairingStartedAction _:
        _pendingHostId = null;
      case PairingSessionTrustedAction _:
        _pendingHostId = ConnectionSelectors.selectedHostSelector(
          store.state,
        )?.hostId;
        _enterWhenConnected(store);
      case ConnectionKnownHostsChangedAction _:
        _enterWhenConnected(store);
      case PairingFailedAction _:
        _pendingHostId = null;
      case PairingDisposedAction(wasTrusted: false):
        _pendingHostId = null;
      case SessionShellBackRequestedAction _:
        _pendingHostId = null;
        _navigator.go(AppRoutes.home);
    }
  }

  /// Replaces Connections with the shell only when SDK state admits the exact trusted Host.
  void _enterWhenConnected(Store<AppState> store) {
    final String? hostId = _pendingHostId;
    if (hostId == null) {
      return;
    }
    final bool isConnected = store.state.connection.knownHosts.any(
      (knownHost) =>
          knownHost.host.hostId == hostId &&
          knownHost.sessionState == KnownHostSessionState.connected,
    );
    if (!isConnected) {
      return;
    }
    _pendingHostId = null;
    _navigator.go(AppRoutes.sessionFor(hostId));
  }
}
