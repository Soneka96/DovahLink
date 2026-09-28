import 'dart:async';

import 'package:flutter/foundation.dart' show FlutterError, FlutterErrorDetails;

import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
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
        DovahLinkProtocolException,
        DovahLinkTrustState,
        HelloResult,
        HostVersionCompatibilityFailure,
        IDovahLinkDiscoveryService,
        ProtocolErrorCode;

/// Mocks SDK local discovery for [ConnectionMiddleware] tests.
class MockDovahLinkDiscoveryService extends Mock
    implements IDovahLinkDiscoveryService {}

/// Mocks the SDK client that owns Known Host state.
class MockDovahLinkClient extends Mock implements DovahLinkClient {}

/// Mocks Redux dispatch for [ConnectionMiddleware] tests.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises discovery action orchestration by [ConnectionMiddleware].
void main() {
  late MockDovahLinkDiscoveryService mockDiscoveryService;
  late MockDovahLinkClient mockClient;
  late StreamController<List<DovahLinkHost>> knownHostsController;
  late Stream<List<DovahLinkHost>> knownHostsChanges;
  late int knownHostListenerCount;
  late int knownHostCancellationCount;
  late MockStore store;
  late ConnectionMiddleware middleware;

  setUp(() async {
    await sl.reset();
    mockDiscoveryService = MockDovahLinkDiscoveryService();
    mockClient = MockDovahLinkClient();
    knownHostsController = StreamController<List<DovahLinkHost>>.broadcast();
    knownHostListenerCount = 0;
    knownHostCancellationCount = 0;
    knownHostsChanges = Stream<List<DovahLinkHost>>.multi((sink) {
      knownHostListenerCount++;
      final StreamSubscription<List<DovahLinkHost>> subscription =
          knownHostsController.stream.listen(sink.add, onError: sink.addError);
      sink.onCancel = () async {
        knownHostCancellationCount++;
        await subscription.cancel();
      };
    }, isBroadcast: true);
    when(
      () => mockClient.knownHostsChanges,
    ).thenAnswer((_) => knownHostsChanges);
    store = MockStore();
    when(() => store.state).thenReturn(AppState.initial());
    middleware = ConnectionMiddleware();
    sl.registerSingleton<IDovahLinkDiscoveryService>(mockDiscoveryService);
    sl.registerSingleton<DovahLinkClient>(mockClient);
  });

  tearDown(() async {
    await middleware.shutdown();
    await knownHostsController.close();
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
      knownHostsController.add(const <DovahLinkHost>[]);
      await pumpEventQueue();

      expect(knownHostListenerCount, 1);
      expect(actions.whereType<ConnectionKnownHostsChangedAction>(), [
        ConnectionKnownHostsChangedAction(const <Host>[]),
      ]);
      expect(integrationStore.state.connection.knownHosts, isEmpty);
    });

    test(
      'initialize maps SDK Hosts and replaces the Redux projection',
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

        middleware.initialize(integrationStore);
        middleware.initialize(integrationStore);
        knownHostsController.add(<DovahLinkHost>[first]);
        await pumpEventQueue();
        expect(integrationStore.state.connection.knownHosts, <Host>[
          HostMapper.fromSdk(first),
        ]);
        knownHostsController.add(<DovahLinkHost>[first, second]);
        await pumpEventQueue();

        expect(knownHostListenerCount, 1);
        expect(integrationStore.state.connection.knownHosts, <Host>[
          HostMapper.fromSdk(first),
          HostMapper.fromSdk(second),
        ]);

        knownHostsController.add(<DovahLinkHost>[second]);
        await pumpEventQueue();

        expect(integrationStore.state.connection.knownHosts, <Host>[
          HostMapper.fromSdk(second),
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
        middleware
          ..initialize(firstStore)
          ..initialize(secondStore);
        knownHostsController.add(<DovahLinkHost>[first]);
        await pumpEventQueue();

        expect(knownHostListenerCount, 2);
        expect(firstStore.state.connection.knownHosts, <Host>[
          HostMapper.fromSdk(first),
        ]);
        expect(secondStore.state.connection.knownHosts, <Host>[
          HostMapper.fromSdk(first),
        ]);

        await middleware.shutdown();
        knownHostsController.add(<DovahLinkHost>[second]);
        await pumpEventQueue();

        expect(knownHostCancellationCount, 2);
        expect(firstStore.state.connection.knownHosts, <Host>[
          HostMapper.fromSdk(first),
        ]);
        expect(secondStore.state.connection.knownHosts, <Host>[
          HostMapper.fromSdk(first),
        ]);
      },
    );

    test('shutdown cancels observation and ignores later SDK values', () async {
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
      middleware.initialize(integrationStore);
      knownHostsController.add(<DovahLinkHost>[first]);
      await pumpEventQueue();

      await middleware.shutdown();
      knownHostsController.add(<DovahLinkHost>[second]);
      await pumpEventQueue();

      expect(knownHostCancellationCount, 1);
      expect(integrationStore.state.connection.knownHosts, <Host>[
        HostMapper.fromSdk(first),
      ]);
    });

    test('initialize stays inert after shutdown', () async {
      final Store<AppState> integrationStore = const CreateStore()(
        middleware: [middleware.call],
      );
      await middleware.shutdown();

      middleware.initialize(integrationStore);

      expect(knownHostListenerCount, 0);
    });

    test(
      'initialize subscribes without resolving a storage implementation',
      () async {
        final Store<AppState> integrationStore = const CreateStore()(
          middleware: [middleware.call],
        );

        middleware.initialize(integrationStore);

        expect(knownHostListenerCount, 1);
      },
    );

    test('stream errors are reported and observation continues', () async {
      final originalHandler = FlutterError.onError;
      final List<FlutterErrorDetails> reported = <FlutterErrorDetails>[];
      FlutterError.onError = reported.add;
      addTearDown(() => FlutterError.onError = originalHandler);
      final Store<AppState> integrationStore = const CreateStore()(
        middleware: [middleware.call],
      );
      final DovahLinkHost host = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'KNOWN-HOST',
        endpoint: defaultHostUri,
      );
      middleware.initialize(integrationStore);

      knownHostsController.addError(StateError('storage read failed'));
      await pumpEventQueue();
      knownHostsController.add(<DovahLinkHost>[host]);
      await pumpEventQueue();

      expect(reported, hasLength(1));
      expect(reported.single.exception, isA<StateError>());
      expect(knownHostListenerCount, 1);
      expect(integrationStore.state.connection.knownHosts, <Host>[
        HostMapper.fromSdk(host),
      ]);
    });
  });

  group(
    'ConnectionMiddleware processes ConnectionDiscoveryRequestedAction correctly',
    () {
      test(
        'ConnectionDiscoveryRequestedAction maps the discovered Host after the request',
        () async {
          final HelloResult reportedHello = Fixtures.buildSdkHelloResult(
            trustState: DovahLinkTrustState.unpaired,
          );
          when(() => mockDiscoveryService.discover()).thenAnswer(
            (_) async => <DovahLinkHost>[
              DovahLinkHost(
                hostId: reportedHello.hostId,
                hostName: reportedHello.hostName,
                endpoint: defaultHostUri,
              ),
            ],
          );
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
            ConnectionDiscoverySucceededAction([
              Fixtures.buildHost(
                hostId: reportedHello.hostId,
                displayName: reportedHello.hostName,
              ),
            ]),
          ]);
          verify(() => mockDiscoveryService.discover()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction dispatches an empty list when no Host responds',
        () async {
          when(
            () => mockDiscoveryService.discover(),
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
            const ConnectionDiscoverySucceededAction(<Host>[]),
          ]);
          verify(() => mockDiscoveryService.discover()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction maps connection failure to hostUnavailable',
        () async {
          const DovahLinkConnectionException exception =
              DovahLinkConnectionException('diagnostic');
          when(() => mockDiscoveryService.discover()).thenThrow(exception);
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
          verify(() => mockDiscoveryService.discover()).called(1);
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
          when(() => mockDiscoveryService.discover()).thenThrow(exception);
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
          verify(() => mockDiscoveryService.discover()).called(1);
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
          when(() => mockDiscoveryService.discover()).thenThrow(exception);
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
          verify(() => mockDiscoveryService.discover()).called(1);
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction maps unexpected errors to unknown',
        () async {
          final StateError error = StateError('diagnostic');
          when(() => mockDiscoveryService.discover()).thenThrow(error);
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
          verify(() => mockDiscoveryService.discover()).called(1);
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
          verifyNever(() => mockDiscoveryService.discover());
          verifyNever(() => store.dispatch(any()));
        },
      );

      test(
        'ConnectionDiscoveryRequestedAction allows only one pending SDK operation',
        () async {
          final Completer<List<DovahLinkHost>> discovery =
              Completer<List<DovahLinkHost>>();
          when(
            () => mockDiscoveryService.discover(),
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
          verify(() => mockDiscoveryService.discover()).called(1);

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
            ConnectionDiscoverySucceededAction([
              Fixtures.buildHost(displayName: 'SKYRIM-PC'),
            ]),
          ]);
          expect(
            integrationStore.state.connection.discoveryStatus,
            ConnectionDiscoveryStatus.available,
          );

          when(
            () => mockDiscoveryService.discover(),
          ).thenAnswer((_) async => const <DovahLinkHost>[]);
          resultDispatched = Completer<void>();
          integrationStore.dispatch(request);
          await resultDispatched.future.timeout(const Duration(seconds: 1));

          expect(actions.skip(4), [
            request,
            const ConnectionDiscoveryStartedAction(),
            const ConnectionDiscoverySucceededAction(<Host>[]),
          ]);
          expect(
            integrationStore.state.connection.discoveryStatus,
            ConnectionDiscoveryStatus.empty,
          );
          verify(() => mockDiscoveryService.discover()).called(1);
        },
      );
    },
  );

  group('ConnectionMiddleware processes unrelated actions correctly', () {
    test(
      'ConnectionMiddleware forwards an unrelated action without discovering Hosts',
      () {
        final List<Object?> actions = [];
        final Object action = Object();

        middleware.call(store, action, actions.add);

        expect(actions, [action]);
        verifyNever(() => mockDiscoveryService.discover());
        verifyNever(() => store.dispatch(any()));
      },
    );
  });
}
