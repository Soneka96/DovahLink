import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_recovery_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
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
  final int value = rawValue.toInt();
  return (value: value, isUnavailable: false);
}

/// Builds a correlated state-snapshot reply envelope for recovery tests.
/// @param revision The authoritative baseline revision.
/// @param value The typed state-area value represented by the payload.
/// @param stateArea The registered area named by the payload.
/// @param stateAuthorityId The Host continuity epoch on the envelope.
/// @param playContextId The loaded play context on the envelope.
/// @param data The state-area object, when a malformed payload is under test.
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
  late IStateRecoveryService<int?> service;

  setUpAll(() {
    registerFallbackValue(Exception('fallback for any()'));
  });

  setUp(() {
    tracker = MockStateRevisionTracker<int?>();
    requests = ControlledRequestService();
    session = MockSessionService();
    when(() => tracker.current).thenReturn(
      Fixtures.buildStateSynchronization<int?>(
        status: DovahLinkStateStatus.synchronized,
        value: 10,
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 1,
      ),
    );
    when(
      () => tracker.applySnapshot(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(true);
    when(() => tracker.beginRecovery()).thenAnswer((_) {});
    when(() => tracker.failRecovery()).thenAnswer((_) {});
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
  });

  group('Method handleEvent behaves correctly', () {
    test(
      'Method handleEvent requests one authenticated Snapshot after a gap',
      () async {
        when(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 4,
            revision: 5,
            value: 50,
            isUnavailable: false,
          ),
        ).thenReturn(StateEventApplyResult.recoveryRequired);

        final StateEventApplyResult result = service.handleEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        );

        expect(result, StateEventApplyResult.recoveryRequired);
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
        expect(
          requests.requests.single.policy.timeoutClass,
          TimeoutClass.normal,
        );

        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(revision: 5, value: 50),
        );
        await service.recover();

        verify(() => tracker.beginRecovery()).called(1);
        verify(
          () => tracker.applySnapshot(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            revision: 5,
            value: 50,
            isUnavailable: false,
          ),
        ).called(1);
      },
    );

    test(
      'Method handleEvent buffers Events and applies only those newer than the '
      'Snapshot',
      () async {
        when(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 4,
            revision: 5,
            value: 50,
            isUnavailable: false,
          ),
        ).thenReturn(StateEventApplyResult.recoveryRequired);
        when(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 2,
            revision: 3,
            value: 30,
            isUnavailable: false,
          ),
        ).thenReturn(StateEventApplyResult.applied);

        service.handleEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        );
        expect(
          service.handleEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 1,
            revision: 2,
            value: 20,
            isUnavailable: false,
          ),
          StateEventApplyResult.buffered,
        );
        expect(
          service.handleEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 2,
            revision: 3,
            value: 30,
            isUnavailable: false,
          ),
          StateEventApplyResult.buffered,
        );

        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(revision: 2, value: 20),
        );
        await service.recover();

        verify(
          () => tracker.applySnapshot(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            revision: 2,
            value: 20,
            isUnavailable: false,
          ),
        ).called(1);
        verify(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 2,
            revision: 3,
            value: 30,
            isUnavailable: false,
          ),
        ).called(1);
        verifyNever(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 1,
            revision: 2,
            value: 20,
            isUnavailable: false,
          ),
        );
      },
    );

    test('Method handleEvent drops Events from other identities', () async {
      when(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).thenReturn(StateEventApplyResult.recoveryRequired);
      service.handleEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );
      service.handleEvent(
        stateAuthorityId: 'authority-2',
        playContextId: 'context-1',
        baseRevision: 2,
        revision: 3,
        value: 30,
        isUnavailable: false,
      );
      service.handleEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-2',
        baseRevision: 3,
        revision: 4,
        value: 40,
        isUnavailable: false,
      );

      requests.requests.single.reply.complete(
        buildStateSnapshotEnvelope(revision: 2, value: 20),
      );
      await service.recover();

      verify(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).called(1);
      verifyNever(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-2',
          playContextId: 'context-1',
          baseRevision: 2,
          revision: 3,
          value: 30,
          isUnavailable: false,
        ),
      );
      verifyNever(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-2',
          baseRevision: 3,
          revision: 4,
          value: 40,
          isUnavailable: false,
        ),
      );
    });

    test(
      'Method handleEvent requests a fresh Snapshot after buffer overflow',
      () async {
        when(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 4,
            revision: 5,
            value: 50,
            isUnavailable: false,
          ),
        ).thenReturn(StateEventApplyResult.recoveryRequired);
        service.handleEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        );

        for (int index = 0; index < kStateRecoveryEventBufferLimit; index++) {
          expect(
            service.handleEvent(
              stateAuthorityId: 'authority-1',
              playContextId: 'context-1',
              baseRevision: index + 1,
              revision: index + 2,
              value: index + 2,
              isUnavailable: false,
            ),
            StateEventApplyResult.buffered,
          );
        }
        expect(
          service.handleEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 129,
            revision: 130,
            value: 130,
            isUnavailable: false,
          ),
          StateEventApplyResult.recoveryRequired,
        );

        requests.requests.first.reply.complete(
          buildStateSnapshotEnvelope(revision: 2, value: 2),
        );
        await Future<void>.delayed(Duration.zero);
        expect(requests.requests, hasLength(2));

        requests.requests.last.reply.complete(
          buildStateSnapshotEnvelope(revision: 130, value: 130),
        );
        await service.recover();

        verifyNever(
          () => tracker.applySnapshot(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            revision: 2,
            value: 2,
            isUnavailable: false,
          ),
        );
        verify(
          () => tracker.applySnapshot(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            revision: 130,
            value: 130,
            isUnavailable: false,
          ),
        ).called(1);
      },
    );

    test(
      'Method handleEvent starts another Snapshot when buffered Events still '
      'have a gap',
      () async {
        when(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 4,
            revision: 5,
            value: 50,
            isUnavailable: false,
          ),
        ).thenReturn(StateEventApplyResult.recoveryRequired);
        when(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 6,
            revision: 7,
            value: 70,
            isUnavailable: false,
          ),
        ).thenReturn(StateEventApplyResult.recoveryRequired);
        service.handleEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        );
        service.handleEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 6,
          revision: 7,
          value: 70,
          isUnavailable: false,
        );

        requests.requests.first.reply.complete(
          buildStateSnapshotEnvelope(revision: 2, value: 20),
        );
        await Future<void>.delayed(Duration.zero);

        expect(requests.requests, hasLength(2));
        requests.requests.last.reply.complete(
          buildStateSnapshotEnvelope(revision: 7, value: 70),
        );
        await service.recover();

        verify(() => tracker.beginRecovery()).called(2);
      },
    );
  });

  group('Method handleSnapshot behaves correctly', () {
    test('Method handleSnapshot applies a matching Host-pushed Snapshot', () {
      service.handleSnapshot(
        stateArea: 'character_level',
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 2,
        value: 20,
        isUnavailable: false,
      );

      verify(
        () => tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          revision: 2,
          value: 20,
          isUnavailable: false,
        ),
      ).called(1);
    });

    test('Method handleSnapshot rejects a Snapshot for a different area', () {
      service.handleSnapshot(
        stateArea: 'character_health',
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 2,
        value: 20,
        isUnavailable: false,
      );

      verify(
        () => session.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: false,
        ),
      ).called(1);
      verifyNever(
        () => tracker.applySnapshot(
          stateAuthorityId: any(named: 'stateAuthorityId'),
          playContextId: any(named: 'playContextId'),
          revision: any(named: 'revision'),
          value: any(named: 'value'),
          isUnavailable: any(named: 'isUnavailable'),
        ),
      );
    });

    test('Method handleSnapshot ignores pushes during recovery', () async {
      when(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).thenReturn(StateEventApplyResult.recoveryRequired);
      service.handleEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );

      service.handleSnapshot(
        stateArea: 'character_level',
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 2,
        value: 20,
        isUnavailable: false,
      );
      verifyNever(
        () => tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          revision: 2,
          value: 20,
          isUnavailable: false,
        ),
      );

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
    });
  });

  group('Method recover behaves correctly', () {
    test('Method recover shares one request across concurrent calls', () async {
      when(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).thenReturn(StateEventApplyResult.recoveryRequired);
      service.handleEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );

      final Future<void> concurrentRecovery = service.recover();
      expect(requests.requests, hasLength(1));
      requests.requests.single.reply.complete(
        buildStateSnapshotEnvelope(revision: 5, value: 50),
      );
      await concurrentRecovery;

      expect(requests.requests, hasLength(1));
    });

    test(
      'Method recover marks failure and follows bounded connection recovery',
      () async {
        when(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 4,
            revision: 5,
            value: 50,
            isUnavailable: false,
          ),
        ).thenReturn(StateEventApplyResult.recoveryRequired);
        service.handleEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        );

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
        verifyNever(
          () => session.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method recover reports malformed Snapshot data as a protocol violation',
      () async {
        when(
          () => tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            baseRevision: 4,
            revision: 5,
            value: 50,
            isUnavailable: false,
          ),
        ).thenReturn(StateEventApplyResult.recoveryRequired);
        service.handleEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        );

        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(
            revision: 5,
            value: 50,
            stateArea: 'unknown_area',
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
        verifyNever(() => session.onUnhealthy(any()));
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

    test('Method recover reports a transport exception', () async {
      when(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).thenReturn(StateEventApplyResult.recoveryRequired);
      service.handleEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );

      requests.requests.single.reply.completeError(
        const DovahLinkConnectionException('recovery transport failed'),
      );
      await service.recover();

      verify(() => tracker.failRecovery()).called(1);
      verify(() => session.onUnhealthy(any())).called(1);
    });

    test('Method recover fails when the tracker rejects a Snapshot', () async {
      when(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).thenReturn(StateEventApplyResult.recoveryRequired);
      when(
        () => tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).thenReturn(false);
      service.handleEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );

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
      when(
        () => tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        ),
      ).thenReturn(StateEventApplyResult.recoveryRequired);
      service.handleEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );

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
      verifyNever(
        () => tracker.applySnapshot(
          stateAuthorityId: any(named: 'stateAuthorityId'),
          playContextId: any(named: 'playContextId'),
          revision: any(named: 'revision'),
          value: any(named: 'value'),
          isUnavailable: any(named: 'isUnavailable'),
        ),
      );
    });
  });
}
