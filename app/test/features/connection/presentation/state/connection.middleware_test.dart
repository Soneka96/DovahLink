import 'dart:async';

import 'package:flutter/foundation.dart' show FlutterError, FlutterErrorDetails;

import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/host.mapper.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkCompatibilityException,
        DovahLinkConnectionException,
        DovahLinkClient,
        DovahLinkHost,
        DovahLinkHostAvailability,
        DovahLinkKnownHostState,
        IDovahLinkHosts,
        IDovahLinkPairing,
        DovahLinkProtocolException,
        DovahLinkTrustState,
        HelloResult,
        HostVersionCompatibilityFailure,
        ProtocolErrorCode;

/// Mocks the SDK client that owns Known Host state.
class MockDovahLinkClient extends Mock implements DovahLinkClient {}

/// Mocks grouped Known Host APIs.
class MockDovahLinkHosts extends Mock implements IDovahLinkHosts {}

/// Mocks grouped discovery and pairing APIs.
class MockDovahLinkPairing extends Mock implements IDovahLinkPairing {}

/// Mocks Redux dispatch for [ConnectionMiddleware] tests.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises discovery action orchestration by [ConnectionMiddleware].
void main() {
  late MockDovahLinkClient mockClient;
  late MockDovahLinkHosts mockHosts;
  late MockDovahLinkPairing mockPairing;
  late StreamController<List<DovahLinkKnownHostState>>
  knownHostStatesController;
  late Stream<List<DovahLinkKnownHostState>> knownHostStatesChanges;
  late StreamController<List<DovahLinkHost>> candidateHostsController;
  late Stream<List<DovahLinkHost>> candidateHostsChanges;
  late int knownHostListenerCount;
  late int knownHostCancellationCount;
  late int candidateListenerCount;
  late int candidateCancellationCount;
  late MockStore store;
  late ConnectionMiddleware middleware;

  setUp(() async {
    await sl.reset();
    mockClient = MockDovahLinkClient();
    mockHosts = MockDovahLinkHosts();
    mockPairing = MockDovahLinkPairing();
    knownHostStatesController =
        StreamController<List<DovahLinkKnownHostState>>.broadcast();
    knownHostListenerCount = 0;
    knownHostCancellationCount = 0;
    candidateListenerCount = 0;
    candidateCancellationCount = 0;
    knownHostStatesChanges = Stream<List<DovahLinkKnownHostState>>.multi((
      sink,
    ) {
      knownHostListenerCount++;
      final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
          knownHostStatesController.stream.listen(
            sink.add,
            onError: sink.addError,
          );
      sink.onCancel = () async {
        knownHostCancellationCount++;
        await subscription.cancel();
      };
    }, isBroadcast: true);
    candidateHostsController =
        StreamController<List<DovahLinkHost>>.broadcast();
    candidateHostsChanges = Stream<List<DovahLinkHost>>.multi((sink) {
      candidateListenerCount++;
      final StreamSubscription<List<DovahLinkHost>> subscription =
          candidateHostsController.stream.listen(
            sink.add,
            onError: sink.addError,
          );
      sink.onCancel = () async {
        candidateCancellationCount++;
        await subscription.cancel();
      };
    }, isBroadcast: true);
    when(() => mockClient.hosts).thenReturn(mockHosts);
    when(() => mockClient.pairing).thenReturn(mockPairing);
    when(
      () => mockHosts.knownHostStatesChanges,
    ).thenAnswer((_) => knownHostStatesChanges);
    when(() => mockPairing.candidates).thenAnswer((_) => candidateHostsChanges);
    store = MockStore();
    when(() => store.state).thenReturn(AppState.initial());
    middleware = ConnectionMiddleware();
    sl.registerSingleton<DovahLinkClient>(mockClient);
  });

  tearDown(() async {
    await middleware.shutdown();
    await knownHostStatesController.close();
    await candidateHostsController.close();
    await sl.reset();
  });

  group('ConnectionMiddleware Known Host observation behaves correctly', () {
    test('initialize dispatches the SDK-reported empty collection', () async {
      final List<Object?> actions = <Object?>[];
      final Store<AppState> integrationStore = const CreateStore()(
        middleware: [
          (Store<AppState> _, dynamic action, NextDispatcher next) {
            actions.add(action);
            next(action);
          },
          middleware.call,
        ],
      );

      middleware.initialize(integrationStore);
      knownHostStatesController.add(const <DovahLinkKnownHostState>[]);
      await pumpEventQueue();

      expect(knownHostListenerCount, 1);
      expect(actions.whereType<ConnectionKnownHostsChangedAction>(), [
        ConnectionKnownHostsChangedAction(const <KnownHost>[]),
      ]);
      expect(
        actions.whereType<ConnectionKnownHostsObservationFailedAction>(),
        isEmpty,
      );
      expect(integrationStore.state.connection.knownHosts, isEmpty);
      expect(
        integrationStore.state.connection.knownHostsStatus,
        KnownHostsObservationStatus.ready,
      );
    });

    test(
      'initialize maps SDK Known Host states and replaces the Redux projection',
      () async {
        final Store<AppState> integrationStore = const CreateStore()(
          middleware: [middleware.call],
        );
        final DovahLinkHost first = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'HOST-A',
          endpoint: defaultHostUri,
        );
        final DovahLinkHost second = DovahLinkHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          hostName: 'HOST-B',
          endpoint: Uri.parse('ws://127.0.0.1:58232/'),
        );
        final DovahLinkKnownHostState firstOnline =
            Fixtures.buildSdkKnownHostState(
              host: first,
              availability: DovahLinkHostAvailability.online,
            );
        final DovahLinkKnownHostState secondOffline =
            Fixtures.buildSdkKnownHostState(
              host: second,
              availability: DovahLinkHostAvailability.offline,
            );

        middleware.initialize(integrationStore);
        middleware.initialize(integrationStore);
        knownHostStatesController.add(<DovahLinkKnownHostState>[firstOnline]);
        await pumpEventQueue();
        expect(integrationStore.state.connection.knownHosts, <KnownHost>[
          HostMapper.fromSdkKnownHostState(firstOnline),
        ]);
        knownHostStatesController.add(<DovahLinkKnownHostState>[
          firstOnline,
          secondOffline,
        ]);
        await pumpEventQueue();

        expect(knownHostListenerCount, 1);
        expect(candidateListenerCount, 1);
        expect(integrationStore.state.connection.knownHosts, <KnownHost>[
          HostMapper.fromSdkKnownHostState(firstOnline),
          HostMapper.fromSdkKnownHostState(secondOffline),
        ]);
        expect(
          integrationStore.state.connection.knownHostsStatus,
          KnownHostsObservationStatus.ready,
        );

        knownHostStatesController.add(<DovahLinkKnownHostState>[secondOffline]);
        await pumpEventQueue();

        expect(integrationStore.state.connection.knownHosts, <KnownHost>[
          HostMapper.fromSdkKnownHostState(secondOffline),
        ]);
      },
    );

    test(
      'initialize observes each store and shutdown cancels every subscription',
      () async {
        final Store<AppState> firstStore = const CreateStore()(
          middleware: [middleware.call],
        );
        final Store<AppState> secondStore = const CreateStore()(
          middleware: [middleware.call],
        );
        final DovahLinkHost first = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'HOST-A',
          endpoint: defaultHostUri,
        );
        final DovahLinkHost second = DovahLinkHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          hostName: 'HOST-B',
          endpoint: Uri.parse('ws://127.0.0.1:58232/'),
        );
        final DovahLinkKnownHostState firstState =
            Fixtures.buildSdkKnownHostState(host: first);
        middleware
          ..initialize(firstStore)
          ..initialize(secondStore);
        knownHostStatesController.add(<DovahLinkKnownHostState>[firstState]);
        await pumpEventQueue();

        expect(knownHostListenerCount, 2);
        expect(candidateListenerCount, 2);
        expect(firstStore.state.connection.knownHosts, <KnownHost>[
          HostMapper.fromSdkKnownHostState(firstState),
        ]);
        expect(secondStore.state.connection.knownHosts, <KnownHost>[
          HostMapper.fromSdkKnownHostState(firstState),
        ]);

        await middleware.shutdown();
        knownHostStatesController.add(<DovahLinkKnownHostState>[
          Fixtures.buildSdkKnownHostState(host: second),
        ]);
        await pumpEventQueue();

        expect(knownHostCancellationCount, 2);
        expect(candidateCancellationCount, 2);
        expect(firstStore.state.connection.knownHosts, <KnownHost>[
          HostMapper.fromSdkKnownHostState(firstState),
        ]);
        expect(secondStore.state.connection.knownHosts, <KnownHost>[
          HostMapper.fromSdkKnownHostState(firstState),
        ]);
      },
    );

    test('shutdown cancels observation and ignores later SDK values', () async {
      final List<Object?> actions = <Object?>[];
      final Store<AppState> integrationStore = const CreateStore()(
        middleware: [
          (Store<AppState> _, dynamic action, NextDispatcher next) {
            actions.add(action);
            next(action);
          },
          middleware.call,
        ],
      );
      final DovahLinkHost first = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'HOST-A',
        endpoint: defaultHostUri,
      );
      final DovahLinkHost second = DovahLinkHost(
        hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
        hostName: 'HOST-B',
        endpoint: Uri.parse('ws://127.0.0.1:58232/'),
      );
      final DovahLinkKnownHostState firstState =
          Fixtures.buildSdkKnownHostState(host: first);
      final DovahLinkHost firstCandidate = DovahLinkHost(
        hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        hostName: 'CANDIDATE-A',
        endpoint: defaultHostUri,
      );
      final DovahLinkHost lateCandidate = DovahLinkHost(
        hostId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        hostName: 'CANDIDATE-B',
        endpoint: defaultHostUri,
      );
      middleware.initialize(integrationStore);
      knownHostStatesController.add(<DovahLinkKnownHostState>[firstState]);
      candidateHostsController.add(<DovahLinkHost>[firstCandidate]);
      await pumpEventQueue();
      actions.clear();

      await middleware.shutdown();
      knownHostStatesController.add(<DovahLinkKnownHostState>[
        Fixtures.buildSdkKnownHostState(host: second),
      ]);
      candidateHostsController.add(<DovahLinkHost>[lateCandidate]);
      await pumpEventQueue();

      expect(knownHostCancellationCount, 1);
      expect(candidateCancellationCount, 1);
      expect(integrationStore.state.connection.knownHosts, <KnownHost>[
        HostMapper.fromSdkKnownHostState(firstState),
      ]);
      expect(integrationStore.state.connection.hosts, <Host>[
        HostMapper.fromSdk(firstCandidate),
      ]);
      expect(actions.whereType<ConnectionCandidatesChangedAction>(), isEmpty);
    });

    test('initialize stays inert after shutdown', () async {
      final Store<AppState> integrationStore = const CreateStore()(
        middleware: [middleware.call],
      );
      await middleware.shutdown();

      middleware.initialize(integrationStore);

      expect(knownHostListenerCount, 0);
      expect(candidateListenerCount, 0);
    });

    test(
      'initialize subscribes without resolving a storage implementation',
      () async {
        final Store<AppState> integrationStore = const CreateStore()(
          middleware: [middleware.call],
        );

        middleware.initialize(integrationStore);

        expect(knownHostListenerCount, 1);
        expect(candidateListenerCount, 1);
      },
    );

    test(
      'stream error before the first snapshot marks the empty projection failed',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        final List<Object?> actions = <Object?>[];
        final Store<AppState> integrationStore = const CreateStore()(
          middleware: [
            (Store<AppState> _, dynamic action, NextDispatcher next) {
              actions.add(action);
              next(action);
            },
            middleware.call,
          ],
        );
        final DovahLinkHost host = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'KNOWN-HOST',
          endpoint: defaultHostUri,
        );
        middleware.initialize(integrationStore);

        knownHostStatesController.addError(
          StateError('initial storage read failed'),
        );
        await pumpEventQueue();

        expect(
          integrationStore.state.connection.knownHostsStatus,
          KnownHostsObservationStatus.failed,
        );
        expect(integrationStore.state.connection.knownHosts, isEmpty);
        expect(
          actions.whereType<ConnectionKnownHostsObservationFailedAction>(),
          [const ConnectionKnownHostsObservationFailedAction()],
        );
        expect(reported, hasLength(1));

        final DovahLinkKnownHostState hostState =
            Fixtures.buildSdkKnownHostState(host: host);
        knownHostStatesController.add(<DovahLinkKnownHostState>[hostState]);
        await pumpEventQueue();

        expect(
          integrationStore.state.connection.knownHostsStatus,
          KnownHostsObservationStatus.ready,
        );
        expect(integrationStore.state.connection.knownHosts, <KnownHost>[
          HostMapper.fromSdkKnownHostState(hostState),
        ]);
      },
    );

    test('stream errors are reported and observation continues', () async {
      final originalHandler = FlutterError.onError;
      final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
      FlutterError.onError = reported.add;
      addTearDown(() => FlutterError.onError = originalHandler);
      final List<Object?> actions = <Object?>[];
      final Store<AppState> integrationStore = const CreateStore()(
        middleware: [
          (Store<AppState> _, dynamic action, NextDispatcher next) {
            actions.add(action);
            next(action);
          },
          middleware.call,
        ],
      );
      final DovahLinkHost previous = DovahLinkHost(
        hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
        hostName: 'PREVIOUS-HOST',
        endpoint: Uri.parse('ws://127.0.0.1:58232/'),
      );
      final DovahLinkHost host = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'KNOWN-HOST',
        endpoint: defaultHostUri,
      );
      middleware.initialize(integrationStore);

      final DovahLinkKnownHostState previousState =
          Fixtures.buildSdkKnownHostState(
            host: previous,
            availability: DovahLinkHostAvailability.online,
          );
      knownHostStatesController.add(<DovahLinkKnownHostState>[previousState]);
      await pumpEventQueue();
      knownHostStatesController.addError(StateError('storage read failed'));
      await pumpEventQueue();

      expect(actions.whereType<ConnectionKnownHostsObservationFailedAction>(), [
        const ConnectionKnownHostsObservationFailedAction(),
      ]);
      expect(
        integrationStore.state.connection.knownHostsStatus,
        KnownHostsObservationStatus.failed,
      );
      expect(integrationStore.state.connection.knownHosts, <KnownHost>[
        HostMapper.fromSdkKnownHostState(previousState),
      ]);

      final DovahLinkKnownHostState hostState = Fixtures.buildSdkKnownHostState(
        host: host,
        availability: DovahLinkHostAvailability.offline,
      );
      knownHostStatesController.add(<DovahLinkKnownHostState>[hostState]);
      await pumpEventQueue();

      expect(reported, hasLength(1));
      expect(reported.single.exception, isA<StateError>());
      expect(knownHostListenerCount, 1);
      expect(integrationStore.state.connection.knownHosts, <KnownHost>[
        HostMapper.fromSdkKnownHostState(hostState),
      ]);
      expect(
        integrationStore.state.connection.knownHostsStatus,
        KnownHostsObservationStatus.ready,
      );
    });
  });

  group('ConnectionMiddleware candidate observation behaves correctly', () {
    test(
      'initialize mirrors the SDK candidate collection into Redux',
      () async {
        final Store<AppState> integrationStore = const CreateStore()(
          middleware: [middleware.call],
        );
        final DovahLinkHost candidate = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'CANDIDATE',
          endpoint: defaultHostUri,
        );
        middleware.initialize(integrationStore);
        candidateHostsController.add(<DovahLinkHost>[candidate]);
        await pumpEventQueue();

        expect(candidateListenerCount, 1);
        expect(integrationStore.state.connection.hosts, <Host>[
          HostMapper.fromSdk(candidate),
        ]);
      },
    );

    test(
      'candidate stream errors surface and later candidate state is still observed',
      () async {
        final originalHandler = FlutterError.onError;
        final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
        FlutterError.onError = reported.add;
        addTearDown(() => FlutterError.onError = originalHandler);
        final Store<AppState> integrationStore = const CreateStore()(
          middleware: [middleware.call],
        );
        middleware.initialize(integrationStore);

        candidateHostsController.addError(StateError('candidate read failed'));
        await pumpEventQueue();
        expect(
          integrationStore.state.connection.discoveryStatus,
          ConnectionDiscoveryStatus.failed,
        );
        expect(reported, hasLength(1));

        final DovahLinkHost candidate = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'CANDIDATE',
          endpoint: defaultHostUri,
        );
        candidateHostsController.add(<DovahLinkHost>[candidate]);
        await pumpEventQueue();

        expect(integrationStore.state.connection.hosts, <Host>[
          HostMapper.fromSdk(candidate),
        ]);
      },
    );
  });

  group(
    'ConnectionMiddleware processes ConnectionDiscoveryRequestedAction correctly',
    () {
      test(
        'ConnectionDiscoveryRequestedAction reports candidate presence after the request',
        () async {
          final HelloResult reportedHello = Fixtures.buildSdkHelloResult(
            trustState: DovahLinkTrustState.unpaired,
          );
          final List<Object?> actions = [];
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();
          when(() => mockPairing.discoverHosts()).thenAnswer((_) async {
            expect(actions, [action, const ConnectionDiscoveryStartedAction()]);
            return <DovahLinkHost>[
              DovahLinkHost(
                hostId: reportedHello.hostId,
                hostName: reportedHello.hostName,
                endpoint: defaultHostUri,
              ),
            ];
          });
          final Completer<void> resultDispatched = Completer<void>();
          when(() => store.dispatch(any())).thenAnswer((invocation) {
            final Object? dispatched = invocation.positionalArguments.single;
            actions.add(dispatched);
            if (dispatched is ConnectionDiscoverySucceededAction ||
                dispatched is ConnectionDiscoveryFailedAction) {
              resultDispatched.complete();
            }
          });
          middleware.call(store, action, actions.add);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions, [
            action,
            const ConnectionDiscoveryStartedAction(),
            const ConnectionDiscoverySucceededAction(hasCandidates: true),
          ]);
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction reports empty results when no Host responds',
        () async {
          when(
            () => mockPairing.discoverHosts(),
          ).thenAnswer((_) async => const <DovahLinkHost>[]);
          final List<Object?> actions = [];
          final Completer<void> resultDispatched = Completer<void>();
          when(() => store.dispatch(any())).thenAnswer((invocation) {
            final Object? dispatched = invocation.positionalArguments.single;
            actions.add(dispatched);
            if (dispatched is ConnectionDiscoverySucceededAction ||
                dispatched is ConnectionDiscoveryFailedAction) {
              resultDispatched.complete();
            }
          });
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions, [
            action,
            const ConnectionDiscoveryStartedAction(),
            const ConnectionDiscoverySucceededAction(hasCandidates: false),
          ]);
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction maps connection failure to hostUnavailable',
        () async {
          const DovahLinkConnectionException exception =
              DovahLinkConnectionException('diagnostic');
          when(() => mockPairing.discoverHosts()).thenThrow(exception);
          final List<Object?> actions = [];
          final Completer<void> resultDispatched = Completer<void>();
          when(() => store.dispatch(any())).thenAnswer((invocation) {
            final Object? dispatched = invocation.positionalArguments.single;
            actions.add(dispatched);
            if (dispatched is ConnectionDiscoverySucceededAction ||
                dispatched is ConnectionDiscoveryFailedAction) {
              resultDispatched.complete();
            }
          });
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions, [
            action,
            const ConnectionDiscoveryStartedAction(),
            const ConnectionDiscoveryFailedAction(
              ConnectionFailureReason.hostUnavailable,
            ),
          ]);
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction maps protocol failure to invalidResponse',
        () async {
          const DovahLinkProtocolException exception =
              DovahLinkProtocolException(
                code: ProtocolErrorCode.malformedMessage,
                message: 'diagnostic',
                retryable: false,
              );
          when(() => mockPairing.discoverHosts()).thenThrow(exception);
          final List<Object?> actions = [];
          final Completer<void> resultDispatched = Completer<void>();
          when(() => store.dispatch(any())).thenAnswer((invocation) {
            final Object? dispatched = invocation.positionalArguments.single;
            actions.add(dispatched);
            if (dispatched is ConnectionDiscoverySucceededAction ||
                dispatched is ConnectionDiscoveryFailedAction) {
              resultDispatched.complete();
            }
          });
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions, [
            action,
            const ConnectionDiscoveryStartedAction(),
            const ConnectionDiscoveryFailedAction(
              ConnectionFailureReason.invalidResponse,
            ),
          ]);
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction maps compatibility failure to incompatibleHost',
        () async {
          const DovahLinkCompatibilityException exception =
              DovahLinkCompatibilityException(
                hostVersion: 'unsupported',
                supportedHostVersionRange: 'supported',
                failure: HostVersionCompatibilityFailure.hostTooNew,
              );
          when(() => mockPairing.discoverHosts()).thenThrow(exception);
          final List<Object?> actions = [];
          final Completer<void> resultDispatched = Completer<void>();
          when(() => store.dispatch(any())).thenAnswer((invocation) {
            final Object? dispatched = invocation.positionalArguments.single;
            actions.add(dispatched);
            if (dispatched is ConnectionDiscoverySucceededAction ||
                dispatched is ConnectionDiscoveryFailedAction) {
              resultDispatched.complete();
            }
          });
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions, [
            action,
            const ConnectionDiscoveryStartedAction(),
            const ConnectionDiscoveryFailedAction(
              ConnectionFailureReason.incompatibleHost,
            ),
          ]);
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction maps unexpected errors to unknown',
        () async {
          final StateError error = StateError('diagnostic');
          when(() => mockPairing.discoverHosts()).thenThrow(error);
          final List<Object?> actions = [];
          final Completer<void> resultDispatched = Completer<void>();
          when(() => store.dispatch(any())).thenAnswer((invocation) {
            final Object? dispatched = invocation.positionalArguments.single;
            actions.add(dispatched);
            if (dispatched is ConnectionDiscoverySucceededAction ||
                dispatched is ConnectionDiscoveryFailedAction) {
              resultDispatched.complete();
            }
          });
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions, [
            action,
            const ConnectionDiscoveryStartedAction(),
            const ConnectionDiscoveryFailedAction(
              ConnectionFailureReason.unknown,
            ),
          ]);
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction does not start when discovery is already running',
        () {
          when(() => store.state).thenReturn(
            AppState(
              connection: const ConnectionState(
                discoveryStatus: ConnectionDiscoveryStatus.discovering,
              ),
              pairing: PairingState.initial(),
            ),
          );
          final List<Object?> actions = [];
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);

          expect(actions, [action]);
          verifyNever(() => mockPairing.discoverHosts());
          verifyNever(() => store.dispatch(any()));
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction does not start after shutdown',
        () async {
          await middleware.shutdown();
          final List<Object?> actions = [];
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);

          expect(actions, [action]);
          verifyNever(() => mockPairing.discoverHosts());
          verifyNever(() => store.dispatch(any()));
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction suppresses late success after shutdown',
        () async {
          final Completer<List<DovahLinkHost>> discovery =
              Completer<List<DovahLinkHost>>();
          when(
            () => mockPairing.discoverHosts(),
          ).thenAnswer((_) => discovery.future);
          final List<Object?> actions = [];
          when(() => store.dispatch(any())).thenAnswer((invocation) {
            actions.add(invocation.positionalArguments.single);
          });
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);
          await middleware.shutdown();
          discovery.complete(<DovahLinkHost>[
            DovahLinkHost(
              hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
              hostName: 'LATE-HOST',
              endpoint: defaultHostUri,
            ),
          ]);
          await pumpEventQueue();

          expect(actions, [action, const ConnectionDiscoveryStartedAction()]);
          expect(
            actions.whereType<ConnectionDiscoverySucceededAction>(),
            isEmpty,
          );
          expect(actions.whereType<ConnectionDiscoveryFailedAction>(), isEmpty);
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction suppresses late failure after shutdown',
        () async {
          final Completer<List<DovahLinkHost>> discovery =
              Completer<List<DovahLinkHost>>();
          when(
            () => mockPairing.discoverHosts(),
          ).thenAnswer((_) => discovery.future);
          final List<Object?> actions = [];
          when(() => store.dispatch(any())).thenAnswer((invocation) {
            actions.add(invocation.positionalArguments.single);
          });
          const ConnectionDiscoveryRequestedAction action =
              ConnectionDiscoveryRequestedAction();

          middleware.call(store, action, actions.add);
          await middleware.shutdown();
          discovery.completeError(StateError('late discovery failure'));
          await pumpEventQueue();

          expect(actions, [action, const ConnectionDiscoveryStartedAction()]);
          expect(
            actions.whereType<ConnectionDiscoverySucceededAction>(),
            isEmpty,
          );
          expect(actions.whereType<ConnectionDiscoveryFailedAction>(), isEmpty);
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction allows only one pending SDK operation',
        () async {
          final Completer<List<DovahLinkHost>> discovery =
              Completer<List<DovahLinkHost>>();
          when(
            () => mockPairing.discoverHosts(),
          ).thenAnswer((_) => discovery.future);
          final List<Object?> actions = [];
          Completer<void> resultDispatched = Completer<void>();
          void recordActions(
            Store<AppState> store,
            dynamic action,
            NextDispatcher next,
          ) {
            actions.add(action);
            next(action);
            if (action is ConnectionDiscoverySucceededAction ||
                action is ConnectionDiscoveryFailedAction) {
              resultDispatched.complete();
            }
          }

          final Store<AppState> integrationStore = const CreateStore()(
            middleware: [recordActions, middleware.call],
          );
          middleware.initialize(integrationStore);
          const ConnectionDiscoveryRequestedAction request =
              ConnectionDiscoveryRequestedAction();

          integrationStore.dispatch(request);
          expect(
            integrationStore.state.connection.discoveryStatus,
            ConnectionDiscoveryStatus.discovering,
          );
          expect(actions, [request, const ConnectionDiscoveryStartedAction()]);

          integrationStore.dispatch(request);
          expect(
            integrationStore.state.connection.discoveryStatus,
            ConnectionDiscoveryStatus.discovering,
          );
          expect(actions, [
            request,
            const ConnectionDiscoveryStartedAction(),
            request,
          ]);
          verify(() => mockPairing.discoverHosts()).called(1);

          final DovahLinkHost latestCandidate = DovahLinkHost(
            hostId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
            hostName: 'NEWER-SDK-SNAPSHOT',
            endpoint: defaultHostUri,
          );
          candidateHostsController.add(<DovahLinkHost>[latestCandidate]);
          await pumpEventQueue();
          expect(integrationStore.state.connection.hosts, <Host>[
            HostMapper.fromSdk(latestCandidate),
          ]);

          discovery.complete(<DovahLinkHost>[
            DovahLinkHost(
              hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
              hostName: 'SKYRIM-PC',
              endpoint: defaultHostUri,
            ),
          ]);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions, [
            request,
            const ConnectionDiscoveryStartedAction(),
            request,
            ConnectionCandidatesChangedAction(<Host>[
              HostMapper.fromSdk(latestCandidate),
            ]),
            const ConnectionDiscoverySucceededAction(hasCandidates: true),
          ]);
          expect(integrationStore.state.connection.hosts, <Host>[
            HostMapper.fromSdk(latestCandidate),
          ]);
          expect(
            integrationStore.state.connection.discoveryStatus,
            ConnectionDiscoveryStatus.available,
          );

          when(
            () => mockPairing.discoverHosts(),
          ).thenAnswer((_) async => const <DovahLinkHost>[]);
          resultDispatched = Completer<void>();
          integrationStore.dispatch(request);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions.skip(5), [
            request,
            const ConnectionDiscoveryStartedAction(),
            const ConnectionDiscoverySucceededAction(hasCandidates: false),
          ]);
          expect(integrationStore.state.connection.hosts, <Host>[
            HostMapper.fromSdk(latestCandidate),
          ]);
          expect(
            integrationStore.state.connection.discoveryStatus,
            ConnectionDiscoveryStatus.empty,
          );
          verify(() => mockPairing.discoverHosts()).called(1);
        },
      );
    },
  );

  group(
    'ConnectionMiddleware processes ConnectionHostReentryRequestedAction correctly',
    () {
      test(
        'ConnectionHostReentryRequestedAction does not start another discovery',
        () {
          final List<Object?> actions = [];
          const ConnectionHostReentryRequestedAction action =
              ConnectionHostReentryRequestedAction('connected-host');

          middleware.call(store, action, actions.add);

          expect(actions, [action]);
          verifyNever(() => mockPairing.discoverHosts());
          verifyNever(() => store.dispatch(any()));
        },
      );
    },
  );
}
