import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/connections.screen.dart';
import 'package:dovahlink_client/features/session/presentation/screens/session_shell.screen.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Builds the app's router. Every destination is a plain top-level [GoRoute] -- no
/// [StatefulShellRoute] and no route-dependent window behavior; DovahLink stays a normal
/// resizable window from startup regardless of the current route.
GoRouter createRouter() => GoRouter(
  initialLocation: AppRoutes.home,
  routes: [
    GoRoute(
      path: AppRoutes.home,
      builder: (BuildContext context, GoRouterState state) =>
          const ConnectionsScreen(),
    ),
    GoRoute(
      path: AppRoutes.session,
      redirect: (BuildContext context, GoRouterState state) {
        final String? hostId = state.pathParameters['hostId'];
        final bool isConnectedKnownHost =
            hostId != null &&
            StoreProvider.of<AppState>(
              context,
              listen: false,
            ).state.connection.knownHosts.any(
              (knownHost) =>
                  knownHost.host.hostId == hostId &&
                  knownHost.sessionState == KnownHostSessionState.connected,
            );
        return isConnectedKnownHost ? null : AppRoutes.home;
      },
      builder: (BuildContext context, GoRouterState state) =>
          SessionShellScreen(hostId: state.pathParameters['hostId']!),
    ),
  ],
);
