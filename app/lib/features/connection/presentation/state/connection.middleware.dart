import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Handles connection actions, resolving [NavigatorService] through the shared [sl] container.
class ConnectionMiddleware extends MiddlewareClass<AppState> {
  /// See [MiddlewareClass.call].
  @override
  void call(Store<AppState> store, dynamic action, NextDispatcher next) {
    next(action);

    switch (action) {
      case ConnectionBridgeSelectedAction _:
        _connectionBridgeSelected(store, action);
    }
  }

  /// Handles [ConnectionBridgeSelectedAction] by navigating to pairing.
  void _connectionBridgeSelected(
    Store<AppState> store,
    ConnectionBridgeSelectedAction action,
  ) {
    sl<NavigatorService>().go(AppRoutes.pairing);
  }
}
