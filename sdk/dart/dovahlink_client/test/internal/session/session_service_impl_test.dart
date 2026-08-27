import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/session/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/session/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service_impl.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// Mock transport used to isolate [SessionServiceImpl]'s lifecycle behavior.
class MockDovahLinkTransport extends Mock implements IDovahLinkTransport {}

/// Mock session state used per `ai/context/sdk/testing.md`'s "Service test boundaries" -- its own
/// state-derivation behavior (for example preserving `reconnecting` through a failed recovery
/// attempt) is `session_state_test.dart`'s responsibility; this file only proves
/// [SessionServiceImpl] calls the right transition method with the right arguments.
class MockSessionState extends Mock implements SessionState {}

/// Mock lifecycle queue used per `ai/context/sdk/testing.md`'s "Service test boundaries". Stubbed
/// to delegate to a real, test-local [LifecycleOperationQueue] instance purely so this file's
/// racing/ordering tests exercise [SessionServiceImpl]'s own queueing usage under genuine
/// concurrent-call scheduling, without [SessionServiceImpl] itself ever depending on anything but
/// the mock.
class MockLifecycleOperationQueue extends Mock
    implements LifecycleOperationQueue {}

/// Mock teardown coordinator used per `ai/context/sdk/testing.md`'s "Service test boundaries" --
/// its own generation-check dedup logic is `connection_teardown_coordinator_test.dart`'s
/// responsibility; this file only proves [SessionServiceImpl] calls it with the right arguments
/// for each reactive signal.
class MockConnectionTeardownCoordinator extends Mock
    implements ConnectionTeardownCoordinator {}

/// Fake stream subscription used to register a mocktail fallback for `any()`.
class FakeStreamSubscription extends Fake
    implements StreamSubscription<String> {}

/// Runs [SessionServiceImpl] behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockSessionState state;
  late MockLifecycleOperationQueue lifecycleQueue;
  late MockConnectionTeardownCoordinator teardownCoordinator;
  late SessionServiceImpl service;
  late StreamController<String> messages;
  late List<Exception> teardownReasons;
  late List<bool> teardownOrphanFlags;
  late List<Uri> reconnectUris;
  late List<String> incomingMessages;
  late DovahLinkConnectionState connectionStateValue;
  late int connectionGenerationValue;
  late String? sessionIdValue;
  late DovahLinkTrustState? trustStateValue;
  late bool isAdministrativelyInvalidatedValue;
  late Uri? lastConnectedUriValue;

  setUpAll(() {
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
    registerFallbackValue(Uri.parse('ws://127.0.0.1:0/'));
    registerFallbackValue(FakeStreamSubscription());
    registerFallbackValue(AdministrativeInvalidationReason.revoked);
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    state = MockSessionState();
    final LifecycleOperationQueue realScheduling = LifecycleOperationQueue();
    lifecycleQueue = MockLifecycleOperationQueue();
    when(() => lifecycleQueue.run(any())).thenAnswer(
      (Invocation invocation) => realScheduling.run(
        invocation.positionalArguments[0] as Future<void> Function(),
      ),
    );
    teardownCoordinator = MockConnectionTeardownCoordinator();
    when(
      () => teardownCoordinator.tearDown(
        any(),
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
      ),
    ).thenAnswer((_) async {});
    when(
      () => teardownCoordinator.closeAfterInvalidation(any()),
    ).thenAnswer((_) async {});
    messages = StreamController<String>.broadcast();
    teardownReasons = <Exception>[];
    teardownOrphanFlags = <bool>[];
    reconnectUris = <Uri>[];
    incomingMessages = <String>[];
    connectionStateValue = DovahLinkConnectionState.disconnected;
    connectionGenerationValue = 0;
    sessionIdValue = null;
    trustStateValue = null;
    isAdministrativelyInvalidatedValue = false;
    lastConnectedUriValue = null;
    when(() => transport.messages).thenAnswer((_) => messages.stream);
    when(() => transport.connect(any())).thenAnswer((_) async {});
    when(() => transport.close()).thenAnswer((_) async {});
    when(() => state.connectionState).thenAnswer((_) => connectionStateValue);
    when(
      () => state.connectionGeneration,
    ).thenAnswer((_) => connectionGenerationValue);
    when(() => state.sessionId).thenAnswer((_) => sessionIdValue);
    when(() => state.trustState).thenAnswer((_) => trustStateValue);
    when(
      () => state.isAdministrativelyInvalidated,
    ).thenAnswer((_) => isAdministrativelyInvalidatedValue);
    when(() => state.lastConnectedUri).thenAnswer((_) => lastConnectedUriValue);
    when(() => state.beginConnectAttempt(any())).thenAnswer((
      Invocation invocation,
    ) {
      lastConnectedUriValue = invocation.positionalArguments[0] as Uri;
    });
    when(() => state.markConnected()).thenAnswer((_) {
      connectionStateValue = DovahLinkConnectionState.connected;
    });
    when(() => state.markConnectFailed()).thenAnswer((_) {
      connectionStateValue = DovahLinkConnectionState.disconnected;
    });
    when(() => state.markReconnecting()).thenAnswer((_) {
      connectionStateValue = DovahLinkConnectionState.reconnecting;
    });
    when(() => state.attachMessageSubscription(any())).thenAnswer((_) {});
    when(() => state.invalidate(any())).thenAnswer((_) {});
    service = SessionServiceImpl(
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
    service.onOrdinaryTransportLoss = reconnectUris.add;
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
            orphanRetrySafeOperations: true,
          ),
        ).called(1);
      },
    );

    test(
      'Method onUnhealthy transitions to reconnecting and notifies onOrdinaryTransportLoss when the '
      'coordinator resolves to disconnected with a known endpoint',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.disconnected;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(reconnectUris, <Uri>[Uri.parse('ws://127.0.0.1:58231/')]);
        verify(() => state.markReconnecting()).called(1);
      },
    );

    test(
      'Method onUnhealthy does not transition to reconnecting or notify onOrdinaryTransportLoss '
      'without a known last-connected endpoint',
      () async {
        lastConnectedUriValue = null;
        connectionStateValue = DovahLinkConnectionState.disconnected;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        verifyNever(() => state.markReconnecting());
      },
    );

    test(
      'Method onUnhealthy does not transition to reconnecting when onOrdinaryTransportLoss is not '
      'assigned',
      () async {
        service.onOrdinaryTransportLoss = null;
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue = DovahLinkConnectionState.disconnected;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        verifyNever(() => state.markReconnecting());
      },
    );

    test(
      'Method onUnhealthy does not transition to reconnecting when the coordinator resolves to a '
      'state other than disconnected (a racing administrative invalidation owns its own terminal '
      'state)',
      () async {
        lastConnectedUriValue = Uri.parse('ws://127.0.0.1:58231/');
        connectionStateValue =
            DovahLinkConnectionState.administrativelyInvalidated;

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
        verifyNever(() => state.markReconnecting());
      },
    );

    test('Method onUnhealthy serializes its recovery-transition decision against a racing connect() '
        'call instead of racing it outside the queue', () async {
      final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
      final Completer<void> teardownCompleter = Completer<void>();
      when(
        () => teardownCoordinator.tearDown(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      ).thenAnswer((_) => teardownCompleter.future);
      lastConnectedUriValue = uri;

      service.onUnhealthy(const DovahLinkConnectionException('timed out'));
      await pumpEventQueue();
      // A manual reconnect races in right as teardown is still resolving, queued immediately
      // behind it -- before the recovery-transition decision has run.
      connectionStateValue = DovahLinkConnectionState.connected;
      final Future<void> manualReconnect = service.connect(uri);
      teardownCompleter.complete();
      await manualReconnect;

      // Bounded recovery's own transition, queued behind the manual connect(), correctly saw the
      // connection already recovered (not disconnected) and did not start a redundant recovery
      // cycle.
      expect(reconnectUris, isEmpty);
      verifyNever(() => state.markReconnecting());
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
      'Method onUnsolicitedError tears down without orphaning, carrying the bridge-reported '
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
          const DovahLinkConnectionException('closed by bridge'),
        );

        verify(() => state.invalidate(any())).called(1);
        // The later onUnhealthy still calls tearDown -- SessionServiceImpl itself does not special-
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
