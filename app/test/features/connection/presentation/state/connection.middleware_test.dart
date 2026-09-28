import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
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

/// Mocks Redux dispatch for [ConnectionMiddleware] tests.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises discovery action orchestration by [ConnectionMiddleware].
void main() {
  late MockDovahLinkDiscoveryService mockDiscoveryService;
  late MockStore store;
  late ConnectionMiddleware middleware;

  setUp(() async {
    await sl.reset();
    mockDiscoveryService = MockDovahLinkDiscoveryService();
    store = MockStore();
    when(() => store.state).thenReturn(AppState.initial());
    middleware = ConnectionMiddleware();
    sl.registerSingleton<IDovahLinkDiscoveryService>(mockDiscoveryService);
  });

  tearDown(() async {
    await sl.reset();
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
