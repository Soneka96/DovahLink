import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/bridge_list.screen.dart';
import 'package:dovahlink_client/features/pairing/presentation/screens/pairing.screen.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';

/// Builds the app's router. Every destination is a plain top-level [GoRoute] -- no
/// [StatefulShellRoute] and no route-dependent window behavior; DovahLink stays a normal
/// resizable window from startup regardless of the current route.
GoRouter createRouter() => GoRouter(
  initialLocation: AppRoutes.home,
  routes: [
    GoRoute(
      path: AppRoutes.home,
      builder: (BuildContext context, GoRouterState state) =>
          const BridgeListScreen(),
    ),
    GoRoute(
      path: AppRoutes.pairing,
      builder: (BuildContext context, GoRouterState state) =>
          const PairingScreen(),
    ),
  ],
);
