import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/appearance/data/datasources/appearance_local.datasource.dart';
import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/load_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/set_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
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
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';

/// Mocks the asynchronous preference API without requiring a registered plugin.
class MockSharedPreferencesAsync extends Mock
    implements SharedPreferencesAsync {}

void main() {
  setUpAll(() {
    TestWidgetsFlutterBinding.ensureInitialized();
  });

  setUp(() async {
    await sl.reset();
  });

  tearDown(() async {
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
    test('initDependencies registers the SDK client', () async {
      await initDependencies();

      expect(sl.isRegistered<DovahLinkClient>(), isTrue);
    });

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

    test('initDependencies registers the pairing ViewModel factory', () async {
      await initDependencies();

      expect(sl.isRegistered<PairingScreenViewModel>(), isTrue);
    });

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
      'initDependencies registers the appearance ViewModel factory',
      () async {
        await initDependencies();

        expect(sl.isRegistered<AppearanceSectionViewModel>(), isTrue);
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
