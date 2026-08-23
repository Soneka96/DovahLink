import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service_impl.dart';
import 'package:dovahlink_client_sdk/src/internal/session_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Mock transport used to isolate [SessionServiceImpl]'s lifecycle behavior.
class MockDovahLinkTransport extends Mock implements DovahLinkTransport {}

/// Runs [SessionServiceImpl] behavior tests. Uses a real [SessionState] directly, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries" -- only the transport is mocked.
void main() {
  late MockDovahLinkTransport transport;
  late SessionState state;
  late SessionServiceImpl service;
  late StreamController<String> messages;
  late List<Exception> teardownReasons;
  late List<bool> teardownOrphanFlags;
  late List<Uri> reconnectUris;
  late List<String> incomingMessages;

  setUpAll(() {
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
    registerFallbackValue(Uri.parse('ws://127.0.0.1:0/'));
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    state = SessionState();
    messages = StreamController<String>.broadcast();
    teardownReasons = <Exception>[];
    teardownOrphanFlags = <bool>[];
    reconnectUris = <Uri>[];
    incomingMessages = <String>[];
    when(() => transport.messages).thenAnswer((_) => messages.stream);
    when(() => transport.connect(any())).thenAnswer((_) async {});
    when(() => transport.close()).thenAnswer((_) async {});
    service = SessionServiceImpl(transport: transport, state: state);
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

  group('Method connect behaves correctly', () {
    test(
      'Method connect transitions to connected and starts receiving on success',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        expect(service.connectionState, DovahLinkConnectionState.connected);
        verify(
          () => transport.connect(Uri.parse('ws://127.0.0.1:58231/')),
        ).called(1);
        verify(() => transport.messages).called(1);
      },
    );

    test(
      'Method connect waits for invalidation cleanup before opening a new transport',
      () async {
        final Completer<void> closeCompleter = Completer<void>();
        when(() => transport.close()).thenAnswer((_) => closeCompleter.future);
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        final Future<void> reconnect = service.connect(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        await pumpEventQueue();

        verifyNever(
          () => transport.connect(Uri.parse('ws://127.0.0.1:58231/')),
        );
        closeCompleter.complete();
        await reconnect;

        verify(
          () => transport.connect(Uri.parse('ws://127.0.0.1:58231/')),
        ).called(1);
      },
    );

    test(
      'Method connect waits for ordinary disconnect cleanup before opening a new transport',
      () async {
        final Completer<void> closeCompleter = Completer<void>();
        when(() => transport.close()).thenAnswer((_) => closeCompleter.future);

        final Future<void> disconnect = service.disconnect();
        final Future<void> reconnect = service.connect(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        await pumpEventQueue();

        verifyNever(
          () => transport.connect(Uri.parse('ws://127.0.0.1:58231/')),
        );
        closeCompleter.complete();
        await disconnect;
        await reconnect;

        verify(
          () => transport.connect(Uri.parse('ws://127.0.0.1:58231/')),
        ).called(1);
      },
    );

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
        expect(service.connectionState, DovahLinkConnectionState.disconnected);
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
        expect(service.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method connect clears an old invalidation reason for a fresh session',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        expect(service.connectionState, DovahLinkConnectionState.connected);
        expect(service.invalidationReason, isNull);
        expect(service.currentSessionId, isNull);
        expect(service.currentTrustState, isNull);
      },
    );

    test(
      'Method connect preserves reconnecting instead of disconnected when a recovery attempt fails',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        await service.connect(uri);
        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();
        expect(service.connectionState, DovahLinkConnectionState.reconnecting);
        when(
          () => transport.connect(any()),
        ).thenThrow(const DovahLinkConnectionException('refused'));

        await expectLater(
          service.connect(uri),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(service.connectionState, DovahLinkConnectionState.reconnecting);
      },
    );
  });

  group('Method onUnhealthy behaves correctly', () {
    test(
      'Method onUnhealthy tears the connection down and notifies onTeardown orphaning by default',
      () async {
        service.onOrdinaryTransportLoss = null;
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(service.connectionState, DovahLinkConnectionState.disconnected);
        verify(() => transport.close()).called(1);
        expect(teardownOrphanFlags, <bool>[true]);
      },
    );

    test(
      'Method onUnhealthy transitions to reconnecting and notifies onOrdinaryTransportLoss when '
      'assigned',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        await service.connect(uri);

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(service.connectionState, DovahLinkConnectionState.reconnecting);
        expect(reconnectUris, <Uri>[uri]);
      },
    );

    test(
      'Method onUnhealthy does not transition to reconnecting or notify onOrdinaryTransportLoss '
      'without a prior successful connect',
      () async {
        // No last-connected URI exists yet, so recovery has no endpoint to retry.
        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(service.connectionState, DovahLinkConnectionState.disconnected);
        expect(reconnectUris, isEmpty);
      },
    );

    test(
      'Method onUnhealthy does not transition to reconnecting when onOrdinaryTransportLoss is not '
      'assigned',
      () async {
        service.onOrdinaryTransportLoss = null;
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        await service.connect(uri);

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(service.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method onUnhealthy does not re-notify onOrdinaryTransportLoss for a second signal while '
      'already reconnecting',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        await service.connect(uri);
        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();
        expect(service.connectionState, DovahLinkConnectionState.reconnecting);

        service.onUnhealthy(const DovahLinkConnectionException('still down'));
        await pumpEventQueue();

        expect(service.connectionState, DovahLinkConnectionState.reconnecting);
        expect(reconnectUris, <Uri>[uri]);
      },
    );

    test(
      'Method onUnhealthy defers to a racing administrative invalidation instead of starting recovery',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));
        final Completer<void> closeCompleter = Completer<void>();
        when(() => transport.close()).thenAnswer((_) => closeCompleter.future);

        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        closeCompleter.complete();
        await pumpEventQueue();

        expect(
          service.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(reconnectUris, isEmpty);
      },
    );

    test('Method onUnhealthy serializes its recovery-transition decision against a racing connect() '
        'call instead of racing it outside the queue', () async {
      final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
      await service.connect(uri);
      final Completer<void> closeCompleter = Completer<void>();
      when(() => transport.close()).thenAnswer((_) => closeCompleter.future);

      service.onUnhealthy(const DovahLinkConnectionException('timed out'));
      await pumpEventQueue();
      // A manual reconnect races in right as teardown is still closing the transport, queued
      // immediately behind it -- before the recovery-transition decision has run.
      final Future<void> manualReconnect = service.connect(uri);
      closeCompleter.complete();
      await manualReconnect;

      // The manual connect() ran to completion as an ordinary (non-recovery) attempt.
      expect(service.connectionState, DovahLinkConnectionState.connected);
      // Bounded recovery's own transition, queued behind the manual connect(), correctly saw the
      // connection already recovered and did not start a redundant recovery cycle.
      expect(reconnectUris, isEmpty);
    });
  });

  group('Method onProtocolViolation behaves correctly', () {
    test(
      'Method onProtocolViolation tears down and notifies onTeardown without orphaning when requested',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        service.onProtocolViolation(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'no match',
            retryable: false,
          ),
          orphanRetrySafeOperations: false,
        );
        await pumpEventQueue();

        expect(teardownOrphanFlags, <bool>[false]);
      },
    );

    test(
      'Method onProtocolViolation tears down and notifies onTeardown orphaning when requested',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        service.onProtocolViolation(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'no match',
            retryable: true,
          ),
          orphanRetrySafeOperations: true,
        );
        await pumpEventQueue();

        expect(teardownOrphanFlags, <bool>[true]);
      },
    );
  });

  group('Method onUnsolicitedError behaves correctly', () {
    test(
      'Method onUnsolicitedError tears down and notifies onTeardown without orphaning, carrying the '
      'bridge-reported classification',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        service.onUnsolicitedError(
          const ErrorPayload(
            code: ProtocolErrorCode.rateLimited,
            message: 'Too many requests.',
            retryable: true,
          ),
        );
        await pumpEventQueue();

        expect(service.connectionState, DovahLinkConnectionState.disconnected);
        verify(() => transport.close()).called(1);
        expect(teardownOrphanFlags, <bool>[false]);
        final DovahLinkProtocolException reason =
            teardownReasons.single as DovahLinkProtocolException;
        expect(reason.code, ProtocolErrorCode.rateLimited);
        expect(reason.message, 'Too many requests.');
        expect(reason.retryable, isTrue);
      },
    );

    test(
      'Method onUnsolicitedError preserves a non-retryable classification',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        service.onUnsolicitedError(
          const ErrorPayload(
            code: ProtocolErrorCode.unauthenticated,
            message: 'Rejected.',
            retryable: false,
          ),
        );
        await pumpEventQueue();

        final DovahLinkProtocolException reason =
            teardownReasons.single as DovahLinkProtocolException;
        expect(reason.code, ProtocolErrorCode.unauthenticated);
        expect(reason.retryable, isFalse);
      },
    );
  });

  group('Method onSessionInvalidated behaves correctly', () {
    test(
      'Method onSessionInvalidated fails closed with no authenticated session',
      () async {
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        expect(service.connectionState, DovahLinkConnectionState.disconnected);
        expect(service.invalidationReason, isNull);
        expect(teardownOrphanFlags, <bool>[false]);
      },
    );

    test(
      'Method onSessionInvalidated sets invalidationReason/connectionState and notifies onTeardown '
      'before closing the transport, for an authenticated session',
      () async {
        final List<String> order = <String>[];
        service.onTeardown =
            (Exception reason, {required bool orphanRetrySafeOperations}) {
              order.add('teardown');
            };
        when(() => transport.close()).thenAnswer((_) async {
          order.add('close');
        });
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        service.onSessionInvalidated(AdministrativeInvalidationReason.blocked);
        await pumpEventQueue();

        expect(
          service.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          service.invalidationReason,
          AdministrativeInvalidationReason.blocked,
        );
        expect(service.currentSessionId, isNull);
        expect(service.currentTrustState, isNull);
        expect(order, <String>['teardown', 'close']);
      },
    );

    test(
      'Method onSessionInvalidated preserves trustReset as the typed invalidation reason',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );

        service.onSessionInvalidated(
          AdministrativeInvalidationReason.trustReset,
        );
        await pumpEventQueue();

        expect(
          service.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          service.invalidationReason,
          AdministrativeInvalidationReason.trustReset,
        );
        verify(() => transport.close()).called(1);
      },
    );

    test(
      'Method onSessionInvalidated preserves factoryReset as the typed invalidation reason',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        service.onSessionInvalidated(
          AdministrativeInvalidationReason.factoryReset,
        );
        await pumpEventQueue();

        expect(
          service.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          service.invalidationReason,
          AdministrativeInvalidationReason.factoryReset,
        );
        verify(() => transport.close()).called(1);
      },
    );

    test(
      'Method onSessionInvalidated is never overwritten by a racing onUnhealthy call',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        service.onUnhealthy(
          const DovahLinkConnectionException('closed by bridge'),
        );
        await pumpEventQueue();

        expect(
          service.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          service.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
        // Only the one teardown notification from onSessionInvalidated -- the later onUnhealthy is a
        // no-op.
        expect(teardownReasons, hasLength(1));
      },
    );

    test(
      'Method onSessionInvalidated ignores a duplicate event after teardown',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        service.onSessionInvalidated(AdministrativeInvalidationReason.blocked);
        await pumpEventQueue();

        expect(
          service.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
        expect(teardownReasons, hasLength(1));
      },
    );

    test(
      'Method onSessionInvalidated is never overwritten by a racing onProtocolViolation call',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        service.onProtocolViolation(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'no match',
            retryable: false,
          ),
          orphanRetrySafeOperations: false,
        );
        await pumpEventQueue();

        expect(
          service.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        // Only the one teardown notification from onSessionInvalidated -- the later
        // onProtocolViolation is a no-op.
        expect(teardownReasons, hasLength(1));
      },
    );

    test(
      'Method onSessionInvalidated is never overwritten by a racing disconnect call',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        await service.disconnect();

        expect(
          service.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        // Only the one teardown notification from onSessionInvalidated -- disconnect()'s own teardown
        // is a no-op once already administratively invalidated.
        expect(teardownReasons, hasLength(1));
      },
    );

    test(
      'Method onSessionInvalidated still reaches administrativelyInvalidated even when the transport '
      'close fails',
      () async {
        when(() => transport.close()).thenThrow(StateError('close failed'));
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        expect(
          service.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
      },
    );

    test('Method onSessionInvalidated terminates an in-progress reconnecting cycle instead of '
        'preserving it', () async {
      // No session is admitted while reconnecting (recovery has not re-authenticated yet), so
      // this hits the "no authenticated session" fail-closed branch -- tearing down to
      // disconnected, never leaving the session stuck at reconnecting.
      await service.connect(Uri.parse('ws://127.0.0.1:58231/'));
      service.onUnhealthy(const DovahLinkConnectionException('timed out'));
      await pumpEventQueue();
      expect(service.connectionState, DovahLinkConnectionState.reconnecting);

      service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
      await pumpEventQueue();

      expect(service.connectionState, DovahLinkConnectionState.disconnected);
    });
  });

  group('Method disconnect behaves correctly', () {
    test(
      'Method disconnect tears down without orphaning, notifies onTeardown, and resets connectionState',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        await service.disconnect();

        expect(service.connectionState, DovahLinkConnectionState.disconnected);
        verify(() => transport.close()).called(1);
        expect(teardownOrphanFlags, <bool>[false]);
      },
    );

    test(
      'Method disconnect is idempotent and does not throw when called twice',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        await service.disconnect();

        await expectLater(service.disconnect(), completes);
      },
    );

    test('Method disconnect preserves orphaned operations and uses the supplied reason when '
        'orphanRetrySafeOperations is true', () async {
      await service.connect(Uri.parse('ws://127.0.0.1:58231/'));
      const DovahLinkConnectionException reason = DovahLinkConnectionException(
        'Reconnect attempt failed; more attempts remain.',
      );

      await service.disconnect(orphanRetrySafeOperations: true, reason: reason);

      expect(teardownReasons, <Exception>[reason]);
      expect(teardownOrphanFlags, <bool>[true]);
      // preserveReconnecting is meaningful only for an already-`reconnecting` session; a plain
      // `connected` session passing orphanRetrySafeOperations: true must still resolve to
      // disconnected, not incorrectly stay at some other state.
      expect(service.connectionState, DovahLinkConnectionState.disconnected);
    });

    test(
      'Method disconnect cancels an in-flight reconnecting cycle back to disconnected',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        await service.connect(uri);
        service.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();
        expect(service.connectionState, DovahLinkConnectionState.reconnecting);

        await service.disconnect();

        expect(service.connectionState, DovahLinkConnectionState.disconnected);
      },
    );
  });

  group('Behavior stale generation isolation behaves correctly', () {
    test(
      'Behavior stale generation isolation ignores a message delivered after teardown',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        await service.disconnect();
        messages.add('late-message');
        await pumpEventQueue();

        expect(incomingMessages, isEmpty);
      },
    );
  });

  group('Behavior onTeardown notification behaves correctly', () {
    test(
      'Behavior onTeardown notification fires exactly once for a duplicate onError+onDone signal '
      'pair belonging to the same dead connection',
      () async {
        service.onOrdinaryTransportLoss = null;
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        messages.addError(StateError('socket error'));
        await messages.close();
        await pumpEventQueue();

        expect(teardownReasons, hasLength(1));
        expect(service.connectionState, DovahLinkConnectionState.disconnected);
        verify(() => transport.close()).called(1);
      },
    );
  });

  group('Behavior onOrdinaryTransportLoss notification behaves correctly', () {
    test(
      'Behavior onOrdinaryTransportLoss notification never fires for onProtocolViolation',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        service.onProtocolViolation(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'no match',
            retryable: false,
          ),
          orphanRetrySafeOperations: false,
        );
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
      },
    );

    test(
      'Behavior onOrdinaryTransportLoss notification never fires for onUnsolicitedError',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        service.onUnsolicitedError(
          const ErrorPayload(
            code: ProtocolErrorCode.rateLimited,
            message: 'Too many requests.',
            retryable: true,
          ),
        );
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
      },
    );

    test(
      'Behavior onOrdinaryTransportLoss notification never fires for onSessionInvalidated',
      () async {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        service.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        expect(reconnectUris, isEmpty);
      },
    );

    test(
      'Behavior onOrdinaryTransportLoss notification never fires for a deliberate disconnect',
      () async {
        await service.connect(Uri.parse('ws://127.0.0.1:58231/'));

        await service.disconnect();

        expect(reconnectUris, isEmpty);
      },
    );
  });
}
