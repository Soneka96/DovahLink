import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/connection_status.screen.dart';
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
          const ConnectionStatusScreen(),
    ),
    GoRoute(
      path: AppRoutes.pairing,
      // Placeholder only: the real pairing UI (its own feature, wrapping the SDK's
      // DovahLinkClient) replaces this in a separate, later build.
      builder: (BuildContext context, GoRouterState state) => const Scaffold(
        key: Key('pairing-placeholder'),
        body: Center(child: Text('Pairing')),
      ),
    ),
  ],
);
