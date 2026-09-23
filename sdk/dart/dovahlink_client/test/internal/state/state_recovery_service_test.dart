import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_recovery_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import '../../fixtures/fixtures.dart';
import 'controlled_request_service.dart';
import 'mock_session_service.dart';
import 'mock_state_revision_tracker.dart';

/// Builds a typed state-area decoder for the integer recovery cases in these tests.
/// @param data The canonical state-area value object.
/// @return The decoded value and its explicit availability status.
({int? value, bool isUnavailable}) decodeIntegerState(JsonMap data) {
  if (!data.containsKey('value')) {
    throw const ProtocolFormatException('State data is missing value.');
  }
  final Object? rawValue = data['value'];
  if (rawValue == null) {
    return (value: null, isUnavailable: true);
  }
  if (rawValue is! num ||
      !rawValue.isFinite ||
      rawValue < 0 ||
      rawValue > 65535 ||
      rawValue != rawValue.toInt()) {
    throw const ProtocolFormatException(
      'State level is outside its valid range.',
    );
  }
  return (value: rawValue.toInt(), isUnavailable: false);
}

/// Builds a correlated state-snapshot reply envelope for recovery tests.
/// @param revision The authoritative baseline revision.
/// @param value The typed state-area value represented by the payload.
/// @param stateArea The registered area named by the payload.
/// @param stateAuthorityId The Host continuity epoch on the envelope.
/// @param playContextId The loaded play context on the envelope.
/// @param data The state-area object, when malformed input is under test.
/// @return A protocol envelope with the supplied Snapshot payload and identity.
Envelope buildStateSnapshotEnvelope({
  required int revision,
  required int? value,
  String stateArea = 'character_level',
  String stateAuthorityId = 'authority-1',
  String? playContextId = 'context-1',
  JsonMap? data,
}) => Fixtures.buildEnvelope(
  messageType: ProtocolMessageType.stateSnapshot,
  messageId: 'snapshot-$revision',
  correlationId: 'request-$revision',
  payload: <String, dynamic>{
    'stateArea': stateArea,
    'revision': revision,
    'occurredAt': '2026-09-23T12:00:00Z',
    'data': data ?? <String, dynamic>{'value': value},
  },
  stateAuthorityId: stateAuthorityId,
  playContextId: playContextId,
);

/// Builds one recovery service over test-controlled collaborators.
/// @param tracker The mock domain revision tracker.
/// @param requests The request fake controlling correlated Snapshot replies.
/// @param session The mock connection lifecycle service.
/// @return The state recovery service under test.
IStateRecoveryService<int?> buildStateRecoveryService({
  required MockStateRevisionTracker<int?> tracker,
  required ControlledRequestService requests,
  required MockSessionService session,
}) => StateRecoveryService<int?>(
  stateArea: 'character_level',
  tracker: tracker,
  requestService: requests,
  sessionService: session,
  decodeState: decodeIntegerState,
);

/// Runs state-recovery-service behavior tests.
void main() {
  late MockStateRevisionTracker<int?> tracker;
  late ControlledRequestService requests;
  late MockSessionService session;
  late StreamController<StateSynchronization<int?>> stateChanges;
  late StateSynchronization<int?> currentState;
  late bool recoveryBufferOverflowed;
  late IStateRecoveryService<int?> service;

  setUpAll(() {
    registerFallbackValue(Exception('fallback for any()'));
    registerFallbackValue(Fixtures.buildStateSynchronization<int?>());
  });

  setUp(() {
    tracker = MockStateRevisionTracker<int?>();
    requests = ControlledRequestService();
    session = MockSessionService();
    stateChanges = StreamController<StateSynchronization<int?>>.broadcast();
    addTearDown(stateChanges.close);
    recoveryBufferOverflowed = false;
    currentState = Fixtures.buildStateSynchronization<int?>(
      status: DovahLinkStateStatus.synchronized,
      value: 10,
      stateAuthorityId: 'authority-1',
      playContextId: 'context-1',
      revision: 1,
    );
    when(() => tracker.current).thenAnswer((_) => currentState);
    when(() => tracker.changes).thenAnswer((_) => stateChanges.stream);
    when(
      () => tracker.recoveryBufferOverflowed,
    ).thenAnswer((_) => recoveryBufferOverflowed);
    when(() => tracker.beginRecovery()).thenAnswer((_) {
      recoveryBufferOverflowed = false;
      currentState = Fixtures.buildStateSynchronization<int?>(
        status: DovahLinkStateStatus.recovering,
        value: currentState.value,
        stateAuthorityId: currentState.stateAuthorityId,
        playContextId: currentState.playContextId,
        revision: currentState.revision,
      );
      stateChanges.add(currentState);
    });
    when(() => tracker.failRecovery()).thenAnswer((_) {
      currentState = Fixtures.buildStateSynchronization<int?>(
        status: DovahLinkStateStatus.failed,
        value: currentState.value,
        stateAuthorityId: currentState.stateAuthorityId,
        playContextId: currentState.playContextId,
        revision: currentState.revision,
      );
      stateChanges.add(currentState);
    });
    when(
      () => tracker.applySnapshot(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenAnswer((Invocation invocation) {
      currentState = Fixtures.buildStateSynchronization<int?>(
        status: invocation.namedArguments[#isUnavailable]! as bool
            ? DovahLinkStateStatus.unavailable
            : DovahLinkStateStatus.synchronized,
        value: invocation.namedArguments[#value] as int?,
        stateAuthorityId:
            invocation.namedArguments[#stateAuthorityId] as String,
        playContextId: invocation.namedArguments[#playContextId] as String?,
        revision: invocation.namedArguments[#revision] as int,
      );
      stateChanges.add(currentState);
      return true;
    });
    when(
      () => session.currentTrustState,
    ).thenReturn(DovahLinkTrustState.trusted);
    when(
      () => session.connectionState,
    ).thenReturn(DovahLinkConnectionState.connected);
    when(() => session.onUnhealthy(any())).thenAnswer((_) {});
    when(
      () => session.onProtocolViolation(
        any(),
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
      ),
    ).thenAnswer((_) {});
    service = buildStateRecoveryService(
      tracker: tracker,
      requests: requests,
      session: session,
    );
    service.start();
  });

  /// Emits a stale tracker view to start recovery.
  void emitStaleState() {
    currentState = Fixtures.buildStateSynchronization<int?>(
      status: DovahLinkStateStatus.stale,
      value: 10,
      stateAuthorityId: 'authority-1',
      playContextId: 'context-1',
      revision: 1,
    );
    recoveryBufferOverflowed = false;
    stateChanges.add(currentState);
  }

  group('Method start behaves correctly', () {
    test('Method start requests a Snapshot after a stale transition', () async {
      emitStaleState();
      await Future<void>.delayed(Duration.zero);

      expect(requests.requests, hasLength(1));
      expect(
        requests.requests.single.messageType,
        ProtocolMessageType.snapshotRequest,
      );
      expect(
        requests.requests.single.expectedType,
        ProtocolMessageType.stateSnapshot,
      );
      expect(requests.requests.single.payload, <String, dynamic>{
        'stateArea': 'character_level',
        'knownRevision': 1,
      });
      expect(requests.requests.single.policy.retrySafe, isTrue);
      expect(
        requests.requests.single.policy.requiredTrustState,
        DovahLinkTrustState.trusted,
      );
      expect(requests.requests.single.policy.timeoutClass, TimeoutClass.normal);

      requests.requests.single.reply.complete(
        buildStateSnapshotEnvelope(revision: 5, value: 50),
      );
      await service.recover();

      verify(
        () => tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).called(1);
      expect(currentState.status, DovahLinkStateStatus.synchronized);
      expect(currentState.value, 50);
    });

    test(
      'Method start coalesces repeated stale transitions into one request',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);
        emitStaleState();

        expect(requests.requests, hasLength(1));
        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(revision: 5, value: 50),
        );
        await service.recover();
        expect(requests.requests, hasLength(1));
      },
    );

    test(
      'Method start requests a new Snapshot after buffer overflow',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        currentState = Fixtures.buildStateSynchronization<int?>(
          status: DovahLinkStateStatus.stale,
          value: 10,
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          revision: 1,
        );
        recoveryBufferOverflowed = true;
        stateChanges.add(currentState);
        requests.requests.first.reply.complete(
          buildStateSnapshotEnvelope(revision: 2, value: 20),
        );
        await Future<void>.delayed(Duration.zero);

        expect(requests.requests, hasLength(2));
        requests.requests.last.reply.complete(
          buildStateSnapshotEnvelope(revision: 5, value: 50),
        );
        await service.recover();
        expect(currentState.status, DovahLinkStateStatus.synchronized);
      },
    );
  });

  group('Method recover behaves correctly', () {
    test('Method recover shares one request across concurrent calls', () async {
      emitStaleState();
      await Future<void>.delayed(Duration.zero);

      final Future<void> concurrentRecovery = service.recover();
      expect(requests.requests, hasLength(1));
      requests.requests.single.reply.complete(
        buildStateSnapshotEnvelope(revision: 5, value: 50),
      );
      await concurrentRecovery;
      expect(requests.requests, hasLength(1));
    });

    test(
      'Method recover follows bounded recovery after a retryable Host error',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        requests.requests.single.reply.completeError(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.temporarilyUnavailable,
            message: 'State is temporarily unavailable.',
            retryable: true,
          ),
        );
        await service.recover();

        verify(() => tracker.failRecovery()).called(1);
        verify(() => session.onUnhealthy(any())).called(1);
        expect(currentState.status, DovahLinkStateStatus.failed);
      },
    );

    test('Method recover stays offline without an active session', () async {
      when(() => session.currentTrustState).thenReturn(null);
      when(
        () => session.connectionState,
      ).thenReturn(DovahLinkConnectionState.disconnected);

      await service.recover();

      expect(requests.requests, isEmpty);
      verify(() => tracker.failRecovery()).called(1);
      verifyNever(() => session.onUnhealthy(any()));
    });

    test(
      'Method recover reports a transport exception to the lifecycle',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        requests.requests.single.reply.completeError(
          const DovahLinkConnectionException('recovery transport failed'),
        );
        await service.recover();

        verify(() => tracker.failRecovery()).called(1);
        verify(() => session.onUnhealthy(any())).called(1);
      },
    );

    test('Method recover rejects an unaccepted Snapshot', () async {
      when(
        () => tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).thenReturn(false);
      emitStaleState();
      await Future<void>.delayed(Duration.zero);

      requests.requests.single.reply.complete(
        buildStateSnapshotEnvelope(revision: 5, value: 50),
      );
      await service.recover();

      verify(() => tracker.failRecovery()).called(1);
      verify(
        () => session.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: false,
        ),
      ).called(1);
    });

    test('Method recover rejects malformed state data', () async {
      emitStaleState();
      await Future<void>.delayed(Duration.zero);

      requests.requests.single.reply.complete(
        buildStateSnapshotEnvelope(
          revision: 5,
          value: null,
          data: <String, dynamic>{'value': 'not an integer'},
        ),
      );
      await service.recover();

      verify(() => tracker.failRecovery()).called(1);
      verify(
        () => session.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: false,
        ),
      ).called(1);
    });
  });
}
