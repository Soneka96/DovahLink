import 'dart:async';
import 'dart:io' show HttpStatus, WebSocketException;

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_invalidation.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/session/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/session/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// Mock transport used to isolate [SessionService]'s lifecycle behavior.
class MockDovahLinkTransport extends Mock implements IDovahLinkTransport {}

/// Mock session state used per `ai/context/sdk/testing.md`'s "Service test boundaries" -- its own
/// state-derivation behavior (for example preserving `reconnecting` through a failed recovery
/// attempt) is `session_state_test.dart`'s responsibility; this file only proves
/// [SessionService] calls the right transition method with the right arguments.
class MockSessionState extends Mock implements SessionState {}

/// Mock teardown coordinator used per `ai/context/sdk/testing.md`'s "Service test boundaries" --
/// its own generation-check dedup logic is `connection_teardown_coordinator_test.dart`'s
/// responsibility; this file only proves [SessionService] calls it with the right arguments
/// for each reactive signal.
class MockConnectionTeardownCoordinator extends Mock
    implements ConnectionTeardownCoordinator {}

/// Builds the Host context used by session-service tests.
DovahLinkHost _currentHost() => DovahLinkHost(
  hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
  hostName: 'LOCAL-HOST',
  endpoint: Uri.parse('ws://127.0.0.1:58231/'),
);

/// Fake stream subscription used to register a mocktail fallback for `any()`.
class FakeStreamSubscription extends Fake
    implements StreamSubscription<String> {}

/// Runs [SessionService] behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockSessionState state;
  late LifecycleOperationQueue lifecycleQueue;
  late MockConnectionTeardownCoordinator teardownCoordinator;
  late SessionService service;
  late StreamController<String> messages;
  late List<Exception> teardownReasons;
  late List<bool> teardownOrphanFlags;
  late List<Uri> reconnectUris;
  late List<DovahLinkHostId?> reconnectHostIds;
  late List<String> incomingMessages;
  late DovahLinkConnectionState connectionStateValue;
  late DovahLinkHost? currentHostValue;
  late int connectionGenerationValue;
  late String? sessionIdValue;
  late DovahLinkTrustState? trustStateValue;
  late bool isAdministrativelyInvalidatedValue;
  late Uri? lastConnectedUriValue;
  late Future<void>? pendingTeardownGate;
  late void Function()? afterTeardown;
  late List<DovahLinkConnectionState> observedTeardownStates;

  setUpAll(() {
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
    registerFallbackValue(Uri.parse('ws://127.0.0.1:0/'));
    registerFallbackValue(
      DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
    );
    registerFallbackValue(FakeStreamSubscription());
    registerFallbackValue(AdministrativeInvalidationReason.revoked);
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    state = MockSessionState();
    // The real queue is used: scheduling is deterministic, and the coordinator's and service's
    // own ordering behavior under it is exactly what these tests exercise.
    lifecycleQueue = LifecycleOperationQueue();
    teardownCoordinator = MockConnectionTeardownCoordinator();
    when(
      () => teardownCoordinator.tearDown(
        any(),
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        canRecover: any(named: 'canRecover'),
      ),
    ).thenAnswer((Invocation invocation) async {
      final Future<void>? gate = pendingTeardownGate;
      if (gate != null) {
        await gate;
      }
      // Mirrors the real coordinator's reset point: recovery is entered directly, from a
      // non-recovering state, only for an eligible ordinary loss.
      final bool Function()? canRecover =
          invocation.namedArguments[#canRecover] as bool Function()?;
      final bool orphan =
          invocation.namedArguments[#orphanRetrySafeOperations] as bool? ??
          true;
      if (isAdministrativelyInvalidatedValue) {
        return false;
      }
      final bool wasRecovering =
          connectionStateValue == DovahLinkConnectionState.reauthenticating ||
          connectionStateValue == DovahLinkConnectionState.reconnecting;
      if (orphan && wasRecovering) {
        if (connectionStateValue != DovahLinkConnectionState.reconnecting) {
          connectionStateValue = DovahLinkConnectionState.reconnecting;
          observedTeardownStates.add(connectionStateValue);
        }
        return false;
      }
      if (orphan && (canRecover?.call() ?? false)) {
        connectionStateValue = DovahLinkConnectionState.reconnecting;
        observedTeardownStates.add(connectionStateValue);
        afterTeardown?.call();
        return true;
      }
      if (connectionStateValue != DovahLinkConnectionState.disconnected) {
        connectionStateValue = DovahLinkConnectionState.disconnected;
        observedTeardownStates.add(connectionStateValue);
      }
      return false;
    });
    when(
      () => teardownCoordinator.closeAfterInvalidation(any()),
    ).thenAnswer((_) async {});
    messages = StreamController<String>.broadcast();
    teardownReasons = <Exception>[];
    teardownOrphanFlags = <bool>[];
    reconnectUris = <Uri>[];
    reconnectHostIds = <DovahLinkHostId?>[];
    incomingMessages = <String>[];
    connectionStateValue = DovahLinkConnectionState.disconnected;
    currentHostValue = null;
    connectionGenerationValue = 0;
    sessionIdValue = null;
    trustStateValue = null;
    isAdministrativelyInvalidatedValue = false;
    lastConnectedUriValue = null;
    pendingTeardownGate = null;
    afterTeardown = null;
    observedTeardownStates = <DovahLinkConnectionState>[];
    when(() => transport.messages).thenAnswer((_) => messages.stream);
    when(() => transport.connect(any())).thenAnswer((_) async {});
    when(() => transport.close()).thenAnswer((_) async {});
    when(() => state.connectionState).thenAnswer((_) => connectionStateValue);
    when(
      () => state.connectionGeneration,
    ).thenAnswer((_) => connectionGenerationValue);
    when(() => state.sessionId).thenAnswer((_) => sessionIdValue);
    when(() => state.trustState).thenAnswer((_) => trustStateValue);
    when(() => state.knownHostId).thenReturn(null);
    when(() => state.currentHost).thenAnswer((_) => currentHostValue);
    when(
      () => state.isAdministrativelyInvalidated,
    ).thenAnswer((_) => isAdministrativelyInvalidatedValue);
    when(() => state.lastConnectedUri).thenAnswer((_) => lastConnectedUriValue);
    when(() => state.beginConnectAttempt(any())).thenAnswer((
      Invocation invocation,
    ) {
      lastConnectedUriValue = invocation.positionalArguments[0] as Uri;
    });
    when(
      () => state.beginConnectAttempt(
        any(),
        knownHostId: any(named: 'knownHostId'),
      ),
    ).thenAnswer((Invocation invocation) {
      lastConnectedUriValue = invocation.positionalArguments[0] as Uri;
    });
    when(() => state.markConnected()).thenAnswer((_) {
      connectionStateValue = DovahLinkConnectionState.connected;
    });
    when(() => state.markConnectFailed()).thenAnswer((_) {
      connectionStateValue = DovahLinkConnectionState.disconnected;
    });
    when(() => state.attachMessageSubscription(any())).thenAnswer((_) {});
    when(() => state.associateKnownHost(any())).thenAnswer((_) {});
    when(() => state.invalidate(any())).thenAnswer((_) {});
    service = SessionService(
      transport: transport,
      state: state,
      lifecycleQueue: lifecycleQueue,
      teardownCoordinator: teardownCoordinator,
    );
    service.onTeardown =
        (Exception reason, {required bool orphanRetrySafeOperations}) {
          teardownReasons.add(reason);
          teardownOrphanFlags.add(orphanRetrySafeOperations);
        };
    service.onOrdinaryTransportLoss =
        (Uri uri, [DovahLinkHostId? knownHostId]) {
          reconnectUris.add(uri);
          reconnectHostIds.add(knownHostId);
        };
    service.onIncomingMessage = incomingMessages.add;
  });

  tearDown(() async {
    if (!messages.isClosed) {
      await messages.close();
    }
  });

  group('Property connectionStateChanges behaves correctly', () {
    test(
      'Property connectionStateChanges delegates to the underlying SessionState stream',
      () async {
        final StreamController<DovahLinkConnectionState> underlying =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(underlying.close);
        when(
          () => state.connectionStateChanges,
        ).thenAnswer((_) => underlying.stream);

        final Future<void> expectation = expectLater(
          service.connectionStateChanges,
          emits(DovahLinkConnectionState.connected),
        );
        underlying.add(DovahLinkConnectionState.connected);

        await expectation;
      },
    );
  });

  group('Property knownHostSessionChanges behaves correctly', () {
    test('Property knownHostSessionChanges delegates to SessionState', () {
      const Stream<KnownHostSessionSnapshot> changes =
          Stream<KnownHostSessionSnapshot>.empty();
      when(() => state.knownHostSessionChanges).thenAnswer((_) => changes);

      expect(service.knownHostSessionChanges, same(changes));
    });
  });

  group('Property knownHostInvalidations behaves correctly', () {
    test('Property knownHostInvalidations delegates to SessionState', () {
      const Stream<DovahLinkKnownHostInvalidation> changes =
          Stream<DovahLinkKnownHostInvalidation>.empty();
      when(() => state.knownHostInvalidations).thenAnswer((_) => changes);

      expect(service.knownHostInvalidations, same(changes));
    });
  });

  group('Properties currentHost and currentEndpoint behave correctly', () {
    test('Property currentHost delegates to SessionState', () {
      final DovahLinkHost host = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'LOCAL-HOST',
        endpoint: Uri.parse('ws://127.0.0.1:58231/'),
      );
      when(() => state.currentHost).thenReturn(host);

      expect(service.currentHost, host);
    });

    test('Property currentEndpoint delegates to SessionState', () {
      final Uri endpoint = Uri.parse('ws://127.0.0.1:58231/');
      when(() => state.currentEndpoint).thenReturn(endpoint);

      expect(service.currentEndpoint, endpoint);
    });
  });

  group('Property currentKnownHostId behaves correctly', () {
    test('Property currentKnownHostId delegates to SessionState', () {
      final DovahLinkHostId knownHostId = DovahLinkHostId(
        '81869993-955c-4ba3-a7d0-d35ca86078ea',
      );
      when(() => state.knownHostId).thenReturn(knownHostId);

      expect(service.currentKnownHostId, knownHostId);
    });
  });

  group('Method associateKnownHost behaves correctly', () {
    test(
      'Method associateKnownHost delegates for the active matching Host',
      () {
        final DovahLinkHost host = _currentHost();
        final DovahLinkHostId hostId = DovahLinkHostId(host.hostId);
        connectionStateValue = DovahLinkConnectionState.connected;
        currentHostValue = host;

        service.associateKnownHost(hostId);

        verify(() => state.associateKnownHost(hostId)).called(1);
      },
    );

    test('Method associateKnownHost rejects a different active Host ID', () {
      connectionStateValue = DovahLinkConnectionState.connected;
      currentHostValue = _currentHost();

      expect(
        () => service.associateKnownHost(
          DovahLinkHostId('81f6cc90-3a88-40c7-8351-104d4a36c971'),
        ),
        throwsA(isA<DovahLinkConnectionException>()),
      );
      verifyNever(() => state.associateKnownHost(any()));
    });
  });

  group('Method connect behaves correctly', () {
    test(
      'Method connect calls beginConnectAttempt then markConnected and starts receiving on success',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');

        await service.connect(uri);

        verify(() => state.beginConnectAttempt(uri)).called(1);
        verify(() => transport.connect(uri)).called(1);
        verify(() => state.markConnected()).called(1);
        verify(() => transport.messages).called(1);
        verify(() => state.attachMessageSubscription(any())).called(1);
      },
    );

    test(
      'Method connect passes the selected Known Host relationship to session state',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        final DovahLinkHostId hostId = DovahLinkHostId(_currentHost().hostId);

        await service.connect(uri, knownHostId: hostId);

        verify(
          () => state.beginConnectAttempt(uri, knownHostId: hostId),
        ).called(1);
      },
    );

    test('Method connect serializes against another already-in-flight connect() call through the '
        'shared lifecycleQueue', () async {
      final Completer<void> firstConnectCompleter = Completer<void>();
      when(
        () => transport.connect(any()),
      ).thenAnswer((_) => firstConnectCompleter.future);

      final Future<void> first = service.connect(
        Uri.parse('ws://127.0.0.1:58231/'),
      );
      final Future<void> second = service.connect(
        Uri.parse('ws://127.0.0.1:58231/'),
      );
      await pumpEventQueue();

      // The second call's own transport.connect() has not started yet -- it is still queued
      // behind the first.
      verify(() => transport.connect(any())).called(1);
      firstConnectCompleter.complete();
      await first;
      await second;

      // The second call's transport.connect() only ran once the first's queued operation
      // finished.
      verify(() => transport.connect(any())).called(1);
    });

    test(
      'Method connect resets to disconnected and throws DovahLinkConnectionException on failure',
      () async {
        when(
          () => transport.connect(any()),
        ).thenThrow(const DovahLinkConnectionException('refused'));

        await expectLater(
          service.connect(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verify(() => state.markConnectFailed()).called(1);
        verifyNever(() => state.markConnected());
        verifyNever(() => transport.close());
      },
    );

    test(
      'Method connect preserves an HTTP status from a rejected WebSocket upgrade',
      () async {
        when(() => transport.connect(any())).thenThrow(
          const WebSocketException('upgrade rejected', HttpStatus.ok),
        );

        await expectLater(
          service.connect(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException error) => error.httpStatusCode,
              'httpStatusCode',
              HttpStatus.ok,
            ),
          ),
        );
      },
    );

    test(
      'Method connect times out and abandons the transport when connect() never completes',
      () async {
        when(
          () => transport.connect(any()),
        ).thenAnswer((_) => Completer<void>().future);
        final SessionService timeoutService = SessionService(
          transport: transport,
          state: state,
          lifecycleQueue: lifecycleQueue,
          teardownCoordinator: teardownCoordinator,
          connectTimeout: const Duration(milliseconds: 10),
        );

        await expectLater(
          timeoutService.connect(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verify(() => transport.close()).called(1);
        verify(() => state.markConnectFailed()).called(1);
        verifyNever(() => state.markConnected());
        verifyNever(() => transport.messages);
      },
    );

    test(
      'Method connect routes an inbound message to onIncomingMessage',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        messages.add('raw-message');
        await pumpEventQueue();

        expect(incomingMessages, <String>['raw-message']);
      },
    );

    test(
      'Method connect surfaces the transport\'s own rejection when already connected '
      'as DovahLinkConnectionException',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));
        when(() => transport.connect(any())).thenThrow(
          StateError(
            'Already connected. Call close() before connecting again.',
          ),
        );

        await expectLater(
          service.connect(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verify(() => state.markConnectFailed()).called(1);
        verifyNever(() => transport.close());
      },
    );
  });

  group('Method onUnhealthy behaves correctly', () {
    test(
      'Method onUnhealthy tears the connection down, orphaning by default',
      () async {
        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        verify(
          () => teardownCoordinator.tearDown(
            const DovahLinkConnectionException('timed out'),
            canRecover: any(named: 'canRecover'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onUnhealthy resolves directly to reconnecting and notifies onOrdinaryTransportLoss '
      'for an eligible loss, never passing through disconnected',
      () async {
        final DovahLinkHostId knownHostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.connected;
        when(() => state.knownHostId).thenReturn(knownHostId);

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(reconnectUris, <Uri>[Uri.parse('ws://127.0.0.1:58231/')]);
        expect(reconnectHostIds, <DovahLinkHostId?>[knownHostId]);
        expect(connectionStateValue, DovahLinkConnectionState.reconnecting);
        expect(observedTeardownStates, <DovahLinkConnectionState>[
          DovahLinkConnectionState.reconnecting,
        ]);
      },
    );

    test(
      'Method onUnhealthy does not enter recovery or notify onOrdinaryTransportLoss without a '
      'known last-connected endpoint',
      () async {
        lastConnectedUriValue = null;
        connectionStateValue = DovahLinkConnectionState.connected;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        expect(connectionStateValue, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method onUnhealthy does not enter recovery when onOrdinaryTransportLoss is not assigned',
      () async {
        service.onOrdinaryTransportLoss = null;
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.connected;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        expect(connectionStateValue, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method onUnhealthy does not enter recovery when the coordinator reports a racing '
      'administrative invalidation (which owns its own terminal state)',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue =
            DovahLinkConnectionState.administrativelyInvalidated;
        isAdministrativelyInvalidatedValue = true;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        expect(
          connectionStateValue,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
      },
    );

    test(
      'Method onUnhealthy leaves an already-recovering session to its existing cycle instead of '
      'starting a second one',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        for (final DovahLinkConnectionState state in <DovahLinkConnectionState>[
          DovahLinkConnectionState.reauthenticating,
          DovahLinkConnectionState.reconnecting,
        ]) {
          connectionStateValue = state;

          service.onUnhealthy(const DovahLinkConnectionException('timed out'));
          await pumpEventQueue();

          expect(reconnectUris, isEmpty);
          expect(connectionStateValue, DovahLinkConnectionState.reconnecting);
        }
        expect(observedTeardownStates, <DovahLinkConnectionState>[
          DovahLinkConnectionState.reconnecting,
        ]);
      },
    );

    test(
      'Method onUnhealthy acts on a report whose connection generation is still current',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.connected;
        connectionGenerationValue = 4;

        service.onUnhealthy(
          const DovahLinkConnectionException('send failed'),
          connectionGeneration: 4,
        );
        await pumpEventQueue();

        expect(reconnectUris, hasLength(1));
        verify(
          () => teardownCoordinator.tearDown(
            any(),
            canRecover: any(named: 'canRecover'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onUnhealthy ignores a report from an ended connection generation without teardown',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.connected;
        connectionGenerationValue = 5;

        service.onUnhealthy(
          const DovahLinkConnectionException('late send failure'),
          connectionGeneration: 4,
        );
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        verifyNever(
          () => teardownCoordinator.tearDown(
            any(),
            canRecover: any(named: 'canRecover'),
          ),
        );
        expect(connectionStateValue, DovahLinkConnectionState.connected);
      },
    );

    test(
      'Method onUnhealthy ignores a stale-generation report after administrative invalidation',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue =
            DovahLinkConnectionState.administrativelyInvalidated;
        isAdministrativelyInvalidatedValue = true;
        connectionGenerationValue = 6;

        service.onUnhealthy(
          const DovahLinkConnectionException('late send failure'),
          connectionGeneration: 5,
        );
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        verifyNever(
          () => teardownCoordinator.tearDown(
            any(),
            canRecover: any(named: 'canRecover'),
          ),
        );
      },
    );

    test(
      'Method onUnhealthy ignores an old connection\'s report while the new one is '
      'reauthenticating, and still acts on the new one\'s own',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.reauthenticating;
        connectionGenerationValue = 8;

        service.onUnhealthy(
          const DovahLinkConnectionException('old connection'),
          connectionGeneration: 7,
        );
        await pumpEventQueue();
        verifyNever(
          () => teardownCoordinator.tearDown(
            any(),
            canRecover: any(named: 'canRecover'),
          ),
        );

        service.onUnhealthy(
          const DovahLinkConnectionException('new connection'),
          connectionGeneration: 8,
        );
        await pumpEventQueue();
        verify(
          () => teardownCoordinator.tearDown(
            any(),
            canRecover: any(named: 'canRecover'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onUnhealthy without a generation still reports about the current connection',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.connected;
        connectionGenerationValue = 9;

        service.onUnhealthy(const DovahLinkConnectionException('lost'));
        await pumpEventQueue();

        expect(reconnectUris, hasLength(1));
      },
    );

    test(
      'Method onUnhealthy skips recovery when explicit disconnect begins during teardown',
      () async {
        final Completer<void> ordinaryTeardown = Completer<void>();
        pendingTeardownGate = ordinaryTeardown.future;
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.connected;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();
        pendingTeardownGate = null;
        await service.disconnect();
        ordinaryTeardown.complete();
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        expect(connectionStateValue, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method onUnhealthy skips the handoff when administrative invalidation lands after '
      'teardown entered recovery but before the queued recovery step',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.connected;
        afterTeardown = () {
          connectionStateValue =
              DovahLinkConnectionState.administrativelyInvalidated;
          isAdministrativelyInvalidatedValue = true;
        };

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        expect(
          connectionStateValue,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
      },
    );

    test(
      'Method onUnhealthy skips recovery when the client closes during teardown',
      () async {
        final Completer<void> ordinaryTeardown = Completer<void>();
        pendingTeardownGate = ordinaryTeardown.future;
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.connected;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();
        pendingTeardownGate = null;
        await service.close();
        ordinaryTeardown.complete();
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        expect(service.isTerminallyClosed, isTrue);
        expect(connectionStateValue, DovahLinkConnectionState.disconnected);
      },
    );

    test('Method onUnhealthy ignores a stale handoff when disconnect lands between teardown and '
        'the recovery step', () async {
      lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
      connectionStateValue = DovahLinkConnectionState.connected;
      final Completer<void> hold = Completer<void>();
      // Teardown has already entered recovery; hold the queue so the handoff step and a
      // disconnect() are both queued behind it, with disconnect() bumping the handoff first.
      service.onUnhealthy(const DovahLinkConnectionException('timed out'));
      unawaited(lifecycleQueue.run<void>(() => hold.future));
      await pumpEventQueue();
      connectionStateValue = DovahLinkConnectionState.reconnecting;
      await service.disconnect();
      hold.complete();
      await pumpEventQueue();

      expect(reconnectUris, isEmpty);
    });
  });

  group('Method onProtocolViolation behaves correctly', () {
    test(
      'Method onProtocolViolation tears down without orphaning when requested',
      () {
        const DovahLinkProtocolException reason = DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'no match',
          retryable: false,
        );

        service.onProtocolViolation(reason, orphanRetrySafeOperations: false);

        verify(
          () => teardownCoordinator.tearDown(
            reason,
            orphanRetrySafeOperations: false,
          ),
        ).called(1);
      },
    );

    test('Method onProtocolViolation tears down orphaning when requested', () {
      const DovahLinkProtocolException reason = DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'no match',
        retryable: true,
      );

      service.onProtocolViolation(reason, orphanRetrySafeOperations: true);

      verify(
        () => teardownCoordinator.tearDown(
          reason,
          orphanRetrySafeOperations: true,
        ),
      ).called(1);
    });
  });

  group('Method onUnsolicitedError behaves correctly', () {
    test(
      'Method onUnsolicitedError tears down without orphaning, carrying the host-reported '
      'classification',
      () {
        service.onUnsolicitedError(
          const ErrorPayload(
            code: ProtocolErrorCode.rateLimited,
            message: 'Too many requests.',
            retryable: true,
          ),
        );

        final VerificationResult verification = verify(
          () => teardownCoordinator.tearDown(
            captureAny(),
            orphanRetrySafeOperations: false,
          ),
        );
        verification.called(1);
        final DovahLinkProtocolException reason =
            verification.captured.single as DovahLinkProtocolException;
        expect(reason.code, ProtocolErrorCode.rateLimited);
        expect(reason.message, 'Too many requests.');
        expect(reason.retryable, isTrue);
      },
    );

    test(
      'Method onUnsolicitedError preserves a non-retryable classification',
      () {
        service.onUnsolicitedError(
          const ErrorPayload(
            code: ProtocolErrorCode.unauthenticated,
            message: 'Rejected.',
            retryable: false,
          ),
        );

        final VerificationResult verification = verify(
          () => teardownCoordinator.tearDown(
            captureAny(),
            orphanRetrySafeOperations: false,
          ),
        );
        final DovahLinkProtocolException reason =
            verification.captured.single as DovahLinkProtocolException;
        expect(reason.code, ProtocolErrorCode.unauthenticated);
        expect(reason.retryable, isFalse);
      },
    );
  });

  group('Method onSessionInvalidated behaves correctly', () {
    test(
      'Method onSessionInvalidated fails closed with no authenticated session',
      () {
        sessionIdValue = null;
        trustStateValue = null;

        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);

        verify(
          () => teardownCoordinator.tearDown(
            any(),
            orphanRetrySafeOperations: false,
          ),
        ).called(1);
        verifyNever(() => state.invalidate(any()));
      },
    );

    test(
      'Method onSessionInvalidated sets invalidationReason and notifies onTeardown before closing '
      'via closeAfterInvalidation, for an authenticated session',
      () async {
        final List<String> order = <String>[];
        service.onTeardown =
            (Exception reason, {required bool orphanRetrySafeOperations}) {
              order.add('teardown');
            };
        when(
          () => teardownCoordinator.closeAfterInvalidation(any()),
        ).thenAnswer((_) async {
          order.add('close');
        });
        sessionIdValue = 'session-1';
        trustStateValue = DovahLinkTrustState.unpaired;
        connectionGenerationValue = 3;

        service.onSessionInvalidated(AdministrativeInvalidationReason.blocked);
        await pumpEventQueue();

        verify(
          () => state.invalidate(AdministrativeInvalidationReason.blocked),
        ).called(1);
        verify(() => teardownCoordinator.closeAfterInvalidation(3)).called(1);
        expect(order, <String>['teardown', 'close']);
      },
    );

    test(
      'Method onSessionInvalidated preserves trustReset as the typed invalidation reason',
      () {
        sessionIdValue = 'session-1';
        trustStateValue = DovahLinkTrustState.trusted;

        service.onSessionInvalidated(
          AdministrativeInvalidationReason.trustReset,
        );

        verify(
          () => state.invalidate(AdministrativeInvalidationReason.trustReset),
        ).called(1);
      },
    );

    test(
      'Method onSessionInvalidated preserves factoryReset as the typed invalidation reason',
      () {
        sessionIdValue = 'session-1';
        trustStateValue = DovahLinkTrustState.unpaired;

        service.onSessionInvalidated(
          AdministrativeInvalidationReason.factoryReset,
        );

        verify(
          () => state.invalidate(AdministrativeInvalidationReason.factoryReset),
        ).called(1);
      },
    );

    test(
      'Method onSessionInvalidated is never overwritten by a racing onUnhealthy call',
      () {
        sessionIdValue = 'session-1';
        trustStateValue = DovahLinkTrustState.unpaired;
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        // Once administratively invalidated, isAdministrativelyInvalidated reflects that for every
        // subsequent read, exactly as the real SessionState.invalidate() would produce.
        isAdministrativelyInvalidatedValue = true;

        service.onUnhealthy(
          const DovahLinkConnectionException('closed by host'),
        );

        verify(() => state.invalidate(any())).called(1);
        // The later onUnhealthy still calls tearDown -- SessionService itself does not special-
        // case an already-invalidated session for onUnhealthy; ConnectionTeardownCoordinator's own
        // isAdministrativelyInvalidated no-op (proven in its own test file) is what makes this safe.
        expect(teardownReasons, hasLength(1));
      },
    );

    test(
      'Method onSessionInvalidated ignores a duplicate event once already invalidated',
      () {
        sessionIdValue = 'session-1';
        trustStateValue = DovahLinkTrustState.unpaired;
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        isAdministrativelyInvalidatedValue = true;

        service.onSessionInvalidated(AdministrativeInvalidationReason.blocked);

        verify(() => state.invalidate(any())).called(1);
      },
    );

    test(
      'Method onSessionInvalidated terminates an in-progress reconnecting cycle instead of '
      'preserving it',
      () {
        // No session is admitted while reconnecting (recovery has not re-authenticated yet), so this
        // hits the "no authenticated session" fail-closed branch.
        sessionIdValue = null;
        trustStateValue = null;
        connectionStateValue = DovahLinkConnectionState.reconnecting;

        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);

        verify(
          () => teardownCoordinator.tearDown(
            any(),
            orphanRetrySafeOperations: false,
          ),
        ).called(1);
        verifyNever(() => state.invalidate(any()));
      },
    );
  });

  group('Method close behaves correctly', () {
    test('Method close prevents a later connection attempt', () async {
      await service.close();

      expect(service.isTerminallyClosed, isTrue);
      await expectLater(
        service.connect(Uri.parse('ws://127.0.0.1:58231/')),
        throwsA(isA<DovahLinkConnectionException>()),
      );

      verifyNever(() => transport.connect(any()));
      verifyNever(() => state.beginConnectAttempt(any()));
    });

    test(
      'Method close rejects a connection queued before terminal shutdown',
      () async {
        final Completer<void> firstConnectStarted = Completer<void>();
        final Completer<void> firstConnectGate = Completer<void>();
        final List<Uri> requestedUris = <Uri>[];
        when(() => transport.connect(any())).thenAnswer((invocation) {
          requestedUris.add(invocation.positionalArguments.single as Uri);
          firstConnectStarted.complete();
          return firstConnectGate.future;
        });
        addTearDown(() {
          if (!firstConnectGate.isCompleted) {
            firstConnectGate.complete();
          }
        });

        final Future<void> firstConnect = service.connect(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        await firstConnectStarted.future;
        final Future<void> queuedConnect = service.connect(
          Uri.parse('ws://127.0.0.1:58232/'),
        );
        final Future<void> closing = service.close();
        firstConnectGate.complete();

        await firstConnect;
        await expectLater(
          queuedConnect,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await closing;

        expect(service.isTerminallyClosed, isTrue);
        expect(requestedUris, <Uri>[Uri.parse('ws://127.0.0.1:58231/')]);
      },
    );
  });

  group('Method disconnect behaves correctly', () {
    test('Method disconnect tears down without orphaning by default', () async {
      await service.disconnect();

      verify(
        () => teardownCoordinator.tearDown(
          any(),
          orphanRetrySafeOperations: false,
        ),
      ).called(1);
    });

    test(
      'Method disconnect uses the default DovahLinkConnectionException reason when none is supplied',
      () async {
        await service.disconnect();

        final VerificationResult verification = verify(
          () => teardownCoordinator.tearDown(
            captureAny(),
            orphanRetrySafeOperations: false,
          ),
        );
        expect(
          verification.captured.single,
          isA<DovahLinkConnectionException>(),
        );
      },
    );

    test(
      'Method disconnect passes the supplied reason and orphanRetrySafeOperations through unchanged',
      () async {
        const DovahLinkConnectionException reason =
            DovahLinkConnectionException(
              'Reconnect attempt failed; more attempts remain.',
            );

        await service.disconnect(
          orphanRetrySafeOperations: true,
          reason: reason,
        );

        verify(
          () => teardownCoordinator.tearDown(
            reason,
            orphanRetrySafeOperations: true,
          ),
        ).called(1);
      },
    );

    test(
      'Method disconnect is idempotent and does not throw when called twice',
      () async {
        await service.disconnect();

        await expectLater(service.disconnect(), completes);
      },
    );
  });

  group('Behavior stale generation isolation behaves correctly', () {
    test(
      'Behavior stale generation isolation ignores a message delivered after the generation moves on',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));
        // Simulates whatever real teardown eventually bumps the generation for -- proven as its own
        // behavior in connection_teardown_coordinator_test.dart -- as a given precondition here.
        connectionGenerationValue = 1;

        messages.add('late-message');
        await pumpEventQueue();

        expect(incomingMessages, isEmpty);
      },
    );
  });

  group('Behavior onOrdinaryTransportLoss notification behaves correctly', () {
    test(
      'Behavior onOrdinaryTransportLoss notification never fires for onProtocolViolation',
      () {
        service.onProtocolViolation(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'no match',
            retryable: false,
          ),
          orphanRetrySafeOperations: false,
        );

        expect(reconnectUris, isEmpty);
      },
    );

    test(
      'Behavior onOrdinaryTransportLoss notification never fires for onUnsolicitedError',
      () {
        service.onUnsolicitedError(
          const ErrorPayload(
            code: ProtocolErrorCode.rateLimited,
            message: 'Too many requests.',
            retryable: true,
          ),
        );

        expect(reconnectUris, isEmpty);
      },
    );

    test(
      'Behavior onOrdinaryTransportLoss notification never fires for onSessionInvalidated',
      () {
        sessionIdValue = 'session-1';
        trustStateValue = DovahLinkTrustState.trusted;

        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);

        expect(reconnectUris, isEmpty);
      },
    );

    test(
      'Behavior onOrdinaryTransportLoss notification never fires for a deliberate disconnect',
      () async {
        await service.disconnect();

        expect(reconnectUris, isEmpty);
      },
    );
  });
}
