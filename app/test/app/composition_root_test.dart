import 'package:flutter/services.dart' show PlatformException;

import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../fixtures/fixtures.dart';

import 'package:flutter/foundation.dart'
    show TargetPlatform, debugDefaultTargetPlatformOverride;

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkHost,
        DovahLinkTrustState,
        HelloResult,
        IDovahLinkDiscoveryService;

/// Mocks async preference reads for composition-root tests.
class MockSharedPreferencesAsync extends Mock
    implements SharedPreferencesAsync {}

/// Mocks SDK local discovery for composition-root tests.
class MockDovahLinkDiscoveryService extends Mock
    implements IDovahLinkDiscoveryService {}

/// Exercises independent store creation by [AppCompositionRoot] through the real dependency
/// graph [initDependencies] wires -- a composition test proving the production graph resolves
/// correctly, not a second suite for any one collaborator's own behavior.
void main() {
  late MockSharedPreferencesAsync preferences;
  late MockDovahLinkDiscoveryService discoveryService;

  setUp(() async {
    TestWidgetsFlutterBinding.ensureInitialized();
    await sl.reset();
    await initDependencies();
    discoveryService = MockDovahLinkDiscoveryService();
    await sl.unregister<IDovahLinkDiscoveryService>();
    sl.registerSingleton<IDovahLinkDiscoveryService>(discoveryService);
    preferences = MockSharedPreferencesAsync();
    when(
      () => preferences.getString('dovahlink.appearance.themePreset'),
    ).thenAnswer((_) async => null);
    await sl.unregister<SharedPreferencesAsync>();
    sl.registerSingleton<SharedPreferencesAsync>(preferences);
  });

  tearDown(() async {
    await sl.reset();
  });

  group('Method createStore behaves correctly', () {
    test(
      'marks pairing unavailable when secure storage is unsupported',
      () async {
        debugDefaultTargetPlatformOverride = TargetPlatform.android;
        addTearDown(() => debugDefaultTargetPlatformOverride = null);
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(
          store.state.pairing.support,
          PairingSupport.secureStorageUnavailable,
        );
      },
    );

    test(
      'Method createStore creates independent stores with initial state',
      () async {
        const AppCompositionRoot root = AppCompositionRoot();

        final firstStore = await root.createStore();
        final secondStore = await root.createStore();

        expect(firstStore.state, isA<AppState>());
        expect(firstStore.state.pairing, isA<PairingState>());
        expect(secondStore.state, isA<AppState>());
        expect(identical(firstStore, secondStore), isFalse);
      },
    );

    test(
      'Method createStore hydrates appearance state from a persisted preset',
      () async {
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenAnswer((_) async => 'hearth');
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(store.state.appearance.activePreset, DovahThemePreset.hearth);
      },
    );

    test(
      'Method createStore uses the default appearance when no preset is persisted',
      () async {
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(store.state.appearance.activePreset, defaultThemePreset);
      },
    );

    test(
      'Method createStore uses the default appearance when preset loading fails',
      () async {
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenAnswer(
          (_) => Future<String?>.error(PlatformException(code: 'read-failed')),
        );
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(store.state.appearance.activePreset, defaultThemePreset);
        verify(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).called(1);
      },
    );

    test(
      'Method createStore runs the registered connection middleware for discovery',
      () async {
        final HelloResult reportedHello = Fixtures.buildSdkHelloResult(
          trustState: DovahLinkTrustState.unpaired,
        );
        when(() => discoveryService.discoverLocalHost()).thenAnswer(
          (_) async => DovahLinkHost(
            hostId: reportedHello.hostId,
            hostName: reportedHello.hostName,
            endpoint: defaultHostUri,
          ),
        );
        const AppCompositionRoot root = AppCompositionRoot();
        final Store<AppState> store = await root.createStore();
        final Future<AppState> availableState = store.onChange.firstWhere(
          (AppState state) =>
              state.connection.discoveryStatus ==
              ConnectionDiscoveryStatus.available,
        );

        store.dispatch(const ConnectionDiscoveryRequestedAction());

        final AppState result = await availableState.timeout(
          const Duration(seconds: 1),
        );
        expect(result.connection.hosts, [Fixtures.buildHost()]);
        verify(() => discoveryService.discoverLocalHost()).called(1);
      },
    );
  });
}
