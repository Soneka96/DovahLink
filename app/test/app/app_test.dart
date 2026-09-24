import 'dart:async';

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
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/observe_connection_status.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/confirm_pairing_code.params.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_dialog.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import '../fixtures/fixtures.dart';
import '../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Mocks the authentication use case the real pairing middleware resolves.
class MockAuthenticateUseCase extends Mock implements AuthenticateUseCase {}

/// Mocks the disconnect use case the real pairing middleware resolves on dismissal.
class MockDisconnectUseCase extends Mock implements DisconnectUseCase {}

/// Mocks the code-request use case the real pairing middleware resolves after an unpaired
/// authentication.
class MockRequestPairingUseCase extends Mock implements RequestPairingUseCase {}

/// Mocks the confirmation use case the real pairing middleware resolves when a code is entered.
class MockConfirmPairingCodeUseCase extends Mock
    implements ConfirmPairingCodeUseCase {}

/// Mocks the status-observation use case the real pairing middleware resolves once trusted.
class MockObserveConnectionStatusUseCase extends Mock
    implements ObserveConnectionStatusUseCase {}

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
    late MockRequestPairingUseCase requestPairing;

    setUpAll(() {
      registerFallbackValue(Fixtures.buildAuthenticateParams());
      registerFallbackValue(NoParams());
    });

    setUp(() async {
      await sl.reset();
      await initDependencies();
      authenticate = MockAuthenticateUseCase();
      disconnect = MockDisconnectUseCase();
      requestPairing = MockRequestPairingUseCase();
      when(() => authenticate(any())).thenAnswer(
        (_) async => Right(Fixtures.buildPairingHandshake(trusted: false)),
      );
      when(() => disconnect(any())).thenAnswer((_) async => const Right(unit));
      when(
        () => requestPairing(any()),
      ).thenAnswer((_) async => const Right(300));
      sl.unregister<AuthenticateUseCase>();
      sl.registerLazySingleton<AuthenticateUseCase>(() => authenticate);
      sl.unregister<DisconnectUseCase>();
      sl.registerLazySingleton<DisconnectUseCase>(() => disconnect);
      sl.unregister<RequestPairingUseCase>();
      sl.registerLazySingleton<RequestPairingUseCase>(() => requestPairing);
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
        // Selecting the Host already expresses the intent to pair, so the code is requested
        // without a further step and the dialog reaches code entry.
        verify(() => requestPairing(any())).called(1);
        expect(find.text('Check Skyrim'), findsOneWidget);
        expect(
          find.byKey(const Key('pairing-request-code-button')),
          findsNothing,
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
        expect(find.text('Check Skyrim'), findsOneWidget);
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
          PairingPhase.awaitingCode,
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
        expect(find.text('Check Skyrim'), findsOneWidget);
        verify(() => authenticate(any())).called(2);
      },
    );
  });

  group('DovahLinkApp pairing dialog follows the pairing state', () {
    late MockAuthenticateUseCase authenticate;
    late MockDisconnectUseCase disconnect;
    late MockRequestPairingUseCase requestPairing;
    late MockConfirmPairingCodeUseCase confirm;
    late MockObserveConnectionStatusUseCase observe;

    const Key hostCard = Key('host-card-ws://127.0.0.1:58231/');

    setUpAll(() {
      registerFallbackValue(Fixtures.buildAuthenticateParams());
      registerFallbackValue(NoParams());
      registerFallbackValue(const ConfirmPairingCodeParams(code: '000000'));
    });

    setUp(() async {
      await sl.reset();
      await initDependencies();
      authenticate = MockAuthenticateUseCase();
      disconnect = MockDisconnectUseCase();
      requestPairing = MockRequestPairingUseCase();
      confirm = MockConfirmPairingCodeUseCase();
      observe = MockObserveConnectionStatusUseCase();
      when(() => authenticate(any())).thenAnswer(
        (_) async => Right(Fixtures.buildPairingHandshake(trusted: false)),
      );
      when(() => disconnect(any())).thenAnswer((_) async => const Right(unit));
      when(
        () => requestPairing(any()),
      ).thenAnswer((_) async => const Right(300));
      when(() => confirm(any())).thenAnswer((_) async => const Right(unit));
      when(
        () => observe(any()),
      ).thenAnswer((_) => const Stream<PairingConnectionStatus>.empty());
      sl.unregister<AuthenticateUseCase>();
      sl.registerLazySingleton<AuthenticateUseCase>(() => authenticate);
      sl.unregister<DisconnectUseCase>();
      sl.registerLazySingleton<DisconnectUseCase>(() => disconnect);
      sl.unregister<RequestPairingUseCase>();
      sl.registerLazySingleton<RequestPairingUseCase>(() => requestPairing);
      sl.unregister<ConfirmPairingCodeUseCase>();
      sl.registerLazySingleton<ConfirmPairingCodeUseCase>(() => confirm);
      sl.unregister<ObserveConnectionStatusUseCase>();
      sl.registerLazySingleton<ObserveConnectionStatusUseCase>(() => observe);
    });

    tearDown(() async {
      await sl.reset();
    });

    /// Builds the real app over a real store with the real pairing middleware and one Host.
    Store<AppState> buildStore() => const CreateStore()(
      middleware: [PairingMiddleware().call],
      initialState: AppState(
        connection: ConnectionState(hosts: [Fixtures.buildHost()]),
        pairing: PairingState.initial(),
      ),
    );

    /// Advances time enough for dispatched actions, rebuilds, and route transitions, without
    /// settling, because pairing progress spinners animate forever.
    Future<void> settle(WidgetTester tester) async {
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));
    }

    /// Pumps the app and taps the Host card, opening the pairing dialog.
    Future<Store<AppState>> openPairing(WidgetTester tester) async {
      final Store<AppState> store = buildStore();
      await tester.pumpWidget(DovahLinkApp(store: store));
      await tester.tap(find.byKey(hostCard));
      await settle(tester);
      return store;
    }

    /// Types [code] into the open code-entry field and taps Pair.
    Future<void> submitCode(WidgetTester tester, String code) async {
      await tester.enterText(find.byKey(const Key('pairing-code-field')), code);
      await tester.pump();
      await tester.tap(find.byKey(const Key('pairing-confirm-button')));
      await settle(tester);
    }

    /// The dialog header's title text.
    Finder headerTitle(String title) => find.descendant(
      of: find.byKey(const Key('dovah-dialog-header')),
      matching: find.text(title),
    );

    testWidgets(
      'DovahLinkApp walks connecting, requesting a code, and code entry without a manual request step',
      (WidgetTester tester) async {
        final Completer<Either<Failure, PairingHandshake>> handshake =
            Completer<Either<Failure, PairingHandshake>>();
        final Completer<Either<Failure, int?>> code =
            Completer<Either<Failure, int?>>();
        when(() => authenticate(any())).thenAnswer((_) => handshake.future);
        when(() => requestPairing(any())).thenAnswer((_) => code.future);

        await openPairing(tester);

        expect(headerTitle('Pair with Local Host'), findsOneWidget);
        expect(find.text('Connecting…'), findsOneWidget);
        verifyNever(() => requestPairing(any()));

        handshake.complete(
          Right(Fixtures.buildPairingHandshake(trusted: false)),
        );
        await settle(tester);

        verify(() => requestPairing(any())).called(1);
        expect(headerTitle('Pair with Local Host'), findsOneWidget);
        expect(find.text('Requesting code…'), findsOneWidget);
        expect(
          find.byKey(const Key('pairing-request-code-button')),
          findsNothing,
        );

        code.complete(const Right(300));
        await settle(tester);

        expect(headerTitle('Pair with Local Host'), findsOneWidget);
        expect(find.text('Check Skyrim'), findsOneWidget);
        verifyNoMoreInteractions(requestPairing);
      },
    );

    testWidgets(
      'DovahLinkApp titles the dialog Skyrim isn’t running while the Host is unreachable and recovers when it returns',
      (WidgetTester tester) async {
        when(
          () => authenticate(any()),
        ).thenAnswer((_) async => const Left(NetworkFailure('unreachable')));

        await openPairing(tester);

        expect(headerTitle('Skyrim isn’t running'), findsOneWidget);
        expect(find.text('Local Host is offline'), findsOneWidget);
        expect(find.textContaining('Start Skyrim'), findsOneWidget);

        when(() => authenticate(any())).thenAnswer(
          (_) async => Right(Fixtures.buildPairingHandshake(trusted: false)),
        );
        await tester.pump(const Duration(seconds: 3));
        await settle(tester);

        expect(headerTitle('Skyrim isn’t running'), findsNothing);
        expect(headerTitle('Pair with Local Host'), findsOneWidget);
        expect(find.text('Check Skyrim'), findsOneWidget);
      },
    );

    testWidgets(
      'DovahLinkApp titles the dialog Connected and keeps trust when Done is tapped after pairing',
      (WidgetTester tester) async {
        final Store<AppState> store = await openPairing(tester);

        await submitCode(tester, '123456');

        expect(headerTitle('Connected'), findsOneWidget);
        expect(find.text('You’re connected'), findsOneWidget);
        verify(
          () => confirm(const ConfirmPairingCodeParams(code: '123456')),
        ).called(1);

        await tester.tap(find.byKey(const Key('pairing-done-button')));
        await settle(tester);

        expect(find.byType(PairingDialog), findsNothing);
        expect(PairingSelectors.phaseSelector(store.state), PairingPhase.none);
        verifyNever(() => disconnect(any()));
      },
    );

    testWidgets(
      'DovahLinkApp titles the dialog Pairing required and waits for Pair again after a rejected credential',
      (WidgetTester tester) async {
        when(() => authenticate(any())).thenAnswer(
          (_) async => Right(
            Fixtures.buildPairingHandshake(
              trusted: false,
              credentialRejectionReason:
                  PairingCredentialRejectionReason.revoked,
              credentialRejectedMessage: "This device's trust was revoked.",
            ),
          ),
        );

        await openPairing(tester);

        expect(headerTitle('Pairing required'), findsOneWidget);
        expect(find.text('Pair Local Host again'), findsOneWidget);
        expect(find.text("This device's trust was revoked."), findsOneWidget);
        expect(find.text('Cancel'), findsOneWidget);
        verifyNever(() => requestPairing(any()));

        await tester.tap(find.byKey(const Key('pairing-request-code-button')));
        await settle(tester);

        verify(() => requestPairing(any())).called(1);
        expect(headerTitle('Pairing required'), findsNothing);
        expect(headerTitle('Pair with Local Host'), findsOneWidget);
        expect(find.text('Check Skyrim'), findsOneWidget);
      },
    );

    testWidgets(
      'DovahLinkApp requests no code and disconnects when Cancel is tapped on the pair-again prompt',
      (WidgetTester tester) async {
        when(() => authenticate(any())).thenAnswer(
          (_) async => Right(
            Fixtures.buildPairingHandshake(
              trusted: false,
              credentialRejectionReason:
                  PairingCredentialRejectionReason.revoked,
              credentialRejectedMessage: "This device's trust was revoked.",
            ),
          ),
        );
        final Store<AppState> store = await openPairing(tester);

        await tester.tap(find.byKey(const Key('pairing-repair-cancel-button')));
        await settle(tester);

        expect(find.byType(PairingDialog), findsNothing);
        expect(PairingSelectors.phaseSelector(store.state), PairingPhase.none);
        verifyNever(() => requestPairing(any()));
        verify(() => disconnect(any())).called(1);
      },
    );

    testWidgets(
      'DovahLinkApp cannot be dismissed while a code is being confirmed',
      (WidgetTester tester) async {
        final Completer<Either<Failure, Unit>> confirmation =
            Completer<Either<Failure, Unit>>();
        when(() => confirm(any())).thenAnswer((_) => confirmation.future);
        final Store<AppState> store = await openPairing(tester);

        await submitCode(tester, '123456');
        expect(
          PairingSelectors.phaseSelector(store.state),
          PairingPhase.confirming,
        );

        await tester.tap(find.byTooltip('Close'));
        await settle(tester);
        expect(find.byType(PairingDialog), findsOneWidget);

        await tester.sendKeyEvent(LogicalKeyboardKey.escape);
        await settle(tester);
        expect(find.byType(PairingDialog), findsOneWidget);

        await tester.tapAt(const Offset(10, 10));
        await settle(tester);
        expect(find.byType(PairingDialog), findsOneWidget);
        verifyNever(() => disconnect(any()));

        confirmation.complete(const Right(unit));
        await settle(tester);

        expect(headerTitle('Connected'), findsOneWidget);
      },
    );

    testWidgets(
      'DovahLinkApp keeps a single pairing dialog when the Host behind it is tapped again',
      (WidgetTester tester) async {
        await openPairing(tester);

        await tester.tap(find.byKey(hostCard), warnIfMissed: false);
        await settle(tester);

        expect(find.byType(PairingDialog), findsOneWidget);
        verify(() => authenticate(any())).called(1);
      },
    );

    for (final (Size size, double mark, EdgeInsets header) in [
      (
        const Size(720, 480),
        42.0,
        const EdgeInsets.symmetric(horizontal: 19, vertical: 12),
      ),
      (
        const Size(900, 560),
        42.0,
        const EdgeInsets.symmetric(horizontal: 19, vertical: 12),
      ),
      (
        const Size(1280, 720),
        54.0,
        const EdgeInsets.symmetric(horizontal: 22, vertical: 19),
      ),
      (
        const Size(1600, 900),
        54.0,
        const EdgeInsets.symmetric(horizontal: 22, vertical: 19),
      ),
    ]) {
      testWidgets(
        'DovahLinkApp sizes the pairing dialog for the window without overflow at $size',
        (WidgetTester tester) async {
          setDovahTestWindow(tester, size);

          await openPairing(tester);

          expect(tester.takeException(), isNull);
          expect(find.text('Check Skyrim'), findsOneWidget);
          expect(tester.getSize(find.byType(PairingMark)), Size.square(mark));
          expect(
            tester
                .widget<Container>(find.byKey(const Key('dovah-dialog-header')))
                .padding,
            header,
          );
          expect(
            tester.getSize(find.byType(DovahDialog)).height,
            lessThanOrEqualTo(size.height * (size.height <= 620 ? 0.92 : 0.86)),
          );
        },
      );
    }
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
