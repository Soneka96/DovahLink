import 'dart:async';
import 'dart:convert';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_registry.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Mock transport used to isolate transmission tests from socket I/O.
class MockDovahLinkTransport extends Mock implements DovahLinkTransport {}

/// Mock session context used to supply the current session identity.
class MockSessionContext extends Mock implements SessionContext {}

/// Mock lifecycle reporter used to capture timeout and send-failure notifications.
class MockConnectionLifecycleReporter extends Mock
    implements ConnectionLifecycleReporter {}

/// Mock pending-operation owner used to capture message-ID registration and failure.
class MockPendingOperationRegistry extends Mock
    implements PendingOperationRegistry {}

/// Short timeout durations used to exercise timeout reporting deterministically.
const Map<TimeoutClass, Duration> _shortTimeouts = <TimeoutClass, Duration>{
  TimeoutClass.short: Duration(milliseconds: 20),
  TimeoutClass.normal: Duration(milliseconds: 20),
  TimeoutClass.heavy: Duration(milliseconds: 20),
};

/// Builds a representative retry-safe pending operation.
PendingOperation buildPendingOperation({RequestPolicy? policy}) =>
    PendingOperation(
      messageType: ProtocolMessageType.pairingRequest,
      payload: const <String, dynamic>{},
      policy:
          policy ??
          const RequestPolicy(
            retrySafe: true,
            requiredTrustState: DovahLinkTrustState.unpaired,
            timeoutClass: TimeoutClass.normal,
          ),
    );

/// Builds a transmitter from the supplied test doubles and timeout policy.
PendingOperationTransmitter buildTransmitter({
  required DovahLinkTransport transport,
  required SessionContext sessionContext,
  required ConnectionLifecycleReporter reporter,
  required PendingOperationRegistry registry,
  Map<TimeoutClass, Duration> timeoutDurations = _shortTimeouts,
}) => PendingOperationTransmitter(
  transport: transport,
  timeoutDurations: timeoutDurations,
  sessionContext: sessionContext,
  reporter: reporter,
  registry: registry,
);

/// Runs pending-operation transmission behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockSessionContext sessionContext;
  late MockConnectionLifecycleReporter reporter;
  late MockPendingOperationRegistry registry;

  setUpAll(() {
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    sessionContext = MockSessionContext();
    reporter = MockConnectionLifecycleReporter();
    registry = MockPendingOperationRegistry();
    when(() => sessionContext.currentSessionId).thenReturn('session-1');
    when(() => transport.send(any())).thenAnswer((_) async {});
  });

  group('Method transmit behaves correctly', () {
    test(
      'Method transmit registers the same message ID sent in the envelope',
      () async {
        final PendingOperation operation = buildPendingOperation();
        final PendingOperationTransmitter transmitter = buildTransmitter(
          transport: transport,
          sessionContext: sessionContext,
          reporter: reporter,
          registry: registry,
        );

        transmitter.transmit(operation);
        await pumpEventQueue();

        final String registeredMessageId =
            verify(
                  () => registry.register(captureAny(), operation),
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
      'Method transmit reports a timeout through the lifecycle reporter',
      () async {
        final PendingOperation operation = buildPendingOperation();
        final PendingOperationTransmitter transmitter = buildTransmitter(
          transport: transport,
          sessionContext: sessionContext,
          reporter: reporter,
          registry: registry,
        );

        transmitter.transmit(operation);
        await Future<void>.delayed(const Duration(milliseconds: 40));

        final List<Object?> reported = verify(
          () => reporter.onUnhealthy(captureAny()),
        ).captured;
        verify(() => registry.fail(any(), operation, any())).called(1);
        expect(reported.single, isA<DovahLinkConnectionException>());
        expect(
          (reported.single! as DovahLinkConnectionException).message,
          contains('Timed out awaiting a reply'),
        );
        operation.timer?.cancel();
      },
    );

    test(
      'Method transmit reports a transport send failure through the lifecycle reporter',
      () async {
        when(
          () => transport.send(any()),
        ).thenAnswer((_) async => throw StateError('send failed'));
        final PendingOperation operation = buildPendingOperation();
        final PendingOperationTransmitter transmitter = buildTransmitter(
          transport: transport,
          sessionContext: sessionContext,
          reporter: reporter,
          registry: registry,
        );

        transmitter.transmit(operation);
        await pumpEventQueue();

        final List<Object?> reported = verify(
          () => reporter.onUnhealthy(captureAny()),
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
