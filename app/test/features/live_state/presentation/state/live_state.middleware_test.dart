import 'dart:async';

import 'package:flutter/foundation.dart' show FlutterError, FlutterErrorDetails;

import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.middleware.dart';
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

/// Delays stream cancellation so tests can deliver a callback from an ended session.
class _DelayedCancelStream<T> extends Stream<T> {
  /// Creates a wrapper over [source] whose subscriptions await [cancelGate].
  _DelayedCancelStream(this.source, this.cancelGate);

  /// The source stream that receives test-controlled values.
  final Stream<T> source;

  /// The gate that holds source cancellation after middleware requests it.
  final Future<void> cancelGate;

  /// Subscribes to [source] and delays only the returned cancellation operation.
  @override
  StreamSubscription<T> listen(
    void Function(T)? onData, {
    Function? onError,
    void Function()? onDone,
    bool? cancelOnError,
  }) => _DelayedCancelSubscription<T>(
    source.listen(
      onData,
      onError: onError,
      onDone: onDone,
      cancelOnError: cancelOnError,
    ),
    cancelGate,
  );
}

/// Holds a source subscription active until the test releases its cancellation gate.
class _DelayedCancelSubscription<T> implements StreamSubscription<T> {
  /// Creates a wrapper over [source] and [cancelGate].
  _DelayedCancelSubscription(this.source, this.cancelGate);

  /// The source subscription controlled by the test.
  final StreamSubscription<T> source;

  /// The gate that delays forwarding [cancel].
  final Future<void> cancelGate;

  /// Implements [StreamSubscription.asFuture].
  @override
  Future<E> asFuture<E>([E? futureValue]) => source.asFuture<E>(futureValue);

  /// Implements [StreamSubscription.cancel] after the test releases the gate.
  @override
  Future<void> cancel() async {
    await cancelGate;
    await source.cancel();
  }

  /// Implements [StreamSubscription.isPaused].
  @override
  bool get isPaused => source.isPaused;

  /// Implements [StreamSubscription.onData].
  @override
  void onData(void Function(T)? handleData) => source.onData(handleData);

  /// Implements [StreamSubscription.onDone].
  @override
  void onDone(void Function()? handleDone) => source.onDone(handleDone);

  /// Implements [StreamSubscription.onError].
  @override
  void onError(Function? handleError) => source.onError(handleError);

  /// Implements [StreamSubscription.pause].
  @override
  void pause([Future<void>? resumeSignal]) => source.pause(resumeSignal);

  /// Implements [StreamSubscription.resume].
  @override
  void resume() => source.resume();
}

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

  /// State area returned as rejected by the SDK fake, when set.
  DovahLinkStateArea? rejectedArea;

  /// State area whose subscription request throws, when set.
  DovahLinkStateArea? failingArea;

  /// State area whose subscription response is controlled by a test.
  DovahLinkStateArea? pendingArea;

  /// Completes a delayed subscription response.
  Completer<Set<DovahLinkStateArea>>? pendingSubscription;

  /// A replacement XP stream supplied for a particular admitted session.
  Stream<StateSynchronization<CharacterXpState>>? xpStreamOverride;

  /// Whether XP subscriptions should delay forwarding cancellation.
  bool delayXpCancellation = false;

  /// Holds a delayed XP cancellation until a test releases it.
  final Completer<void> xpCancellationGate = Completer<void>();

  /// Additional XP sources used to separate old and replacement sessions.
  final List<StreamController<StateSynchronization<CharacterXpState>>>
  additionalXpSources =
      <StreamController<StateSynchronization<CharacterXpState>>>[];

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
      final Stream<StateSynchronization<CharacterXpState>> changes =
          xpStreamOverride ?? xp.stream;
      return delayXpCancellation
          ? _DelayedCancelStream<StateSynchronization<CharacterXpState>>(
              changes,
              xpCancellationGate.future,
            )
          : changes;
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
    ) {
      final DovahLinkStateArea area =
          invocation.positionalArguments.single as DovahLinkStateArea;
      requestedAreas.add(area);
      if (area == failingArea) {
        return Future<Set<DovahLinkStateArea>>.error(
          StateError('Subscription request failed.'),
        );
      }
      if (area == pendingArea && pendingSubscription != null) {
        return pendingSubscription!.future;
      }
      return Future<Set<DovahLinkStateArea>>.value(
        area == rejectedArea
            ? <DovahLinkStateArea>{area}
            : <DovahLinkStateArea>{},
      );
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
      for (final StreamController<StateSynchronization<CharacterXpState>> source
          in additionalXpSources)
        source.close(),
    ]);
  }
}

/// Exercises SDK lifecycle, subscription ownership, projection, and teardown.
void main() {
  late LiveStateSdkFake sdk;
  late LiveStateMiddleware middleware;
  late MockStore store;
  late List<Object?> actions;

  setUpAll(() {
    registerFallbackValue(DovahLinkStateArea.gameTime);
  });

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
    if (!sdk.xpCancellationGate.isCompleted) {
      sdk.xpCancellationGate.complete();
    }
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
        await _trustCurrentSession(sdk, middleware, store);

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
        final List<Object?> forwarded = <Object?>[];
        const PairingSessionTrustedAction trustedAction =
            PairingSessionTrustedAction();
        middleware.call(store, trustedAction, forwarded.add);
        middleware.call(store, trustedAction, forwarded.add);
        await pumpEventQueue();

        expect(forwarded, [trustedAction, trustedAction]);
        expect(sdk.requestedAreas, _expectedAreas);
        expect(sdk.vitalsStreamReads, 1);
      },
    );

    test(
      'subscription rejection is reported while later domains are still requested',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        sdk.rejectedArea = DovahLinkStateArea.gameTime;
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);

        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        expect(sdk.requestedAreas, _expectedAreas);
        expect(reported, hasLength(1));
        expect(reported.single.exception, isA<StateError>());
        expect(sdk.questsStreamReads, 1);
      },
    );

    test(
      'subscription failure is reported while later domains are still requested',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        sdk.failingArea = DovahLinkStateArea.gameTime;
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);

        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        expect(sdk.requestedAreas, _expectedAreas);
        expect(reported, hasLength(1));
        expect(reported.single.exception, isA<StateError>());
        expect(sdk.questsStreamReads, 1);
      },
    );

    test(
      'late subscription acknowledgement after disconnect stops the remaining requests',
      () async {
        sdk.pendingArea = DovahLinkStateArea.characterVitals;
        sdk.pendingSubscription = Completer<Set<DovahLinkStateArea>>();
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        expect(sdk.requestedAreas, [DovahLinkStateArea.characterVitals]);
        sdk.emitConnectionState(DovahLinkConnectionState.disconnected);
        sdk.pendingSubscription!.complete(<DovahLinkStateArea>{});
        await pumpEventQueue();

        expect(sdk.requestedAreas, [DovahLinkStateArea.characterVitals]);
        expect(actions.whereType<SessionLiveStateResetAction>(), hasLength(1));
      },
    );

    test(
      'a delayed session failure cannot continue requests or report in a replacement session',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        sdk.pendingArea = DovahLinkStateArea.characterVitals;
        final Completer<Set<DovahLinkStateArea>> sessionAResponse =
            Completer<Set<DovahLinkStateArea>>();
        sdk.pendingSubscription = sessionAResponse;
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        expect(sdk.requestedAreas, [DovahLinkStateArea.characterVitals]);
        sdk.emitConnectionState(DovahLinkConnectionState.disconnected);
        sdk.pendingArea = null;
        sdk.pendingSubscription = null;
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
        await pumpEventQueue();

        expect(sdk.requestedAreas, [
          DovahLinkStateArea.characterVitals,
          ..._expectedAreas,
        ]);
        sessionAResponse.completeError(
          StateError('Session A subscription failed late.'),
          StackTrace.current,
        );
        await pumpEventQueue();

        expect(sdk.requestedAreas, [
          DovahLinkStateArea.characterVitals,
          ..._expectedAreas,
        ]);
        expect(reported, isEmpty);
      },
    );

    test(
      'a delayed session rejection cannot continue requests or report in a replacement session',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        sdk.pendingArea = DovahLinkStateArea.characterVitals;
        final Completer<Set<DovahLinkStateArea>> sessionAResponse =
            Completer<Set<DovahLinkStateArea>>();
        sdk.pendingSubscription = sessionAResponse;
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        expect(sdk.requestedAreas, [DovahLinkStateArea.characterVitals]);
        sdk.emitConnectionState(DovahLinkConnectionState.disconnected);
        sdk.pendingArea = null;
        sdk.pendingSubscription = null;
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
        await pumpEventQueue();

        expect(sdk.requestedAreas, [
          DovahLinkStateArea.characterVitals,
          ..._expectedAreas,
        ]);
        sessionAResponse.complete(<DovahLinkStateArea>{
          DovahLinkStateArea.characterVitals,
        });
        await pumpEventQueue();

        expect(sdk.requestedAreas, [
          DovahLinkStateArea.characterVitals,
          ..._expectedAreas,
        ]);
        expect(reported, isEmpty);
      },
    );

    test(
      'late subscription acknowledgement after shutdown starts no more requests',
      () async {
        sdk.pendingArea = DovahLinkStateArea.characterVitals;
        sdk.pendingSubscription = Completer<Set<DovahLinkStateArea>>();
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        final Future<void> shutdown = middleware.shutdown();
        sdk.pendingSubscription!.complete(<DovahLinkStateArea>{});
        await Future.wait<void>([shutdown, pumpEventQueue()]);

        expect(sdk.requestedAreas, [DovahLinkStateArea.characterVitals]);
        expect(sdk.vitals.hasListener, isFalse);
      },
    );

    test(
      'a stream error is reported while the listener keeps observing',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        sdk.xp.addError(StateError('SDK stream failed.'), StackTrace.current);
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

        expect(reported, hasLength(1));
        expect(reported.single.exception, isA<StateError>());
        expect(
          actions.whereType<CharacterXpSynchronizationChangedAction>(),
          hasLength(1),
        );
      },
    );

    test(
      'a lifecycle stream error is reported and later transitions still work',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        middleware.initialize(store);
        await pumpEventQueue();

        sdk.lifecycle.addError(
          StateError('Lifecycle stream failed.'),
          StackTrace.current,
        );
        await pumpEventQueue();
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        expect(reported, hasLength(1));
        expect(reported.single.exception, isA<StateError>());
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
        await _trustCurrentSession(sdk, middleware, store);
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
        expect(
          projections.first.synchronization.status,
          DovahLinkStateStatus.stale,
        );
        expect(projections.first.synchronization.value?.health?.current, 88);
        expect(
          projections.last.synchronization.status,
          DovahLinkStateStatus.synchronized,
        );
        expect(projections.last.synchronization.revision, 5);
        expect(actions.whereType<SessionLiveStateResetAction>(), isEmpty);
        expect(sdk.vitalsStreamReads, 1);
        expect(sdk.requestedAreas, _expectedAreas);
      },
    );

    test(
      'late callbacks from an ended session are ignored while cancellation is pending',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        final StreamController<StateSynchronization<CharacterXpState>>
        sessionAXp =
            StreamController<StateSynchronization<CharacterXpState>>.broadcast(
              sync: true,
            );
        final StreamController<StateSynchronization<CharacterXpState>>
        sessionBXp =
            StreamController<StateSynchronization<CharacterXpState>>.broadcast(
              sync: true,
            );
        sdk.additionalXpSources.addAll([sessionAXp, sessionBXp]);
        sdk.delayXpCancellation = true;
        sdk.xpStreamOverride = sessionAXp.stream;
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);

        sdk.emitConnectionState(DovahLinkConnectionState.disconnected);
        expect(sessionAXp.hasListener, isTrue);
        sdk.delayXpCancellation = false;
        sdk.xpStreamOverride = sessionBXp.stream;
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
        await _trustCurrentSession(sdk, middleware, store);

        expect(sdk.vitalsStreamReads, 2);
        expect(sdk.xpStreamReads, 2);
        expect(sdk.levelStreamReads, 2);
        expect(sdk.identityStreamReads, 2);
        expect(sdk.supernaturalTraitsStreamReads, 2);
        expect(sdk.locationStreamReads, 2);
        expect(sdk.gameTimeStreamReads, 2);
        expect(sdk.questsStreamReads, 2);
        expect(sessionBXp.hasListener, isTrue);

        sessionAXp.add(
          const StateSynchronization<CharacterXpState>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterXpState(value: 11),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 1,
          ),
        );
        await pumpEventQueue();
        expect(
          actions.whereType<CharacterXpSynchronizationChangedAction>(),
          isEmpty,
        );
        sessionAXp.addError(
          StateError('Session A stream failed after replacement.'),
          StackTrace.current,
        );
        await pumpEventQueue();
        expect(reported, isEmpty);

        const StateSynchronization<CharacterXpState> sessionBSynchronization =
            StateSynchronization<CharacterXpState>(
              status: DovahLinkStateStatus.synchronized,
              value: CharacterXpState(value: 52),
              stateAuthorityId: 'authority-b',
              playContextId: 'context-b',
              revision: 1,
            );
        sessionBXp.add(sessionBSynchronization);
        await pumpEventQueue();

        final CharacterXpSynchronizationChangedAction accepted = actions
            .whereType<CharacterXpSynchronizationChangedAction>()
            .single;
        expect(
          identical(accepted.synchronization, sessionBSynchronization),
          isTrue,
        );

        sdk.xpCancellationGate.complete();
        await pumpEventQueue();
        expect(sessionAXp.hasListener, isFalse);
        expect(sessionBXp.hasListener, isTrue);
      },
    );

    test(
      'actual disconnect cancels listeners and resets every projected domain',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
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

        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
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

        final List<CharacterXpSynchronizationChangedAction> xpProjections =
            actions
                .whereType<CharacterXpSynchronizationChangedAction>()
                .toList();
        expect(xpProjections, hasLength(2));
        expect(xpProjections.first.synchronization.value?.value, 42);
        expect(xpProjections.first.synchronization.playContextId, 'context-a');
        expect(xpProjections.last.synchronization.value?.value, 50);
        expect(xpProjections.last.synchronization.playContextId, 'context-b');
        expect(
          actions.indexOf(const SessionLiveStateResetAction()),
          lessThan(actions.indexOf(xpProjections.last)),
        );
        expect(sdk.requestedAreas, <DovahLinkStateArea>[
          ..._expectedAreas,
          ..._expectedAreas,
        ]);
      },
    );

    test(
      'administrative invalidation resets projection without an app disconnect',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
        sdk.emitConnectionState(
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        await pumpEventQueue();

        expect(actions.whereType<SessionLiveStateResetAction>(), hasLength(1));
        expect(sdk.vitals.hasListener, isFalse);
        verifyNever(() => sdk.connections.disconnect());

        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
        expect(sdk.requestedAreas, [..._expectedAreas, ..._expectedAreas]);
        expect(sdk.vitalsStreamReads, 2);
      },
    );

    test(
      'Session Shell Back keeps state listeners attached and does not disconnect',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
        final List<Object?> forwarded = <Object?>[];
        const SessionShellBackRequestedAction action =
            SessionShellBackRequestedAction();
        middleware.call(store, action, forwarded.add);
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
        expect(forwarded, [action]);
        expect(
          actions.whereType<CharacterXpSynchronizationChangedAction>(),
          hasLength(1),
        );
        verifyNever(() => sdk.connections.disconnect());
      },
    );

    test(
      'PairingDisposedAction trust variants are forwarded without owning teardown',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
        final List<Object?> forwarded = <Object?>[];
        const PairingDisposedAction untrustedDispose = PairingDisposedAction(
          wasTrusted: false,
        );
        const PairingDisposedAction trustedDispose = PairingDisposedAction(
          wasTrusted: true,
        );

        middleware.call(store, untrustedDispose, forwarded.add);
        middleware.call(store, trustedDispose, forwarded.add);

        expect(forwarded, [untrustedDispose, trustedDispose]);
        expect(sdk.xp.hasListener, isTrue);
        verifyNever(() => sdk.connections.disconnect());
      },
    );

    test(
      'shutdown cancels listeners and prevents later reinitialization',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
        final Future<void> firstShutdown = middleware.shutdown();
        final Future<void> repeatedShutdown = middleware.shutdown();
        expect(identical(firstShutdown, repeatedShutdown), isTrue);
        await firstShutdown;
        final int requestedAreaCount = sdk.requestedAreas.length;
        middleware.initialize(store);
        sdk.emitConnectionState(DovahLinkConnectionState.connected);
        await pumpEventQueue();

        expect(sdk.vitals.hasListener, isFalse);
        expect(sdk.requestedAreas, hasLength(requestedAreaCount));

        final LiveStateMiddleware replacement = LiveStateMiddleware();
        replacement.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, replacement, store);
        expect(sdk.vitals.hasListener, isTrue);
        expect(sdk.requestedAreas, <DovahLinkStateArea>[
          ..._expectedAreas,
          ..._expectedAreas,
        ]);
        await replacement.shutdown();
      },
    );

    test('shutdown waits until delayed stream cancellation finishes', () async {
      sdk.delayXpCancellation = true;
      sdk.connectionState = DovahLinkConnectionState.connected;
      middleware.initialize(store);
      await pumpEventQueue();
      await _trustCurrentSession(sdk, middleware, store);

      final Future<void> shutdown = middleware.shutdown();
      bool isShutdownComplete = false;
      unawaited(shutdown.then((_) => isShutdownComplete = true));
      await pumpEventQueue();

      expect(isShutdownComplete, isFalse);
      expect(sdk.xp.hasListener, isTrue);
      sdk.xpCancellationGate.complete();
      await shutdown;

      expect(isShutdownComplete, isTrue);
      expect(sdk.xp.hasListener, isFalse);
    });
  });

  group('LiveStateMiddleware projects SDK domain streams', () {
    test('all eight typed SDK streams dispatch their domain actions', () async {
      sdk.connectionState = DovahLinkConnectionState.connected;
      middleware.initialize(store);
      await pumpEventQueue();
      await _trustCurrentSession(sdk, middleware, store);
      final StateSynchronization<CharacterVitalsState> vitalsSynchronization =
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
          );
      const StateSynchronization<CharacterXpState> xpSynchronization =
          StateSynchronization<CharacterXpState>(
            status: DovahLinkStateStatus.unavailable,
            value: CharacterXpState(value: null),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 2,
          );
      const StateSynchronization<CharacterLevelState> levelSynchronization =
          StateSynchronization<CharacterLevelState>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterLevelState(value: 43),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 3,
          );
      const StateSynchronization<CharacterIdentityState?>
      identitySynchronization = StateSynchronization<CharacterIdentityState?>(
        status: DovahLinkStateStatus.synchronized,
        value: CharacterIdentityState(name: 'Player', race: 'Nord'),
        stateAuthorityId: 'authority-a',
        playContextId: 'context-a',
        revision: 4,
      );
      const StateSynchronization<CharacterSupernaturalTraitsState?>
      traitsSynchronization =
          StateSynchronization<CharacterSupernaturalTraitsState?>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterSupernaturalTraitsState(
              isVampire: false,
              hasVampireLordForm: false,
              hasWerewolfForm: false,
            ),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 5,
          );
      const StateSynchronization<PlayerLocationState?> locationSynchronization =
          StateSynchronization<PlayerLocationState?>(
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
          );
      const StateSynchronization<GameTimeState?> timeSynchronization =
          StateSynchronization<GameTimeState?>(
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
          );
      final StateSynchronization<TrackedQuestsState?> questsSynchronization =
          StateSynchronization<TrackedQuestsState?>(
            status: DovahLinkStateStatus.synchronized,
            value: TrackedQuestsState(quests: const []),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 8,
          );
      sdk.vitals.add(vitalsSynchronization);
      sdk.xp.add(xpSynchronization);
      sdk.level.add(levelSynchronization);
      sdk.identity.add(identitySynchronization);
      sdk.supernaturalTraits.add(traitsSynchronization);
      sdk.location.add(locationSynchronization);
      sdk.gameTime.add(timeSynchronization);
      sdk.quests.add(questsSynchronization);
      await pumpEventQueue();

      expect(
        actions
            .whereType<CharacterVitalsSynchronizationChangedAction>()
            .single
            .synchronization,
        same(vitalsSynchronization),
      );
      expect(
        actions
            .whereType<CharacterXpSynchronizationChangedAction>()
            .single
            .synchronization,
        same(xpSynchronization),
      );
      expect(
        actions
            .whereType<CharacterLevelSynchronizationChangedAction>()
            .single
            .synchronization,
        same(levelSynchronization),
      );
      expect(
        actions
            .whereType<CharacterIdentitySynchronizationChangedAction>()
            .single
            .synchronization,
        same(identitySynchronization),
      );
      expect(
        actions
            .whereType<
              CharacterSupernaturalTraitsSynchronizationChangedAction
            >()
            .single
            .synchronization,
        same(traitsSynchronization),
      );
      expect(
        actions
            .whereType<PlayerLocationSynchronizationChangedAction>()
            .single
            .synchronization,
        same(locationSynchronization),
      );
      expect(
        actions
            .whereType<GameTimeSynchronizationChangedAction>()
            .single
            .synchronization,
        same(timeSynchronization),
      );
      expect(
        actions
            .whereType<TrackedQuestsSynchronizationChangedAction>()
            .single
            .synchronization,
        same(questsSynchronization),
      );
    });

    test(
      'SDK failed, stale, recovering, and notSubscribed statuses pass through',
      () async {
        sdk.connectionState = DovahLinkConnectionState.connected;
        middleware.initialize(store);
        await pumpEventQueue();
        await _trustCurrentSession(sdk, middleware, store);
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
            DovahLinkStateStatus.notSubscribed,
            DovahLinkStateStatus.stale,
            DovahLinkStateStatus.recovering,
            DovahLinkStateStatus.failed,
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

/// Dispatches SDK trust admission after lifecycle observation is attached.
/// @param sdk The SDK fake reporting the connected session.
/// @param middleware The middleware receiving trust admission.
/// @param store The mock Redux store receiving projections.
Future<void> _trustCurrentSession(
  LiveStateSdkFake sdk,
  LiveStateMiddleware middleware,
  MockStore store,
) async {
  expect(sdk.connectionState, DovahLinkConnectionState.connected);
  expect(sdk.trustState, DovahLinkTrustState.trusted);
  middleware.call(store, const PairingSessionTrustedAction(), (_) {});
  await pumpEventQueue();
}
