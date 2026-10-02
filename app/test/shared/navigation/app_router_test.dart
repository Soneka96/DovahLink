import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/connections.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/session/presentation/screens/session_shell.screen.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_shell.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/navigation/app_router.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import '../../fixtures/fixtures.dart';

/// Exercises the real router built by [createRouter] rather than mocking navigation.
void main() {
  setUp(() async {
    await sl.reset();
    await initDependencies();
  });

  tearDown(() async {
    await sl.reset();
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

    testWidgets('createRouter admits the exact connected Known Host', (
      WidgetTester tester,
    ) async {
      await sl.unregister<SessionShellViewModel>();
      String? resolvedHostId;
      sl.registerFactoryParam<SessionShellViewModel, Store<AppState>, String>((
        Store<AppState> _,
        String hostId,
      ) {
        resolvedHostId = hostId;
        return const SessionShellViewModel(host: null, onBack: _ignoreBack);
      });
      final GoRouter router = createRouter();
      final Store<AppState> store = _storeWithKnownHosts([
        Fixtures.buildKnownHost(
          host: Fixtures.buildHost(hostId: 'selected-host'),
          sessionState: KnownHostSessionState.connected,
        ),
      ]);

      await tester.pumpWidget(
        StoreProvider<AppState>(
          store: store,
          child: MaterialApp.router(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            routerConfig: router,
          ),
        ),
      );
      router.go(AppRoutes.sessionFor('selected-host'));
      await tester.pumpAndSettle();

      expect(find.byType(SessionShellScreen), findsOneWidget);
      expect(find.byType(ConnectionsScreen), findsNothing);
      expect(resolvedHostId, 'selected-host');
    });

    testWidgets(
      'createRouter redirects direct navigation when the requested Host is missing',
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

        router.go(AppRoutes.sessionFor('missing-host'));
        await tester.pumpAndSettle();

        expect(find.byType(ConnectionsScreen), findsOneWidget);
        expect(find.byType(SessionShellScreen), findsNothing);
      },
    );

    testWidgets(
      'createRouter redirects when the requested Known Host is disconnected',
      (WidgetTester tester) async {
        final GoRouter router = createRouter();
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: _storeWithKnownHosts([
              Fixtures.buildKnownHost(
                host: Fixtures.buildHost(hostId: 'requested-host'),
                sessionState: KnownHostSessionState.disconnected,
              ),
              Fixtures.buildKnownHost(
                host: Fixtures.buildHost(hostId: 'other-host'),
                sessionState: KnownHostSessionState.connected,
              ),
            ]),
            child: MaterialApp.router(
              theme: dovahThemeDataFor(defaultThemePreset),
              routerConfig: router,
            ),
          ),
        );

        router.go(AppRoutes.sessionFor('requested-host'));
        await tester.pumpAndSettle();

        expect(find.byType(ConnectionsScreen), findsOneWidget);
        expect(find.byType(SessionShellScreen), findsNothing);
      },
    );

    testWidgets(
      'createRouter rejects a connected Host when a different Host is requested',
      (WidgetTester tester) async {
        final GoRouter router = createRouter();
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: _storeWithKnownHosts([
              Fixtures.buildKnownHost(
                host: Fixtures.buildHost(hostId: 'connected-host'),
                sessionState: KnownHostSessionState.connected,
              ),
            ]),
            child: MaterialApp.router(
              theme: dovahThemeDataFor(defaultThemePreset),
              routerConfig: router,
            ),
          ),
        );

        router.go(AppRoutes.sessionFor('requested-host'));
        await tester.pumpAndSettle();

        expect(find.byType(ConnectionsScreen), findsOneWidget);
        expect(find.byType(SessionShellScreen), findsNothing);
      },
    );
  });
}

/// Builds a Redux store with the supplied Known Host projection.
/// @param knownHosts The current Known Host records exposed to the router.
/// @return A store whose connection state contains [knownHosts].
Store<AppState> _storeWithKnownHosts(List<KnownHost> knownHosts) {
  final Store<AppState> store = const CreateStore()();
  store.dispatch(ConnectionKnownHostsChangedAction(knownHosts));
  return store;
}

void _ignoreBack() {}
