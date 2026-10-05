import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.middleware.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterLevelState,
        CharacterSupernaturalTraitsState,
        CharacterVital,
        CharacterVitalsState,
        CharacterXpState,
        DovahLinkClient,
        DovahLinkConnectionState,
        DovahLinkStateArea,
        DovahLinkStateStatus,
        DovahLinkTrustState,
        GameTimeState,
        IDovahLinkCharacter,
        IDovahLinkConnections,
        IDovahLinkCurrentHost,
        PlayerLocationCellKind,
        PlayerLocationState,
        StateSynchronization,
        TrackedQuestsState;

/// Mocks the single SDK engine used by live-state middleware.
class MockDovahLinkClient extends Mock implements DovahLinkClient {}

/// Mocks the SDK's lifecycle view.
class MockDovahLinkConnections extends Mock implements IDovahLinkConnections {}

/// Mocks the SDK's grouped Character streams.
class MockDovahLinkCharacter extends Mock implements IDovahLinkCharacter {}

/// Mocks the SDK's current-Host streams and subscription operation.
class MockDovahLinkCurrentHost extends Mock implements IDovahLinkCurrentHost {}

/// Mocks the Redux store and records dispatched actions.
class MockStore extends Mock implements Store<AppState> {}

/// Controls SDK lifecycle and typed domain streams for middleware tests.
class LiveStateSdkFake {
  /// The mocked SDK client wired to these controllable views.
  final MockDovahLinkClient client = MockDovahLinkClient();

  /// The mocked SDK connection lifecycle view.
  final MockDovahLinkConnections connections = MockDovahLinkConnections();

  /// The mocked current-Host view.
  final MockDovahLinkCurrentHost currentHost = MockDovahLinkCurrentHost();

  /// The mocked Character view.
  final MockDovahLinkCharacter character = MockDovahLinkCharacter();

  /// Broadcast source controlled by tests for SDK connection lifecycle changes.
  final StreamController<DovahLinkConnectionState> lifecycle =
      StreamController<DovahLinkConnectionState>.broadcast(sync: true);

  /// Broadcast source for Vitals changes.
  final StreamController<StateSynchronization<CharacterVitalsState>> vitals =
      StreamController<StateSynchronization<CharacterVitalsState>>.broadcast(
        sync: true,
      );

  /// Broadcast source for XP changes.
  final StreamController<StateSynchronization<CharacterXpState>> xp =
      StreamController<StateSynchronization<CharacterXpState>>.broadcast(
        sync: true,
      );

  /// Broadcast source for Level changes.
  final StreamController<StateSynchronization<CharacterLevelState>> level =
      StreamController<StateSynchronization<CharacterLevelState>>.broadcast(
        sync: true,
      );

  /// Broadcast source for Identity changes.
  final StreamController<StateSynchronization<CharacterIdentityState?>>
  identity =
      StreamController<StateSynchronization<CharacterIdentityState?>>.broadcast(
        sync: true,
      );

  /// Broadcast source for Supernatural Traits changes.
  final StreamController<
    StateSynchronization<CharacterSupernaturalTraitsState?>
  >
  supernaturalTraits =
      StreamController<
        StateSynchronization<CharacterSupernaturalTraitsState?>
      >.broadcast(sync: true);

  /// Broadcast source for Location changes.
  final StreamController<StateSynchronization<PlayerLocationState?>> location =
      StreamController<StateSynchronization<PlayerLocationState?>>.broadcast(
        sync: true,
      );

  /// Broadcast source for Game Time changes.
  final StreamController<StateSynchronization<GameTimeState?>> gameTime =
      StreamController<StateSynchronization<GameTimeState?>>.broadcast(
        sync: true,
      );

  /// Broadcast source for Tracked Quests changes.
  final StreamController<StateSynchronization<TrackedQuestsState?>> quests =
      StreamController<StateSynchronization<TrackedQuestsState?>>.broadcast(
        sync: true,
      );

  /// Every SDK area requested by the middleware in call order.
  final List<DovahLinkStateArea> requestedAreas = <DovahLinkStateArea>[];

  /// The current lifecycle state replayed to a newly attached listener.
  DovahLinkConnectionState connectionState =
      DovahLinkConnectionState.disconnected;

  /// The current trust standing exposed by the fake SDK view.
  DovahLinkTrustState? trustState = DovahLinkTrustState.trusted;

  /// Reads of the SDK Vitals stream getter.
  int vitalsStreamReads = 0;

  /// Reads of the SDK XP stream getter.
  int xpStreamReads = 0;

  /// Reads of the SDK Level stream getter.
  int levelStreamReads = 0;

  /// Reads of the SDK Identity stream getter.
  int identityStreamReads = 0;

  /// Reads of the SDK Supernatural Traits stream getter.
  int supernaturalTraitsStreamReads = 0;

  /// Reads of the SDK Location stream getter.
  int locationStreamReads = 0;

  /// Reads of the SDK Game Time stream getter.
  int gameTimeStreamReads = 0;

  /// Reads of the SDK Tracked Quests stream getter.
  int questsStreamReads = 0;

  /// Creates all SDK interface stubs over controllable stream sources.
  LiveStateSdkFake() {
    when(() => client.connections).thenReturn(connections);
    when(() => client.currentHost).thenReturn(currentHost);
    when(() => connections.state).thenAnswer((_) => connectionState);
    when(() => connections.stateChanges).thenAnswer((_) => _stateChanges());
    when(() => currentHost.trustState).thenAnswer((_) => trustState);
    when(() => currentHost.character).thenReturn(character);
    when(() => character.vitalsChanges).thenAnswer((_) {
      vitalsStreamReads++;
      return vitals.stream;
    });
    when(() => character.xpChanges).thenAnswer((_) {
      xpStreamReads++;
      return xp.stream;
    });
    when(() => character.levelChanges).thenAnswer((_) {
      levelStreamReads++;
      return level.stream;
    });
    when(() => character.identityChanges).thenAnswer((_) {
      identityStreamReads++;
      return identity.stream;
    });
    when(() => character.supernaturalTraitsChanges).thenAnswer((_) {
      supernaturalTraitsStreamReads++;
      return supernaturalTraits.stream;
    });
    when(() => currentHost.playerLocationChanges).thenAnswer((_) {
      locationStreamReads++;
      return location.stream;
    });
    when(() => currentHost.gameTimeChanges).thenAnswer((_) {
      gameTimeStreamReads++;
      return gameTime.stream;
    });
    when(() => currentHost.trackedQuestsChanges).thenAnswer((_) {
      questsStreamReads++;
      return quests.stream;
    });
    when(() => currentHost.subscribeStateArea(any())).thenAnswer((
      Invocation invocation,
    ) async {
      requestedAreas.add(
        invocation.positionalArguments.single as DovahLinkStateArea,
      );
      return <DovahLinkStateArea>{};
    });
  }

  /// Emits a connection lifecycle transition after updating the current value.
  /// @param state The SDK lifecycle state to publish.
  void emitConnectionState(DovahLinkConnectionState state) {
    connectionState = state;
    lifecycle.add(state);
  }

  /// Replays the current lifecycle state before forwarding later changes.
  Stream<DovahLinkConnectionState> _stateChanges() async* {
    yield connectionState;
    yield* lifecycle.stream;
  }

  /// Closes every test-owned SDK stream source.
  Future<void> close() async {
    await Future.wait<void>([
      lifecycle.close(),
      vitals.close(),
      xp.close(),
      level.close(),
      identity.close(),
      supernaturalTraits.close(),
      location.close(),
      gameTime.close(),
      quests.close(),
    ]);
  }
}

/// Exercises SDK lifecycle, subscription ownership, projection, and teardown.
void main() {
  late LiveStateSdkFake sdk;
  late LiveStateMiddleware middleware;
  late MockStore store;
  late List<Object?> actions;

  setUp(() async {
    await sl.reset();
    sdk = LiveStateSdkFake();
    sl.registerSingleton<DovahLinkClient>(sdk.client);
    middleware = LiveStateMiddleware();
    store = MockStore();
    actions = <Object?>[];
    when(() => store.state).thenReturn(AppState.initial());
    when(() => store.dispatch(any())).thenAnswer((Invocation invocation) {
      actions.add(invocation.positionalArguments.single);
    });
  });

  tearDown(() async {
    await middleware.shutdown();
    await sdk.close();
    await sl.reset();
  });

  group('LiveStateMiddleware processes SDK lifecycle correctly', () {
    test(
      'initialize attaches once and a trusted session requests all eight areas',
      () async {
        middleware.initialize(store);
        middleware.initialize(store);
        await pumpEventQueue();

        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();

        expect(sdk.requestedAreas, _expectedAreas);
        expect(sdk.vitalsStreamReads, 1);
        expect(sdk.xpStreamReads, 1);
        expect(sdk.levelStreamReads, 1);
        expect(sdk.identityStreamReads, 1);
        expect(sdk.supernaturalTraitsStreamReads, 1);
        expect(sdk.locationStreamReads, 1);
        expect(sdk.gameTimeStreamReads, 1);
        expect(sdk.questsStreamReads, 1);
      },
    );

    test(
      'an untrusted connected session waits for PairingSessionTrustedAction',
      () async {
        sdk.trustState = DovahLinkTrustState.unpaired;
        middleware.initialize(store);
        await pumpEventQueue();
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();

        expect(sdk.requestedAreas, isEmpty);
        expect(sdk.vitalsStreamReads, 0);

        sdk.trustState = DovahLinkTrustState.trusted;
        middleware.call(store, const PairingSessionTrustedAction(), (_) {});
        await pumpEventQueue();

        expect(sdk.requestedAreas, _expectedAreas);
        expect(sdk.vitalsStreamReads, 1);
      },
    );

    test(
      'duplicate trusted actions and reconnect transitions do not reattach streams',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        middleware.call(store, const PairingSessionTrustedAction(), (_) {});
        sdk.emitConnectionState(DovahLinkConnectionState.reconnecting);
        sdk.emitConnectionState(DovahLinkConnectionState.reauthenticating);
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();

        expect(sdk.requestedAreas, _expectedAreas);
        expect(sdk.vitalsStreamReads, 1);
        expect(sdk.xpStreamReads, 1);
        sdk.xp.add(
          const StateSynchronization<CharacterXpState>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterXpState(value: 42),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 2,
          ),
        );
        await pumpEventQueue();

        expect(
          actions.whereType<CharacterXpSynchronizationChangedAction>(),
          hasLength(1),
        );
      },
    );

    test(
      'ordinary reconnect keeps projection listeners through fresh baselines',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        sdk.vitals.add(
          StateSynchronization<CharacterVitalsState>(
            status: DovahLinkStateStatus.stale,
            value: CharacterVitalsState(
              health: const CharacterVital(current: 88, max: 100),
              magicka: const CharacterVital(current: 55, max: 80),
              stamina: const CharacterVital(current: 48, max: 90),
            ),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 4,
          ),
        );
        sdk.emitConnectionState(DovahLinkConnectionState.reconnecting);
        sdk.emitConnectionState(DovahLinkConnectionState.reauthenticating);
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        sdk.vitals.add(
          StateSynchronization<CharacterVitalsState>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterVitalsState(
              health: const CharacterVital(current: 89, max: 100),
              magicka: const CharacterVital(current: 54, max: 80),
              stamina: const CharacterVital(current: 49, max: 90),
            ),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 5,
          ),
        );
        await pumpEventQueue();

        final List<CharacterVitalsSynchronizationChangedAction> projections =
            actions
                .whereType<CharacterVitalsSynchronizationChangedAction>()
                .toList();
        expect(projections, hasLength(2));
        expect(projections.first.synchronization.status, LiveStateStatus.stale);
        expect(projections.first.synchronization.value?.health.current, 88);
        expect(
          projections.last.synchronization.status,
          LiveStateStatus.synchronized,
        );
        expect(projections.last.synchronization.revision, 5);
        expect(actions.whereType<SessionLiveStateResetAction>(), isEmpty);
        expect(sdk.vitalsStreamReads, 1);
        expect(sdk.requestedAreas, _expectedAreas);
      },
    );

    test(
      'actual disconnect cancels listeners and resets every projected domain',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        sdk.xp.add(
          const StateSynchronization<CharacterXpState>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterXpState(value: 42),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 1,
          ),
        );
        await pumpEventQueue();
        sdk.emitConnectionState(DovahLinkConnectionState.disconnected);
        await pumpEventQueue();

        expect(actions.whereType<SessionLiveStateResetAction>(), hasLength(1));
        expect(sdk.vitals.hasListener, isFalse);
        expect(sdk.xp.hasListener, isFalse);
        verifyNever(() => sdk.connections.disconnect());

        sdk.xp.add(
          const StateSynchronization<CharacterXpState>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterXpState(value: 50),
            stateAuthorityId: 'authority-b',
            playContextId: 'context-b',
            revision: 1,
          ),
        );
        await pumpEventQueue();

        expect(
          actions.whereType<CharacterXpSynchronizationChangedAction>(),
          hasLength(1),
        );
      },
    );

    test(
      'administrative invalidation resets projection without an app disconnect',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        sdk.emitConnectionState(
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        await pumpEventQueue();

        expect(actions.whereType<SessionLiveStateResetAction>(), hasLength(1));
        expect(sdk.vitals.hasListener, isFalse);
        verifyNever(() => sdk.connections.disconnect());
      },
    );

    test(
      'Session Shell Back keeps state listeners attached and does not disconnect',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        middleware.call(store, const SessionShellBackRequestedAction(), (_) {});
        sdk.xp.add(
          const StateSynchronization<CharacterXpState>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterXpState(value: 42),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 1,
          ),
        );
        await pumpEventQueue();

        expect(sdk.xp.hasListener, isTrue);
        expect(
          actions.whereType<CharacterXpSynchronizationChangedAction>(),
          hasLength(1),
        );
        verifyNever(() => sdk.connections.disconnect());
      },
    );

    test(
      'shutdown cancels listeners and prevents later reinitialization',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await middleware.shutdown();
        final int requestedAreaCount = sdk.requestedAreas.length;
        middleware.initialize(store);
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();

        expect(sdk.vitals.hasListener, isFalse);
        expect(sdk.requestedAreas, hasLength(requestedAreaCount));
      },
    );
  });

  group('LiveStateMiddleware projects SDK domain streams', () {
    test('all eight typed SDK streams dispatch their domain actions', () async {
      sdk.connectionState = DovahLinkConnectionState.connected;
      middleware.initialize(store);
      await pumpEventQueue();
      sdk.vitals.add(
        StateSynchronization<CharacterVitalsState>(
          status: DovahLinkStateStatus.synchronized,
          value: CharacterVitalsState(
            health: const CharacterVital(current: 100, max: 100),
            magicka: const CharacterVital(current: 70, max: 80),
            stamina: const CharacterVital(current: 65, max: 90),
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 1,
        ),
      );
      sdk.xp.add(
        const StateSynchronization<CharacterXpState>(
          status: DovahLinkStateStatus.unavailable,
          value: CharacterXpState(value: null),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 2,
        ),
      );
      sdk.level.add(
        const StateSynchronization<CharacterLevelState>(
          status: DovahLinkStateStatus.synchronized,
          value: CharacterLevelState(value: 43),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 3,
        ),
      );
      sdk.identity.add(
        const StateSynchronization<CharacterIdentityState?>(
          status: DovahLinkStateStatus.synchronized,
          value: CharacterIdentityState(name: 'Player', race: 'Nord'),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 4,
        ),
      );
      sdk.supernaturalTraits.add(
        const StateSynchronization<CharacterSupernaturalTraitsState?>(
          status: DovahLinkStateStatus.synchronized,
          value: CharacterSupernaturalTraitsState(
            isVampire: false,
            hasVampireLordForm: false,
            hasWerewolfForm: false,
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 5,
        ),
      );
      sdk.location.add(
        const StateSynchronization<PlayerLocationState?>(
          status: DovahLinkStateStatus.synchronized,
          value: PlayerLocationState(
            cellId: 1,
            cellKind: PlayerLocationCellKind.exterior,
            cellName: null,
            locationId: null,
            locationName: null,
            worldspaceId: null,
            worldspaceName: null,
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 6,
        ),
      );
      sdk.gameTime.add(
        const StateSynchronization<GameTimeState?>(
          status: DovahLinkStateStatus.synchronized,
          value: GameTimeState(
            year: 4,
            month: 8,
            monthName: 'Last Seed',
            day: 12,
            hour: 14,
            minute: 30,
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 7,
        ),
      );
      sdk.quests.add(
        StateSynchronization<TrackedQuestsState?>(
          status: DovahLinkStateStatus.synchronized,
          value: TrackedQuestsState(quests: const []),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 8,
        ),
      );
      await pumpEventQueue();

      expect(
        actions.whereType<CharacterVitalsSynchronizationChangedAction>(),
        hasLength(1),
      );
      expect(
        actions
            .whereType<CharacterXpSynchronizationChangedAction>()
            .single
            .synchronization
            .status,
        LiveStateStatus.unavailable,
      );
      expect(
        actions
            .whereType<CharacterLevelSynchronizationChangedAction>()
            .single
            .synchronization
            .value,
        43,
      );
      expect(
        actions
            .whereType<CharacterIdentitySynchronizationChangedAction>()
            .single
            .synchronization
            .value
            ?.race,
        'Nord',
      );
      expect(
        actions
            .whereType<
              CharacterSupernaturalTraitsSynchronizationChangedAction
            >()
            .single
            .synchronization
            .value
            ?.isVampire,
        isFalse,
      );
      expect(
        actions
            .whereType<PlayerLocationSynchronizationChangedAction>()
            .single
            .synchronization
            .value
            ?.locationName,
        isNull,
      );
      expect(
        actions
            .whereType<GameTimeSynchronizationChangedAction>()
            .single
            .synchronization
            .value
            ?.monthName,
        'Last Seed',
      );
      expect(
        actions
            .whereType<TrackedQuestsSynchronizationChangedAction>()
            .single
            .synchronization
            .value,
        isEmpty,
      );
    });

    test(
      'SDK failed, stale, recovering, and notSubscribed statuses pass through',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        for (final DovahLinkStateStatus status in <DovahLinkStateStatus>[
          DovahLinkStateStatus.notSubscribed,
          DovahLinkStateStatus.stale,
          DovahLinkStateStatus.recovering,
          DovahLinkStateStatus.failed,
        ]) {
          sdk.xp.add(
            StateSynchronization<CharacterXpState>(
              status: status,
              value: const CharacterXpState(value: 42),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 9,
            ),
          );
        }
        await pumpEventQueue();

        expect(
          actions.whereType<CharacterXpSynchronizationChangedAction>().map(
            (action) => action.synchronization.status,
          ),
          [
            LiveStateStatus.notSubscribed,
            LiveStateStatus.stale,
            LiveStateStatus.recovering,
            LiveStateStatus.failed,
          ],
        );
      },
    );
  });
}

/// The app's requested state areas in middleware call order.
const List<DovahLinkStateArea> _expectedAreas = <DovahLinkStateArea>[
  DovahLinkStateArea.characterVitals,
  DovahLinkStateArea.characterXp,
  DovahLinkStateArea.characterLevel,
  DovahLinkStateArea.characterIdentity,
  DovahLinkStateArea.characterSupernaturalTraits,
  DovahLinkStateArea.playerLocation,
  DovahLinkStateArea.gameTime,
  DovahLinkStateArea.trackedQuests,
];
