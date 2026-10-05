import 'dart:async';

import 'package:flutter/services.dart' show PlatformException;

import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.middleware.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
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
        CharacterIdentityState,
        CharacterLevelState,
        CharacterSupernaturalTraitsState,
        CharacterVitalsState,
        CharacterXpState,
        DovahLinkConnectionState,
        DovahLinkHost,
        DovahLinkHostAvailability,
        DovahLinkKnownHostInvalidation,
        DovahLinkKnownHostState,
        DovahLinkStateArea,
        DovahLinkStateStatus,
        DovahLinkClient,
        DovahLinkTrustState,
        GameTimeState,
        HelloResult,
        IDovahLinkCharacter,
        IDovahLinkConnections,
        IDovahLinkCurrentHost,
        IDovahLinkHosts,
        IDovahLinkPairing,
        IClientStorage,
        PlayerLocationState,
        StateSynchronization,
        TrackedQuestsState;

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
class MockDovahLinkConnections extends Mock implements IDovahLinkConnections {
  /// Current state used when trust admission reaches live-state middleware.
  DovahLinkConnectionState currentState = DovahLinkConnectionState.disconnected;

  /// Returns the configured current SDK state.
  @override
  DovahLinkConnectionState get state => currentState;

  /// Replays the configured current SDK lifecycle state to every listener.
  @override
  Stream<DovahLinkConnectionState> get stateChanges =>
      Stream<DovahLinkConnectionState>.value(currentState);
}

/// Mocks the current-Host stream and subscription contract for app composition.
class MockDovahLinkCurrentHost extends Mock implements IDovahLinkCurrentHost {}

/// Mocks the grouped Character stream contract for app composition.
class MockDovahLinkCharacter extends Mock implements IDovahLinkCharacter {}

/// Mocks supported client storage for Known Host store-composition coverage.
class MockClientStorage extends Mock implements IClientStorage {}

/// Exercises independent store creation by [AppCompositionRoot] through the real dependency
/// graph [initDependencies] wires -- a composition test proving the production graph resolves
/// correctly, not a second suite for any one collaborator's own behavior.
void main() {
  late MockSharedPreferencesAsync preferences;

  setUp(() async {
    TestWidgetsFlutterBinding.ensureInitialized();
    registerFallbackValue(DovahLinkStateArea.gameTime);
    await sl.reset();
    await initDependencies();
    preferences = MockSharedPreferencesAsync();
    when(
      () => preferences.getString('dovahlink.appearance.themePreset'),
    ).thenAnswer((_) async => null);
    when(
      () => preferences.getString('dovahlink.identity.deviceName'),
    ).thenAnswer((_) async => null);
    await sl.unregister<SharedPreferencesAsync>();
    sl.registerSingleton<SharedPreferencesAsync>(preferences);
  });

  tearDown(() async {
    await sl.reset();
  });

  group('Method createStore behaves correctly', () {
    test(
      'Method createStore loads the saved device name before creating state',
      () async {
        when(
          () => preferences.getString('dovahlink.identity.deviceName'),
        ).thenAnswer((_) async => 'Pairing Name');

        final Store<AppState> store = await const AppCompositionRoot()
            .createStore();

        expect(store.state.deviceIdentity.displayName, 'Pairing Name');
        expect(store.state.deviceIdentity.loadFailure, isNull);
      },
    );

    test(
      'Method createStore keeps the name unavailable when preferences fail',
      () async {
        when(
          () => preferences.getString('dovahlink.identity.deviceName'),
        ).thenAnswer(
          (_) => Future<String?>.error(
            PlatformException(
              code: 'read-failed',
              message: 'Identity storage unavailable.',
            ),
          ),
        );

        final Store<AppState> store = await const AppCompositionRoot()
            .createStore();

        expect(store.state.deviceIdentity.displayName, isNull);
        expect(
          store.state.deviceIdentity.loadFailure,
          'Identity storage unavailable.',
        );
      },
    );

    test('createStore dependencies register Session Shell navigation', () {
      expect(sl<ISessionShellMiddleware>(), isA<SessionShellMiddleware>());
      expect(sl<ILiveStateMiddleware>(), isA<LiveStateMiddleware>());
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
      'Method createStore initializes live-state projection from the SDK',
      () async {
        final MockDovahLinkClient client = MockDovahLinkClient();
        final MockDovahLinkHosts hosts = MockDovahLinkHosts();
        final MockDovahLinkPairing pairing = MockDovahLinkPairing();
        final MockDovahLinkConnections connections = MockDovahLinkConnections();
        final MockDovahLinkCurrentHost currentHost = MockDovahLinkCurrentHost();
        final MockDovahLinkCharacter character = MockDovahLinkCharacter();
        final List<DovahLinkStateArea> requestedAreas = <DovahLinkStateArea>[];
        when(() => client.hosts).thenReturn(hosts);
        when(() => client.pairing).thenReturn(pairing);
        when(() => client.connections).thenReturn(connections);
        when(() => client.currentHost).thenReturn(currentHost);
        connections.currentState = DovahLinkConnectionState.connected;
        when(
          () => currentHost.trustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        when(() => currentHost.character).thenReturn(character);
        when(() => connections.knownHostInvalidations).thenAnswer(
          (_) => const Stream<DovahLinkKnownHostInvalidation>.empty(),
        );
        when(() => hosts.knownHostStatesChanges).thenAnswer(
          (_) => const Stream<List<DovahLinkKnownHostState>>.empty(),
        );
        when(
          () => pairing.candidates,
        ).thenAnswer((_) => const Stream<List<DovahLinkHost>>.empty());
        when(() => character.vitalsChanges).thenAnswer(
          (_) =>
              const Stream<StateSynchronization<CharacterVitalsState>>.empty(),
        );
        when(() => character.xpChanges).thenAnswer(
          (_) => Stream<StateSynchronization<CharacterXpState>>.value(
            const StateSynchronization<CharacterXpState>(
              status: DovahLinkStateStatus.synchronized,
              value: CharacterXpState(value: 61.5),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 2,
            ),
          ),
        );
        when(() => character.levelChanges).thenAnswer(
          (_) =>
              const Stream<StateSynchronization<CharacterLevelState>>.empty(),
        );
        when(() => character.identityChanges).thenAnswer(
          (_) =>
              const Stream<
                StateSynchronization<CharacterIdentityState?>
              >.empty(),
        );
        when(() => character.supernaturalTraitsChanges).thenAnswer(
          (_) =>
              const Stream<
                StateSynchronization<CharacterSupernaturalTraitsState?>
              >.empty(),
        );
        when(() => currentHost.playerLocationChanges).thenAnswer(
          (_) =>
              const Stream<StateSynchronization<PlayerLocationState?>>.empty(),
        );
        when(() => currentHost.gameTimeChanges).thenAnswer(
          (_) => const Stream<StateSynchronization<GameTimeState?>>.empty(),
        );
        when(() => currentHost.trackedQuestsChanges).thenAnswer(
          (_) =>
              const Stream<StateSynchronization<TrackedQuestsState?>>.empty(),
        );
        when(() => currentHost.subscribeStateArea(any())).thenAnswer((
          Invocation invocation,
        ) async {
          requestedAreas.add(
            invocation.positionalArguments.single as DovahLinkStateArea,
          );
          return <DovahLinkStateArea>{};
        });
        await sl.unregister<IClientStorage>();
        sl.registerSingleton<IClientStorage>(MockClientStorage());
        await sl.unregister<DovahLinkClient>();
        sl.registerSingleton<DovahLinkClient>(client);
        addTearDown(() async => sl<ILiveStateMiddleware>().shutdown());

        final Store<AppState> store = await const AppCompositionRoot()
            .createStore();
        store.dispatch(const PairingSessionTrustedAction());
        await pumpEventQueue();

        expect(
          store.state.liveState.characterXp.status,
          LiveStateStatus.synchronized,
        );
        expect(store.state.liveState.characterXp.value, 61.5);
        expect(store.state.liveState.characterXp.playContextId, 'context-a');
        expect(requestedAreas, [
          DovahLinkStateArea.characterVitals,
          DovahLinkStateArea.characterXp,
          DovahLinkStateArea.characterLevel,
          DovahLinkStateArea.characterIdentity,
          DovahLinkStateArea.characterSupernaturalTraits,
          DovahLinkStateArea.playerLocation,
          DovahLinkStateArea.gameTime,
          DovahLinkStateArea.trackedQuests,
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
