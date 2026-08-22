import 'dart:async';
import 'dart:convert';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/client_session.dart';
import 'package:dovahlink_client_sdk/src/internal/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Mock transport used to isolate client-session lifecycle behavior.
class MockDovahLinkTransport extends Mock implements DovahLinkTransport {}

/// Mock request manager used to capture session teardown and retry decisions.
class MockRequestManager extends Mock implements RequestManager {}

/// Mock message router used to capture inbound message dispatch.
class MockMessageRouter extends Mock implements MessageRouter {}

/// A minimal [DovahLinkTransport] backed by a [StreamController], for the one test that exercises
/// [ClientSession]'s primary constructor end to end with real (not mocked) collaborators.
class StreamControllerTransport implements DovahLinkTransport {
  /// The inbound stream controlled by this transport.
  final StreamController<String> controller = StreamController<String>();

  /// Raw text messages sent through this transport.
  final List<String> sent = <String>[];

  /// Whether [close] has been called.
  bool closeCalled = false;

  /// See [DovahLinkTransport.connect].
  @override
  Future<void> connect(Uri uri) async {}

  /// See [DovahLinkTransport.send].
  @override
  Future<void> send(String text) async {
    sent.add(text);
  }

  /// See [DovahLinkTransport.messages].
  @override
  Stream<String> get messages => controller.stream;

  /// See [DovahLinkTransport.close].
  @override
  Future<void> close() async {
    closeCalled = true;
    await controller.close();
  }
}

/// Long timeout durations used by client-session tests that must not expire during setup.
const Map<TimeoutClass, Duration> _timeouts = <TimeoutClass, Duration>{
  TimeoutClass.short: Duration(seconds: 30),
  TimeoutClass.normal: Duration(seconds: 30),
  TimeoutClass.heavy: Duration(seconds: 30),
};

/// Runs client-session behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockRequestManager requestManager;
  late MockMessageRouter messageRouter;
  late StreamController<String> messages;
  late ClientSession session;

  setUpAll(() {
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
    registerFallbackValue(Uri.parse('ws://127.0.0.1:0/'));
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    requestManager = MockRequestManager();
    messageRouter = MockMessageRouter();
    messages = StreamController<String>.broadcast();
    when(() => transport.messages).thenAnswer((_) => messages.stream);
    when(() => transport.connect(any())).thenAnswer((_) async {});
    when(() => transport.close()).thenAnswer((_) async {});
    session = ClientSession.withCollaborators(
      transport: transport,
      requestManager: requestManager,
      messageRouter: messageRouter,
    );
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
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        expect(session.connectionState, DovahLinkConnectionState.connected);
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
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        session.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        final Future<void> reconnect = session.connect(
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

        final Future<void> disconnect = session.disconnect();
        final Future<void> reconnect = session.connect(
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
          session.connect(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(session.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test('Method connect routes an inbound message to MessageRouter', () async {
      await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

      messages.add('raw-message');
      await pumpEventQueue();

      verify(() => messageRouter.handleIncoming('raw-message')).called(1);
    });

    test(
      'Method connect surfaces the transport\'s own rejection when already connected '
      'as DovahLinkConnectionException',
      () async {
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));
        when(() => transport.connect(any())).thenThrow(
          StateError(
            'Already connected. Call close() before connecting again.',
          ),
        );

        await expectLater(
          session.connect(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(session.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method connect clears an old invalidation reason for a fresh session',
      () async {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );
        session.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        expect(session.connectionState, DovahLinkConnectionState.connected);
        expect(session.invalidationReason, isNull);
        expect(session.currentSessionId, isNull);
        expect(session.currentTrustState, isNull);
      },
    );
  });

  group('Method ensureReceiving behaves correctly', () {
    test(
      'Method ensureReceiving is idempotent and does not re-subscribe',
      () async {
        session.ensureReceiving();
        session.ensureReceiving();

        verify(() => transport.messages).called(1);
      },
    );

    test(
      'Method ensureReceiving converts a synchronous transport failure into DovahLinkConnectionException',
      () {
        when(() => transport.messages).thenThrow(StateError('not connected'));

        expect(
          session.ensureReceiving,
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );

    test(
      'Method ensureReceiving tears the connection down on an async stream error',
      () async {
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        messages.addError(StateError('socket error'));
        await pumpEventQueue();

        expect(session.connectionState, DovahLinkConnectionState.disconnected);
        verify(() => transport.close()).called(1);
        verify(
          () => requestManager.failAll(any(), orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method ensureReceiving tears the connection down when the stream closes',
      () async {
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        await messages.close();
        await pumpEventQueue();

        expect(session.connectionState, DovahLinkConnectionState.disconnected);
        verify(() => transport.close()).called(1);
        verify(
          () => requestManager.failAll(any(), orphanRetrySafeOperations: true),
        ).called(1);
      },
    );
  });

  group('Method admitSession behaves correctly', () {
    test(
      'Method admitSession sets currentSessionId/currentTrustState and triggers retryOrphanedOperations',
      () {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );

        expect(session.currentSessionId, 'session-1');
        expect(session.currentTrustState, DovahLinkTrustState.trusted);
        verify(() => requestManager.retryOrphanedOperations()).called(1);
      },
    );
  });

  group('Method markTrusted behaves correctly', () {
    test(
      'Method markTrusted sets currentTrustState without touching currentSessionId',
      () {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        session.markTrusted();

        expect(session.currentTrustState, DovahLinkTrustState.trusted);
        expect(session.currentSessionId, 'session-1');
      },
    );

    test(
      'Method markTrusted sets currentTrustState even without an admitted session',
      () {
        session.markTrusted();

        expect(session.currentTrustState, DovahLinkTrustState.trusted);
        expect(session.currentSessionId, isNull);
      },
    );
  });

  group('Method onUnhealthy behaves correctly', () {
    test(
      'Method onUnhealthy tears the connection down and orphans retry-safe operations by default',
      () async {
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        session.onUnhealthy(const DovahLinkConnectionException('timed out'));
        await pumpEventQueue();

        expect(session.connectionState, DovahLinkConnectionState.disconnected);
        verify(() => transport.close()).called(1);
        verify(
          () => requestManager.failAll(any(), orphanRetrySafeOperations: true),
        ).called(1);
      },
    );
  });

  group('Method onProtocolViolation behaves correctly', () {
    test(
      'Method onProtocolViolation tears down without orphaning when requested',
      () async {
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        session.onProtocolViolation(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'no match',
            retryable: false,
          ),
          orphanRetrySafeOperations: false,
        );
        await pumpEventQueue();

        verify(
          () => requestManager.failAll(any(), orphanRetrySafeOperations: false),
        ).called(1);
      },
    );
  });

  group('Method onSessionInvalidated behaves correctly', () {
    test(
      'Method onSessionInvalidated fails closed with no authenticated session',
      () async {
        session.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        expect(session.connectionState, DovahLinkConnectionState.disconnected);
        expect(session.invalidationReason, isNull);
        verify(
          () => requestManager.failAll(any(), orphanRetrySafeOperations: false),
        ).called(1);
      },
    );

    test(
      'Method onSessionInvalidated sets invalidationReason/connectionState and fails everything without '
      'orphaning, for an authenticated session',
      () async {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        session.onSessionInvalidated(AdministrativeInvalidationReason.blocked);
        await pumpEventQueue();

        expect(
          session.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          session.invalidationReason,
          AdministrativeInvalidationReason.blocked,
        );
        expect(session.currentSessionId, isNull);
        expect(session.currentTrustState, isNull);
        verify(
          () => requestManager.failAll(any(), orphanRetrySafeOperations: false),
        ).called(1);
        verify(() => transport.close()).called(1);
      },
    );

    test(
      'Method onSessionInvalidated preserves trustReset as the typed invalidation reason',
      () async {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );

        session.onSessionInvalidated(
          AdministrativeInvalidationReason.trustReset,
        );
        await pumpEventQueue();

        expect(
          session.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          session.invalidationReason,
          AdministrativeInvalidationReason.trustReset,
        );
        verify(() => transport.close()).called(1);
      },
    );

    test(
      'Method onSessionInvalidated preserves factoryReset as the typed invalidation reason',
      () async {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        session.onSessionInvalidated(
          AdministrativeInvalidationReason.factoryReset,
        );
        await pumpEventQueue();

        expect(
          session.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          session.invalidationReason,
          AdministrativeInvalidationReason.factoryReset,
        );
        verify(() => transport.close()).called(1);
      },
    );

    test(
      'Method onSessionInvalidated is never overwritten by a racing onUnhealthy call',
      () async {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        session.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        session.onUnhealthy(
          const DovahLinkConnectionException('closed by bridge'),
        );
        await pumpEventQueue();

        expect(
          session.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          session.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
        // Only the one failAll from onSessionInvalidated -- the later onUnhealthy is a no-op.
        verify(
          () => requestManager.failAll(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onSessionInvalidated ignores a duplicate event after teardown',
      () async {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        session.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        session.onSessionInvalidated(AdministrativeInvalidationReason.blocked);
        await pumpEventQueue();

        expect(
          session.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
        verify(
          () => requestManager.failAll(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onSessionInvalidated is never overwritten by a racing onProtocolViolation call',
      () async {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        session.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        session.onProtocolViolation(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'no match',
            retryable: false,
          ),
          orphanRetrySafeOperations: false,
        );
        await pumpEventQueue();

        expect(
          session.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        // Only the one failAll from onSessionInvalidated -- the later onProtocolViolation is a
        // no-op.
        verify(
          () => requestManager.failAll(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onSessionInvalidated is never overwritten by a racing disconnect call',
      () async {
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        session.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        await session.disconnect();

        expect(
          session.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        // Only the one failAll from onSessionInvalidated -- disconnect()'s own teardown is a
        // no-op once already administratively invalidated.
        verify(
          () => requestManager.failAll(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onSessionInvalidated still reaches administrativelyInvalidated even when the transport close fails',
      () async {
        when(() => transport.close()).thenThrow(StateError('close failed'));
        session.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        session.onSessionInvalidated(AdministrativeInvalidationReason.revoked);
        await pumpEventQueue();

        expect(
          session.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
      },
    );
  });

  group('Method disconnect behaves correctly', () {
    test(
      'Method disconnect tears down without orphaning and resets connectionState',
      () async {
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        await session.disconnect();

        expect(session.connectionState, DovahLinkConnectionState.disconnected);
        verify(() => transport.close()).called(1);
        verify(
          () => requestManager.failAll(any(), orphanRetrySafeOperations: false),
        ).called(1);
      },
    );

    test(
      'Method disconnect is idempotent and does not throw when called twice',
      () async {
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        await session.disconnect();

        await expectLater(session.disconnect(), completes);
      },
    );
  });

  group('Behavior stale generation isolation behaves correctly', () {
    test(
      'Behavior stale generation isolation ignores a message delivered after teardown',
      () async {
        await session.connect(Uri.parse('ws://127.0.0.1:58231/'));

        await session.disconnect();
        messages.add('late-message');
        await pumpEventQueue();

        verifyNever(() => messageRouter.handleIncoming(any()));
      },
    );
  });

  group('Property requestManager behaves correctly', () {
    test(
      'Property requestManager exposes the same instance passed to withCollaborators',
      () {
        expect(session.requestManager, same(requestManager));
      },
    );
  });

  group('Method constructor behaves correctly', () {
    test(
      'Method constructor atomically builds real collaborators wired to this session',
      () async {
        final StreamControllerTransport realTransport =
            StreamControllerTransport();
        final ClientSession realSession = ClientSession(
          transport: realTransport,
          timeoutDurations: _timeouts,
        );

        await realSession.connect(Uri.parse('ws://127.0.0.1:58231/'));
        final Future<Envelope> pending = realSession.requestManager
            .sendAndAwait(
              messageType: ProtocolMessageType.hello,
              payload: const <String, dynamic>{},
              expectedType: ProtocolMessageType.helloAck,
              policy: const RequestPolicy(
                retrySafe: false,
                requiredTrustState: null,
                timeoutClass: TimeoutClass.short,
              ),
            );
        await pumpEventQueue();

        final JsonMap sent = jsonDecode(realTransport.sent.single) as JsonMap;
        expect(sent['messageType'], 'hello');
        final String messageId = sent['messageId'] as String;

        realTransport.controller.add(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'reply-1',
            'sessionId': 'session-1',
            'correlationId': messageId,
            'payload': <String, dynamic>{
              'bridgeVersion': '0.3.2',
              'clientIdentityKind': 'unpaired',
            },
            'bridgeInstanceId': 'bridge-1',
            'playContextId': null,
            'clientId': 'client-1',
          }),
        );

        final Envelope response = await pending;
        expect(response.messageType, ProtocolMessageType.helloAck);
      },
    );
  });
}
