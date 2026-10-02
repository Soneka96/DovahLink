import 'dart:async';

import 'package:flutter/services.dart' show PlatformException;

import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../fixtures/fixtures.dart';

import 'package:flutter/foundation.dart'
    show
        FlutterError,
        FlutterErrorDetails,
        TargetPlatform,
        debugDefaultTargetPlatformOverride;

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkHost,
        DovahLinkHostAvailability,
        DovahLinkKnownHostInvalidation,
        DovahLinkKnownHostState,
        DovahLinkClient,
        DovahLinkTrustState,
        HelloResult,
        IDovahLinkConnections,
        IDovahLinkHosts,
        IDovahLinkPairing,
        IClientStorage;

/// Mocks async preference reads for composition-root tests.
class MockSharedPreferencesAsync extends Mock
    implements SharedPreferencesAsync {}

/// Mocks the SDK client that supplies Known Host state during store creation.
class MockDovahLinkClient extends Mock implements DovahLinkClient {}

/// Mocks the SDK client's grouped Known Host API.
class MockDovahLinkHosts extends Mock implements IDovahLinkHosts {}

/// Mocks the SDK client's grouped discovery and pairing API.
class MockDovahLinkPairing extends Mock implements IDovahLinkPairing {}

/// Mocks the SDK client's grouped connection API.
class MockDovahLinkConnections extends Mock implements IDovahLinkConnections {}

/// Mocks supported client storage for Known Host store-composition coverage.
class MockClientStorage extends Mock implements IClientStorage {}

/// Exercises independent store creation by [AppCompositionRoot] through the real dependency
/// graph [initDependencies] wires -- a composition test proving the production graph resolves
/// correctly, not a second suite for any one collaborator's own behavior.
void main() {
  late MockSharedPreferencesAsync preferences;

  setUp(() async {
    TestWidgetsFlutterBinding.ensureInitialized();
    await sl.reset();
    await initDependencies();
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
    test('createStore dependencies register Session Shell navigation', () {
      expect(sl<ISessionShellMiddleware>(), isA<SessionShellMiddleware>());
    });

    test(
      'createStore routes Session Shell Back through registered middleware',
      () async {
        final GoRouter router = sl<GoRouter>();
        router.go(AppRoutes.sessionFor('selected-host'));
        final Store<AppState> store = await const AppCompositionRoot()
            .createStore();

        store.dispatch(const SessionShellBackRequestedAction());
        await pumpEventQueue();

        expect(router.routeInformationProvider.value.uri.path, AppRoutes.home);
      },
    );

    test(
      'createStore and Pairing resolve the same SDK client registration',
      () async {
        final MockDovahLinkClient client = MockDovahLinkClient();
        final MockDovahLinkHosts hosts = MockDovahLinkHosts();
        final MockDovahLinkPairing pairing = MockDovahLinkPairing();
        final MockDovahLinkConnections connections = MockDovahLinkConnections();
        when(() => client.hosts).thenReturn(hosts);
        when(() => client.pairing).thenReturn(pairing);
        when(() => client.connections).thenReturn(connections);
        when(() => connections.knownHostInvalidations).thenAnswer(
          (_) => const Stream<DovahLinkKnownHostInvalidation>.empty(),
        );
        int knownHostStatesSubscriptionReads = 0;
        when(() => hosts.knownHostStatesChanges).thenAnswer((_) {
          knownHostStatesSubscriptionReads++;
          return const Stream<List<DovahLinkKnownHostState>>.empty();
        });
        when(
          () => pairing.candidates,
        ).thenAnswer((_) => const Stream<List<DovahLinkHost>>.empty());
        when(() => connections.disconnect()).thenAnswer((_) async {});
        await sl.unregister<DovahLinkClient>();
        sl.registerSingleton<DovahLinkClient>(client);

        await const AppCompositionRoot().createStore();
        expect(knownHostStatesSubscriptionReads, 1);
        final result = await sl<IPairingRemoteDataSource>().disconnect();

        expect(result.isRight(), isTrue);
        verify(() => connections.disconnect()).called(1);
      },
    );

    test(
      'Method createStore subscribes to and maps SDK Known Host state',
      () async {
        final DovahLinkHost sdkHost = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'KNOWN-HOST',
          endpoint: defaultHostUri,
        );
        final MockDovahLinkClient client = MockDovahLinkClient();
        final MockDovahLinkHosts hosts = MockDovahLinkHosts();
        final MockDovahLinkPairing pairing = MockDovahLinkPairing();
        final MockDovahLinkConnections connections = MockDovahLinkConnections();
        when(() => client.hosts).thenReturn(hosts);
        when(() => client.pairing).thenReturn(pairing);
        when(() => client.connections).thenReturn(connections);
        when(() => connections.knownHostInvalidations).thenAnswer(
          (_) => const Stream<DovahLinkKnownHostInvalidation>.empty(),
        );
        when(() => hosts.knownHostStatesChanges).thenAnswer(
          (_) => Stream<List<DovahLinkKnownHostState>>.value(
            <DovahLinkKnownHostState>[
              Fixtures.buildSdkKnownHostState(
                host: sdkHost,
                availability: DovahLinkHostAvailability.online,
              ),
            ],
          ),
        );
        when(
          () => pairing.candidates,
        ).thenAnswer((_) => const Stream<List<DovahLinkHost>>.empty());
        await sl.unregister<IClientStorage>();
        sl.registerSingleton<IClientStorage>(MockClientStorage());
        await sl.unregister<DovahLinkClient>();
        sl.registerSingleton<DovahLinkClient>(client);

        final Store<AppState> store = await const AppCompositionRoot()
            .createStore();
        final AppState observed = await store.onChange.firstWhere(
          (AppState state) => state.connection.knownHosts.isNotEmpty,
        );

        expect(observed.connection.knownHosts, [
          Fixtures.buildKnownHost(
            host: Fixtures.buildHost(
              hostId: sdkHost.hostId,
              displayName: sdkHost.hostName,
            ),
            availability: HostAvailability.online,
          ),
        ]);
      },
    );

    test(
      'marks pairing unavailable when secure storage is unsupported',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        debugDefaultTargetPlatformOverride = TargetPlatform.android;
        addTearDown(() => debugDefaultTargetPlatformOverride = null);
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();
        await pumpEventQueue();

        expect(
          store.state.pairing.support,
          PairingSupport.secureStorageUnavailable,
        );
        expect(reported, hasLength(1));
        expect(reported.single.exception, isA<UnsupportedError>());
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
        final DovahLinkHost candidate = DovahLinkHost(
          hostId: reportedHello.hostId,
          hostName: reportedHello.hostName,
          endpoint: defaultHostUri,
        );
        final MockDovahLinkClient client = MockDovahLinkClient();
        final MockDovahLinkHosts hosts = MockDovahLinkHosts();
        final MockDovahLinkPairing pairing = MockDovahLinkPairing();
        final MockDovahLinkConnections connections = MockDovahLinkConnections();
        when(() => client.hosts).thenReturn(hosts);
        when(() => client.pairing).thenReturn(pairing);
        when(() => client.connections).thenReturn(connections);
        when(() => connections.knownHostInvalidations).thenAnswer(
          (_) => const Stream<DovahLinkKnownHostInvalidation>.empty(),
        );
        when(() => hosts.knownHostStatesChanges).thenAnswer(
          (_) => const Stream<List<DovahLinkKnownHostState>>.empty(),
        );
        when(() => pairing.candidates).thenAnswer(
          (_) => Stream<List<DovahLinkHost>>.value(<DovahLinkHost>[candidate]),
        );
        when(
          () => pairing.discoverHosts(),
        ).thenAnswer((_) async => <DovahLinkHost>[candidate]);
        await sl.unregister<DovahLinkClient>();
        sl.registerSingleton<DovahLinkClient>(client);
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
        expect(result.connection.hosts, [
          Fixtures.buildHost(
            hostId: reportedHello.hostId,
            displayName: reportedHello.hostName,
          ),
        ]);
        verify(() => pairing.discoverHosts()).called(1);
      },
    );
  });
}
