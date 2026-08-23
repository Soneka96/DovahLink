import 'dart:async';
import 'dart:convert';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';
import '../fixtures/internal/pending_operation.fixture.dart';

/// Mock transport used to isolate transmission tests from socket I/O.
class MockDovahLinkTransport extends Mock implements DovahLinkTransport {}

/// Mock session service used to supply the current session identity and capture timeout and
/// send-failure notifications, per `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionService extends Mock implements SessionService {}

/// Mock pending-operation owner used to capture message-ID registration and failure.
class MockPendingOperationBookkeeping extends Mock
    implements PendingOperationBookkeeping {}

/// Short timeout durations used to exercise timeout reporting deterministically.
const Map<TimeoutClass, Duration> _shortTimeouts = <TimeoutClass, Duration>{
  TimeoutClass.short: Duration(milliseconds: 20),
  TimeoutClass.normal: Duration(milliseconds: 20),
  TimeoutClass.heavy: Duration(milliseconds: 20),
};

/// Builds a transmitter from the supplied test doubles and timeout policy.
PendingOperationTransmitter buildTransmitter({
  required DovahLinkTransport transport,
  required SessionService sessionService,
  required PendingOperationBookkeeping bookkeeping,
  Map<TimeoutClass, Duration> timeoutDurations = _shortTimeouts,
}) => PendingOperationTransmitter(
  transport: transport,
  timeoutDurations: timeoutDurations,
  sessionService: sessionService,
  bookkeeping: bookkeeping,
);

/// Runs pending-operation transmission behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockSessionService sessionService;
  late MockPendingOperationBookkeeping bookkeeping;

  setUpAll(() {
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    sessionService = MockSessionService();
    bookkeeping = MockPendingOperationBookkeeping();
    when(() => sessionService.currentSessionId).thenReturn('session-1');
    when(() => transport.send(any())).thenAnswer((_) async {});
  });

  group('Method transmit behaves correctly', () {
    test(
      'Method transmit registers the same message ID sent in the envelope',
      () async {
        final PendingOperation operation = buildPendingOperation();
        final PendingOperationTransmitter transmitter = buildTransmitter(
          transport: transport,
          sessionService: sessionService,
          bookkeeping: bookkeeping,
        );

        transmitter.transmit(operation);
        await pumpEventQueue();

        final String registeredMessageId =
            verify(
                  () => bookkeeping.register(captureAny(), operation),
                ).captured.single
                as String;
        final JsonMap envelope =
            jsonDecode(
                  verify(() => transport.send(captureAny())).captured.single,
                )
                as JsonMap;
        expect(envelope['messageType'], 'pairing_request');
        expect(envelope['messageId'], registeredMessageId);
        expect(envelope['sessionId'], 'session-1');
        expect(envelope['payload'], <String, dynamic>{});
        operation.timer?.cancel();
      },
    );

    test(
      'Method transmit reports a timeout through the session service',
      () async {
        final PendingOperation operation = buildPendingOperation();
        final PendingOperationTransmitter transmitter = buildTransmitter(
          transport: transport,
          sessionService: sessionService,
          bookkeeping: bookkeeping,
        );

        transmitter.transmit(operation);
        await Future<void>.delayed(const Duration(milliseconds: 40));

        final List<Object?> reported = verify(
          () => sessionService.onUnhealthy(captureAny()),
        ).captured;
        expect(reported.single, isA<DovahLinkConnectionException>());
        expect(
          (reported.single! as DovahLinkConnectionException).message,
          contains('Timed out awaiting a reply'),
        );
        operation.timer?.cancel();
      },
    );

    test(
      'Method transmit reports a transport send failure through the session service',
      () async {
        when(
          () => transport.send(any()),
        ).thenAnswer((_) async => throw StateError('send failed'));
        final PendingOperation operation = buildPendingOperation();
        final PendingOperationTransmitter transmitter = buildTransmitter(
          transport: transport,
          sessionService: sessionService,
          bookkeeping: bookkeeping,
        );

        transmitter.transmit(operation);
        await pumpEventQueue();

        final List<Object?> reported = verify(
          () => sessionService.onUnhealthy(captureAny()),
        ).captured;
        expect(reported.single, isA<DovahLinkConnectionException>());
        expect(
          (reported.single! as DovahLinkConnectionException).message,
          contains('Failed to send'),
        );
        operation.timer?.cancel();
      },
    );
  });
}
