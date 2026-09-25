import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/app.viewmodel.dart';
import 'package:dovahlink_client/features/appearance/data/datasources/appearance_local.datasource.dart';
import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/load_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/set_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/cancel_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/observe_connection_status.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing_renotify.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_countdown.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_renotify_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_section.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/platform/windows/windows_lifecycle_bridge.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/utils/app_shutdown_service.dart';
import 'package:dovahlink_client/shared/utils/existing_dovahlink_client.dart';

import 'package:dovahlink_client_sdk/dovahlink_client_windows.dart'
    show DpapiClientStorage;

/// Mocks the asynchronous preference API without requiring a registered plugin.
class MockSharedPreferencesAsync extends Mock
    implements SharedPreferencesAsync {}

/// Mocks the Redux store used to resolve Store-backed registrations.
class MockStore extends Mock implements Store<AppState> {}

void main() {
  setUpAll(() {
    TestWidgetsFlutterBinding.ensureInitialized();
    registerFallbackValue(
      const ThemePresetSelectedAction(DovahThemePreset.dovah),
    );
    registerFallbackValue(const PairingStartedAction());
    registerFallbackValue(const PairingCancelRequestedAction());
    registerFallbackValue(const PairingDisposedAction(wasTrusted: false));
  });

  setUp(() async {
    await sl.reset();
  });

  tearDown(() async {
    const MethodChannel(
      'dovahlink/window_lifecycle',
    ).setMethodCallHandler(null);
    await sl.reset();
  });

  group('injection_container — shared registrations', () {
    test(
      'initDependencies leaves the legacy preferences channel untouched',
      () async {
        const MethodChannel channel = MethodChannel(
          'plugins.flutter.io/shared_preferences',
        );
        final TestDefaultBinaryMessenger messenger =
            TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
        int storageCalls = 0;
        messenger.setMockMethodCallHandler(channel, (MethodCall call) async {
          storageCalls++;
          throw PlatformException(code: 'read-failed');
        });

        try {
          await initDependencies();
          expect(sl.isRegistered<GoRouter>(), isTrue);
          expect(sl.isRegistered<NavigatorService>(), isTrue);
          expect(sl.isRegistered<SharedPreferencesAsync>(), isTrue);
          expect(storageCalls, 0);
        } finally {
          messenger.setMockMethodCallHandler(channel, null);
        }
      },
    );

    test('initDependencies registers the router', () async {
      await initDependencies();

      expect(sl.isRegistered<GoRouter>(), isTrue);
    });

    test('initDependencies registers the navigator service', () async {
      await initDependencies();

      expect(sl.isRegistered<NavigatorService>(), isTrue);
    });

    test(
      'initDependencies resolves DovahLinkAppViewModel from a Store',
      () async {
        await initDependencies();
        final MockStore store = MockStore();
        when(() => store.state).thenReturn(
          AppState.initial(
            appearance: const AppearanceState(
              activePreset: DovahThemePreset.hearth,
            ),
          ),
        );

        final DovahLinkAppViewModel viewModel = sl<DovahLinkAppViewModel>(
          param1: store,
        );

        expect(viewModel.activePreset, DovahThemePreset.hearth);
      },
    );

    test('the registered GoRouter is a true singleton', () async {
      await initDependencies();

      expect(identical(sl<GoRouter>(), sl<GoRouter>()), isTrue);
    });

    test(
      'calling initDependencies twice does not throw or re-register',
      () async {
        await initDependencies();

        await expectLater(initDependencies(), completes);
        expect(sl.isRegistered<GoRouter>(), isTrue);
        expect(sl.isRegistered<NavigatorService>(), isTrue);
      },
    );
  });

  group('injection_container — connection registrations', () {
    test(
      'initDependencies registers the connections screen ViewModel factory',
      () async {
        await initDependencies();

        expect(sl.isRegistered<ConnectionsScreenViewModel>(), isTrue);
      },
    );
  });

  group('injection_container — pairing registrations', () {
    test(
      'initDependencies registers app shutdown and pairing middleware',
      () async {
        await initDependencies();

        expect(sl.isRegistered<IAppShutdownService>(), isTrue);
        expect(sl.isRegistered<IPairingMiddleware>(), isTrue);
      },
    );

    test('initDependencies registers the SDK client', () async {
      await initDependencies();

      expect(sl.isRegistered<DovahLinkClient>(), isTrue);
    });

    for (final TargetPlatform platform in <TargetPlatform>[
      TargetPlatform.android,
      TargetPlatform.iOS,
    ]) {
      test(
        'initDependencies selects unsupported storage on $platform without constructing DPAPI',
        () async {
          debugDefaultTargetPlatformOverride = platform;

          await initDependencies();

          expect(sl<IClientStorage>(), isA<UnsupportedClientStorage>());
          expect(sl<DovahLinkClient>(), isA<DovahLinkClient>());
          expect(sl.isRegistered<IWindowsLifecycleBridge>(), isFalse);
        },
      );
    }

    test('initDependencies selects DPAPI storage on Windows', () async {
      debugDefaultTargetPlatformOverride = TargetPlatform.windows;

      await initDependencies();

      expect(sl<IClientStorage>(), isA<DpapiClientStorage>());
    });

    test(
      'initDependencies registers Windows lifecycle without constructing the SDK client',
      () async {
        debugDefaultTargetPlatformOverride = TargetPlatform.windows;

        await initDependencies();
        final ExistingDovahLinkClient existingClient =
            sl<ExistingDovahLinkClient>();

        expect(sl.isRegistered<IWindowsLifecycleBridge>(), isTrue);
        expect(existingClient.hasClient, isFalse);
        await sl<IAppShutdownService>().shutdown();
        expect(existingClient.hasClient, isFalse);
      },
    );

    test(
      'initDependencies tracks a client created for pairing so shutdown can disconnect it',
      () async {
        debugDefaultTargetPlatformOverride = TargetPlatform.windows;

        await initDependencies();
        final ExistingDovahLinkClient existingClient =
            sl<ExistingDovahLinkClient>();
        final DovahLinkClient client = sl<DovahLinkClient>();

        expect(existingClient.hasClient, isTrue);
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        await sl<IAppShutdownService>().shutdown();
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
      },
    );
    test('initDependencies registers the pairing remote data source', () async {
      await initDependencies();

      expect(sl.isRegistered<IPairingRemoteDataSource>(), isTrue);
    });

    test('initDependencies registers the pairing repository', () async {
      await initDependencies();

      expect(sl.isRegistered<IPairingRepository>(), isTrue);
    });

    test('initDependencies registers the pairing use cases', () async {
      await initDependencies();

      expect(sl.isRegistered<AuthenticateUseCase>(), isTrue);
      expect(sl.isRegistered<RequestPairingUseCase>(), isTrue);
      expect(sl.isRegistered<ConfirmPairingCodeUseCase>(), isTrue);
      expect(sl.isRegistered<DisconnectUseCase>(), isTrue);
      expect(sl.isRegistered<RequestPairingRenotifyUseCase>(), isTrue);
      expect(sl.isRegistered<CancelPairingUseCase>(), isTrue);
      expect(sl.isRegistered<ObserveConnectionStatusUseCase>(), isTrue);
    });

    test(
      'initDependencies registers and resolves the pairing dialog ViewModel factory',
      () async {
        await initDependencies();
        final MockStore store = MockStore();
        when(() => store.state).thenReturn(AppState.initial());

        final PairingDialogViewModel viewModel = sl<PairingDialogViewModel>(
          param1: store,
        );

        expect(sl.isRegistered<PairingDialogViewModel>(), isTrue);
        expect(viewModel.title, 'Pair with this PC');
      },
    );

    test(
      'initDependencies registers and resolves the pairing section ViewModel factory',
      () async {
        await initDependencies();
        final MockStore store = MockStore();
        when(() => store.state).thenReturn(AppState.initial());

        final PairingSectionViewModel viewModel = sl<PairingSectionViewModel>(
          param1: store,
        );

        expect(sl.isRegistered<PairingSectionViewModel>(), isTrue);
        expect(viewModel.phase, PairingPhase.none);
      },
    );

    test(
      'initDependencies resolves PairingSectionViewModel callbacks against a Store',
      () async {
        await initDependencies();
        final MockStore store = MockStore();
        when(() => store.state).thenReturn(AppState.initial());
        when(() => store.dispatch(any())).thenAnswer((_) {});

        final PairingSectionViewModel viewModel = sl<PairingSectionViewModel>(
          param1: store,
        );
        viewModel.onStart();
        viewModel.onDispose();

        verify(() => store.dispatch(const PairingStartedAction())).called(1);
        verify(
          () => store.dispatch(const PairingDisposedAction(wasTrusted: false)),
        ).called(1);
      },
    );

    test(
      'initDependencies resolves PairingCancelButtonViewModel from a Store',
      () async {
        await initDependencies();
        final MockStore store = MockStore();
        when(() => store.state).thenReturn(
          AppState(
            connection: ConnectionState.initial(),
            pairing: const PairingState(
              phase: PairingPhase.awaitingCode,
              hostVersion: null,
              error: null,
              codeExpiresAt: null,
              renotifyAvailableAt: null,
            ),
          ),
        );
        when(() => store.dispatch(any())).thenAnswer((_) {});

        final PairingCancelButtonViewModel viewModel =
            sl<PairingCancelButtonViewModel>(param1: store);

        expect(viewModel.isEnabled, isTrue);
        expect(viewModel.onPressed, isNotNull);
        viewModel.onPressed!();
        verify(
          () => store.dispatch(const PairingCancelRequestedAction()),
        ).called(1);
      },
    );

    test(
      'initDependencies resolves PairingCountdownViewModel from a Store',
      () async {
        await initDependencies();
        final MockStore store = MockStore();
        when(() => store.state).thenReturn(
          AppState(
            connection: ConnectionState.initial(),
            pairing: PairingState(
              phase: PairingPhase.awaitingCode,
              hostVersion: null,
              error: null,
              codeExpiresAt: DateTime.now().add(const Duration(minutes: 1)),
              renotifyAvailableAt: null,
            ),
          ),
        );

        final PairingCountdownViewModel viewModel =
            sl<PairingCountdownViewModel>(param1: store);

        expect(viewModel.remainingSeconds, greaterThan(0));
      },
    );

    test(
      'initDependencies resolves PairingRenotifyButtonViewModel from a Store',
      () async {
        await initDependencies();
        final MockStore store = MockStore();
        when(() => store.state).thenReturn(
          AppState(
            connection: ConnectionState.initial(),
            pairing: PairingState(
              phase: PairingPhase.awaitingCode,
              hostVersion: null,
              error: null,
              codeExpiresAt: null,
              renotifyAvailableAt: DateTime.now().add(
                const Duration(minutes: 1),
              ),
            ),
          ),
        );
        when(() => store.dispatch(any())).thenAnswer((_) {});

        final PairingRenotifyButtonViewModel viewModel =
            sl<PairingRenotifyButtonViewModel>(param1: store);

        expect(viewModel.isAvailable, isFalse);
        expect(viewModel.cooldownSeconds, greaterThan(0));
        expect(viewModel.onPressed, isNull);
      },
    );

    test('RequestPairingRenotifyUseCase is a singleton', () async {
      await initDependencies();

      expect(
        identical(
          sl<RequestPairingRenotifyUseCase>(),
          sl<RequestPairingRenotifyUseCase>(),
        ),
        isTrue,
      );
    });

    test('CancelPairingUseCase is a singleton', () async {
      await initDependencies();

      expect(
        identical(sl<CancelPairingUseCase>(), sl<CancelPairingUseCase>()),
        isTrue,
      );
    });

    test('RequestPairingRenotifyUseCase can be instantiated', () async {
      await initDependencies();

      expect(sl<RequestPairingRenotifyUseCase>(), isNotNull);
      expect(
        sl<RequestPairingRenotifyUseCase>(),
        isA<RequestPairingRenotifyUseCase>(),
      );
    });

    test('CancelPairingUseCase can be instantiated', () async {
      await initDependencies();

      expect(sl<CancelPairingUseCase>(), isNotNull);
      expect(sl<CancelPairingUseCase>(), isA<CancelPairingUseCase>());
    });
  });

  group('injection_container — appearance registrations', () {
    test(
      'initDependencies registers the appearance local data source',
      () async {
        await initDependencies();

        expect(sl.isRegistered<IAppearanceLocalDataSource>(), isTrue);
      },
    );

    test('initDependencies registers the appearance repository', () async {
      await initDependencies();

      expect(sl.isRegistered<IAppearanceRepository>(), isTrue);
    });

    test('initDependencies registers the appearance use cases', () async {
      await initDependencies();

      expect(sl.isRegistered<LoadThemePresetUseCase>(), isTrue);
      expect(sl.isRegistered<SetThemePresetUseCase>(), isTrue);
    });

    test(
      'initDependencies resolves AppearanceSectionViewModel from a Store',
      () async {
        await initDependencies();
        final MockStore store = MockStore();
        when(() => store.state).thenReturn(
          AppState.initial(
            appearance: const AppearanceState(
              activePreset: DovahThemePreset.hearth,
            ),
          ),
        );
        when(() => store.dispatch(any())).thenAnswer((_) {});

        final AppearanceSectionViewModel viewModel =
            sl<AppearanceSectionViewModel>(param1: store);

        expect(viewModel.activePreset, DovahThemePreset.hearth);
        viewModel.onSelectPreset(DovahThemePreset.frostbound);
        verify(
          () => store.dispatch(
            const ThemePresetSelectedAction(DovahThemePreset.frostbound),
          ),
        ).called(1);
      },
    );

    test('LoadThemePresetUseCase is a singleton', () async {
      await initDependencies();
      await sl.unregister<SharedPreferencesAsync>();
      sl.registerSingleton<SharedPreferencesAsync>(
        MockSharedPreferencesAsync(),
      );

      expect(
        identical(sl<LoadThemePresetUseCase>(), sl<LoadThemePresetUseCase>()),
        isTrue,
      );
    });
  });
}
