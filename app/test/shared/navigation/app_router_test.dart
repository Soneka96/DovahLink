import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/connections.screen.dart';
import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/navigation/app_router.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises the real router built by [createRouter] rather than mocking navigation.
void main() {
  setUp(() {
    return initDependencies();
  });

  group('createRouter', () {
    testWidgets('createRouter resolves the home route to ConnectionsScreen', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        StoreProvider<AppState>(
          store: const CreateStore()(),
          child: MaterialApp.router(
            theme: dovahThemeDataFor(defaultThemePreset),
            routerConfig: createRouter(),
          ),
        ),
      );

      expect(find.byType(ConnectionsScreen), findsOneWidget);
      expect(find.byType(PairingSection), findsNothing);
    });

    testWidgets(
      'createRouter no longer resolves a standalone pairing route, since pairing is a dialog',
      (WidgetTester tester) async {
        final GoRouter router = createRouter();
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: const CreateStore()(),
            child: MaterialApp.router(
              theme: dovahThemeDataFor(defaultThemePreset),
              routerConfig: router,
            ),
          ),
        );

        router.go('/pairing');
        await tester.pumpAndSettle();

        expect(find.byType(ConnectionsScreen), findsNothing);
        expect(find.byType(PairingSection), findsNothing);
      },
    );

    testWidgets(
      'createRouter falls back safely for an unmatched path instead of resolving a known screen',
      (WidgetTester tester) async {
        final GoRouter router = createRouter();
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: const CreateStore()(),
            child: MaterialApp.router(
              theme: dovahThemeDataFor(defaultThemePreset),
              routerConfig: router,
            ),
          ),
        );

        router.go('/does-not-exist');
        await tester.pumpAndSettle();

        expect(find.byType(ConnectionsScreen), findsNothing);
        expect(find.byType(PairingSection), findsNothing);
      },
    );
  });
}
