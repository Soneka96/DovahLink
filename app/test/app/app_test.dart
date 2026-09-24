import 'package:flutter/material.dart' hide ConnectionState;
import 'package:flutter/services.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/app/app.dart';
import 'package:dovahlink_client/app/app.viewmodel.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/connections.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import '../fixtures/fixtures.dart';

/// Mocks the authentication use case the real pairing middleware resolves.
class MockAuthenticateUseCase extends Mock implements AuthenticateUseCase {}

/// Mocks the disconnect use case the real pairing middleware resolves on dismissal.
class MockDisconnectUseCase extends Mock implements DisconnectUseCase {}

/// Exercises the root application shell before connection.
void main() {
  setUpAll(() {
    TestWidgetsFlutterBinding.ensureInitialized();
  });

  group('DovahLinkApp renders the initial Host list', () {
    testWidgets(
      'DovahLinkApp renders the Host list before a connection exists',
      (WidgetTester tester) async {
        await initDependencies();
        await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

        expect(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
          findsOneWidget,
        );
        expect(find.text('Local Host'), findsOneWidget);
      },
    );
  });

  group('DovahLinkApp opens pairing from Connections', () {
    late MockAuthenticateUseCase authenticate;
    late MockDisconnectUseCase disconnect;

    setUpAll(() {
      registerFallbackValue(Fixtures.buildAuthenticateParams());
      registerFallbackValue(NoParams());
    });

    setUp(() async {
      await sl.reset();
      await initDependencies();
      authenticate = MockAuthenticateUseCase();
      disconnect = MockDisconnectUseCase();
      when(() => authenticate(any())).thenAnswer(
        (_) async => Right(Fixtures.buildPairingHandshake(trusted: false)),
      );
      when(() => disconnect(any())).thenAnswer((_) async => const Right(unit));
      sl.unregister<AuthenticateUseCase>();
      sl.registerLazySingleton<AuthenticateUseCase>(() => authenticate);
      sl.unregister<DisconnectUseCase>();
      sl.registerLazySingleton<DisconnectUseCase>(() => disconnect);
    });

    tearDown(() async {
      await sl.reset();
    });

    /// Builds the real app over a real store with the real pairing middleware, over [hosts].
    Store<AppState> buildStore(List<Host> hosts) => const CreateStore()(
      middleware: [PairingMiddleware().call],
      initialState: AppState(
        connection: ConnectionState(hosts: hosts),
        pairing: PairingState.initial(),
      ),
    );

    testWidgets(
      'DovahLinkApp opens the pairing dialog, not a route, when a Host is tapped',
      (WidgetTester tester) async {
        final Store<AppState> store = buildStore([Fixtures.buildHost()]);
        await tester.pumpWidget(DovahLinkApp(store: store));

        await tester.tap(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
        );
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));

        expect(find.byType(DovahDialog), findsOneWidget);
        expect(find.text('Pair with Local Host'), findsOneWidget);
        expect(find.byType(PairingSection), findsOneWidget);
        expect(find.byType(ConnectionsScreen), findsOneWidget);
        expect(
          ConnectionSelectors.selectedHostSelector(store.state),
          Fixtures.buildHost(),
        );
      },
    );

    testWidgets(
      'DovahLinkApp authenticates with the second Host when two Hosts share a display name',
      (WidgetTester tester) async {
        final Host first = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://192.168.1.10:1000/'),
        );
        final Host second = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://192.168.1.11:2000/'),
        );
        await tester.binding.setSurfaceSize(const Size(1280, 900));
        addTearDown(() => tester.binding.setSurfaceSize(null));
        final Store<AppState> store = buildStore([first, second]);
        await tester.pumpWidget(DovahLinkApp(store: store));

        await tester.tap(
          find.byKey(const Key('host-card-ws://192.168.1.11:2000/')),
        );
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));

        verify(
          () => authenticate(AuthenticateParams(hostUri: second.uri)),
        ).called(1);
        verifyNever(() => authenticate(AuthenticateParams(hostUri: first.uri)));
        expect(find.text('Pair this device'), findsOneWidget);
        expect(
          ConnectionSelectors.selectedHostSelector(store.state)?.uri,
          second.uri,
        );
      },
    );

    testWidgets(
      'DovahLinkApp ends pairing and disconnects when the dialog is closed',
      (WidgetTester tester) async {
        final Store<AppState> store = buildStore([Fixtures.buildHost()]);
        await tester.pumpWidget(DovahLinkApp(store: store));
        await tester.tap(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
        );
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));
        expect(
          PairingSelectors.phaseSelector(store.state),
          PairingPhase.unpaired,
        );

        await tester.tap(find.byTooltip('Close'));
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));

        expect(find.byType(DovahDialog), findsNothing);
        expect(PairingSelectors.phaseSelector(store.state), PairingPhase.none);
        verify(() => disconnect(any())).called(1);
      },
    );

    testWidgets('DovahLinkApp closes the pairing dialog on Escape', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        DovahLinkApp(store: buildStore([Fixtures.buildHost()])),
      );
      await tester.tap(
        find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
      );
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));

      await tester.sendKeyEvent(LogicalKeyboardKey.escape);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));

      expect(find.byType(DovahDialog), findsNothing);
    });

    testWidgets(
      'DovahLinkApp starts a fresh pairing session when the dialog is reopened',
      (WidgetTester tester) async {
        final Store<AppState> store = buildStore([Fixtures.buildHost()]);
        await tester.pumpWidget(DovahLinkApp(store: store));
        final Finder card = find.byKey(
          const Key('host-card-ws://127.0.0.1:58231/'),
        );

        await tester.tap(card);
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));
        await tester.tap(find.byTooltip('Close'));
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));
        await tester.tap(card);
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));

        expect(find.byType(DovahDialog), findsOneWidget);
        expect(find.text('Pair this device'), findsOneWidget);
        verify(() => authenticate(any())).called(2);
      },
    );
  });

  group('DovahLinkApp uses the shared router', () {
    testWidgets(
      'DovahLinkApp navigates through the same router instance backing the app shell',
      (WidgetTester tester) async {
        await initDependencies();
        await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

        sl<NavigatorService>().go('/somewhere-else');
        await tester.pumpAndSettle();

        expect(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
          findsNothing,
        );
      },
    );
  });

  group('DovahLinkApp theme', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahLinkApp renders the active $preset preset\'s ThemeData',
        (WidgetTester tester) async {
          await initDependencies();
          final store = const CreateStore()(
            initialState: AppState.initial(
              appearance: AppearanceState(activePreset: preset),
            ),
          );

          await tester.pumpWidget(DovahLinkApp(store: store));

          final MaterialApp app = tester.widget(find.byType(MaterialApp));
          final DovahThemeTokens? tokens = app.theme
              ?.extension<DovahThemeTokens>();

          expect(tokens, isA<DovahThemeTokens>());
          expect(
            tokens,
            dovahThemeDataFor(preset).extension<DovahThemeTokens>(),
          );
        },
      );
    }

    testWidgets(
      'DovahLinkApp updates ThemeData reactively when the active preset changes',
      (WidgetTester tester) async {
        await initDependencies();
        final store = const CreateStore()();

        await tester.pumpWidget(DovahLinkApp(store: store));

        store.dispatch(
          const ThemePresetSelectedAction(DovahThemePreset.frostbound),
        );
        await tester.pump();

        final MaterialApp app = tester.widget(find.byType(MaterialApp));
        final DovahThemeTokens? tokens = app.theme
            ?.extension<DovahThemeTokens>();

        expect(
          tokens,
          dovahThemeDataFor(
            DovahThemePreset.frostbound,
          ).extension<DovahThemeTokens>(),
        );
      },
    );

    testWidgets(
      'DovahLinkApp keeps ThemeData when unrelated Redux state changes',
      (WidgetTester tester) async {
        await initDependencies();
        final store = const CreateStore()(
          initialState: AppState.initial(
            appearance: const AppearanceState(
              activePreset: DovahThemePreset.hearth,
            ),
          ),
        );
        await tester.pumpWidget(DovahLinkApp(store: store));

        final StoreConnector<AppState, DovahLinkAppViewModel> connector = tester
            .widget(
              find.byType(StoreConnector<AppState, DovahLinkAppViewModel>),
            );
        final ThemeData initialTheme = tester
            .widget<MaterialApp>(find.byType(MaterialApp))
            .theme!;

        expect(connector.distinct, isTrue);
        expect(
          connector.converter(store).activePreset,
          DovahThemePreset.hearth,
        );

        store.dispatch(const PairingStartedAction());
        expect(
          PairingSelectors.phaseSelector(store.state),
          PairingPhase.connecting,
        );
        await tester.pump();

        final ThemeData updatedTheme = tester
            .widget<MaterialApp>(find.byType(MaterialApp))
            .theme!;
        expect(identical(updatedTheme, initialTheme), isTrue);
      },
    );
  });
}
