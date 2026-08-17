import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/connection_status.screen.dart';
import 'package:dovahlink_client/features/pairing/presentation/screens/pairing.screen.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_router.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Exercises the real router built by [createRouter] rather than mocking navigation.
void main() {
  setUp(initDependencies);

  group('createRouter', () {
    testWidgets('resolves the home route to ConnectionStatusScreen', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        StoreProvider<AppState>(
          store: const CreateStore()(),
          child: MaterialApp.router(routerConfig: createRouter()),
        ),
      );

      expect(find.byType(ConnectionStatusScreen), findsOneWidget);
      expect(find.byType(PairingScreen), findsNothing);
    });

    testWidgets('resolves the pairing route to PairingScreen', (
      WidgetTester tester,
    ) async {
      final GoRouter router = createRouter();
      await tester.pumpWidget(
        StoreProvider<AppState>(
          store: const CreateStore()(),
          child: MaterialApp.router(routerConfig: router),
        ),
      );

      router.go(AppRoutes.pairing);
      // Not pumpAndSettle: PairingScreen auto-starts a real connection attempt
      // with no bridge listening in this test, so it retries forever by
      // design and never quiesces. The route-transition duration is enough
      // to mount the destination screen, which is all this asserts.
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));

      expect(find.byType(PairingScreen), findsOneWidget);
      expect(find.byType(ConnectionStatusScreen), findsNothing);
    });

    testWidgets(
      'an unmatched path falls back safely instead of resolving a known screen',
      (WidgetTester tester) async {
        final GoRouter router = createRouter();
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: const CreateStore()(),
            child: MaterialApp.router(routerConfig: router),
          ),
        );

        router.go('/does-not-exist');
        await tester.pumpAndSettle();

        expect(find.byType(ConnectionStatusScreen), findsNothing);
        expect(find.byType(PairingScreen), findsNothing);
      },
    );
  });
}
