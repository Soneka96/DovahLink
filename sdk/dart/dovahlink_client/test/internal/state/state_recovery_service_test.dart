import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_recovery_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import '../../fixtures/fixtures.dart';
import 'controlled_request_service.dart';
import 'mock_session_service.dart';
import 'mock_state_revision_tracker.dart';

/// Immediate, zero-delay attempts keep recovery retry tests deterministic and fast.
const List<Duration> _shortRetryDelays = <Duration>[
  Duration.zero,
  Duration.zero,
  Duration.zero,
];

/// Mock state-domain definition used to isolate recovery-service behavior.
class MockStateDomainDefinition<T> extends Mock
    implements IStateDomainDefinition<T> {}

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
/// @param domain The mock registered state-domain policy and tracker.
/// @param requests The request fake controlling correlated Snapshot replies.
/// @param session The mock connection lifecycle service.
/// @param retryDelays The bounded retry schedule used by the recovery service.
/// @return The state recovery service under test.
IStateRecoveryService<int?> buildStateRecoveryService({
  required MockStateDomainDefinition<int?> domain,
  required ControlledRequestService requests,
  required MockSessionService session,
  List<Duration> retryDelays = _shortRetryDelays,
}) => StateRecoveryService<int?>(
  domain: domain,
  requestService: requests,
  sessionService: session,
  retryDelays: retryDelays,
);

/// Runs state-recovery-service behavior tests.
void main() {
  late MockStateDomainDefinition<int?> domain;
  late MockStateRevisionTracker<int?> tracker;
  late ControlledRequestService requests;
  late MockSessionService session;
  late StreamController<StateSynchronization<int?>> stateChanges;
  late StateSynchronization<int?> currentState;
  late bool recoveryBufferOverflowed;
  late IStateRecoveryService<int?> service;

  setUpAll(() {
    registerFallbackValue(Exception('fallback for any()'));
    registerFallbackValue(<String, dynamic>{});
    registerFallbackValue(Fixtures.buildStateSynchronization<int?>());
  });

  setUp(() {
    domain = MockStateDomainDefinition<int?>();
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
    when(() => domain.stateArea).thenReturn('character_level');
    when(() => domain.tracker).thenReturn(tracker);
    when(
      () => domain.decodeState(any()),
    ).thenReturn((value: 50, isUnavailable: false));
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
      domain: domain,
      requests: requests,
      session: session,
      retryDelays: _shortRetryDelays,
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

  /// Replaces the live baseline while a recovery request is awaiting its Snapshot.
  /// @param stateAuthorityId The current authority identity.
  /// @param playContextId The current play-context identity.
  /// @param revision The newer live revision.
  /// @param value The current typed state value.
  void replaceCurrentState({
    required String stateAuthorityId,
    required String? playContextId,
    required int revision,
    required int? value,
  }) {
    currentState = Fixtures.buildStateSynchronization<int?>(
      status: DovahLinkStateStatus.synchronized,
      value: value,
      stateAuthorityId: stateAuthorityId,
      playContextId: playContextId,
      revision: revision,
    );
    stateChanges.add(currentState);
  }

  /// Simulates the subscription gate resetting the domain during recovery.
  void unsubscribeCurrentState() {
    when(() => tracker.resetToNotSubscribed()).thenAnswer((_) {
      currentState = Fixtures.buildStateSynchronization<int?>(
        status: DovahLinkStateStatus.notSubscribed,
      );
      stateChanges.add(currentState);
    });
    tracker.resetToNotSubscribed();
  }

  group('Method start behaves correctly', () {
    test(
      'Method start waits for the Host baseline for a newly accepted subscription',
      () async {
        currentState = Fixtures.buildStateSynchronization<int?>(
          status: DovahLinkStateStatus.recovering,
        );
        stateChanges.add(currentState);
        await Future<void>.delayed(Duration.zero);

        expect(requests.requests, isEmpty);
      },
    );

    test(
      'Method start requests recovery for an identified recovering domain',
      () async {
        currentState = Fixtures.buildStateSynchronization<int?>(
          status: DovahLinkStateStatus.recovering,
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
        );
        stateChanges.add(currentState);
        await Future<void>.delayed(Duration.zero);

        expect(requests.requests, hasLength(1));
        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(revision: 5, value: 50),
        );
        await service.recover();

        expect(currentState.status, DovahLinkStateStatus.synchronized);
        expect(currentState.revision, 5);
      },
    );

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

      final JsonMap decodedData =
          verify(() => domain.decodeState(captureAny())).captured.single
              as JsonMap;
      expect(decodedData, <String, dynamic>{'value': 50});
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
      'Method recover ignores a late Snapshot after the domain becomes notSubscribed',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        expect(requests.requests, hasLength(1));
        unsubscribeCurrentState();
        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(revision: 5, value: 50),
        );
        await service.recover();

        expect(currentState.status, DovahLinkStateStatus.notSubscribed);
        verifyNever(() => domain.decodeState(any()));
        verifyNever(
          () => tracker.applySnapshot(
            stateAuthorityId: any(named: 'stateAuthorityId'),
            playContextId: any(named: 'playContextId'),
            revision: any(named: 'revision'),
            value: any(named: 'value'),
            isUnavailable: any(named: 'isUnavailable'),
          ),
        );
        verifyNever(() => tracker.failRecovery());
        verifyNever(() => session.onUnhealthy(any()));
        verify(() => tracker.beginRecovery()).called(1);
        expect(requests.requests, hasLength(1));
      },
    );

    test(
      'Method recover ignores a request failure after the domain becomes notSubscribed',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        unsubscribeCurrentState();
        requests.requests.single.reply.completeError(
          const DovahLinkConnectionException('recovery transport failed'),
        );
        await service.recover();

        expect(currentState.status, DovahLinkStateStatus.notSubscribed);
        verifyNever(() => tracker.failRecovery());
        verifyNever(() => session.onUnhealthy(any()));
        verify(() => tracker.beginRecovery()).called(1);
        expect(requests.requests, hasLength(1));
      },
    );

    test(
      'Method recover does not retry a superseded retryable Host error after unsubscribe',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        unsubscribeCurrentState();
        requests.requests.single.reply.completeError(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.temporarilyUnavailable,
            message: 'The snapshot request was superseded.',
            retryable: true,
          ),
        );
        await service.recover();

        expect(currentState.status, DovahLinkStateStatus.notSubscribed);
        verifyNever(() => tracker.failRecovery());
        verifyNever(() => session.onUnhealthy(any()));
        verify(() => tracker.beginRecovery()).called(1);
        expect(requests.requests, hasLength(1));
      },
    );

    test(
      'Method recover retries a temporarily unavailable Snapshot without making the '
      'session unhealthy',
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
        await pumpEventQueue();
        expect(requests.requests, hasLength(2));

        requests.requests.last.reply.complete(
          buildStateSnapshotEnvelope(revision: 5, value: 50),
        );
        await service.recover();

        expect(currentState.status, DovahLinkStateStatus.synchronized);
        expect(currentState.value, 50);
        verifyNever(() => tracker.failRecovery());
        verifyNever(() => session.onUnhealthy(any()));
      },
    );

    test(
      'Method recover fails only the domain after bounded Snapshot retries and accepts '
      'a later baseline',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        for (int attempt = 0; attempt < _shortRetryDelays.length; attempt++) {
          requests.requests[attempt].reply.completeError(
            const DovahLinkProtocolException(
              code: ProtocolErrorCode.temporarilyUnavailable,
              message: 'State is temporarily unavailable.',
              retryable: true,
            ),
          );
          if (attempt + 1 < _shortRetryDelays.length) {
            await pumpEventQueue();
          }
        }
        await service.recover();

        expect(requests.requests, hasLength(_shortRetryDelays.length));
        expect(currentState.status, DovahLinkStateStatus.failed);
        verify(() => tracker.failRecovery()).called(1);
        verifyNever(() => session.onUnhealthy(any()));

        when(
          () => domain.decodeState(any()),
        ).thenReturn((value: 60, isUnavailable: false));
        final Future<void> laterRecovery = service.recover();
        requests.requests.last.reply.complete(
          buildStateSnapshotEnvelope(revision: 6, value: 60),
        );
        await laterRecovery;

        expect(currentState.status, DovahLinkStateStatus.synchronized);
        expect(currentState.value, 60);
        expect(requests.requests, hasLength(_shortRetryDelays.length + 1));
        verifyNever(() => session.onUnhealthy(any()));
      },
    );

    test(
      'Method recover keeps other retryable protocol errors on the unhealthy-session path',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        requests.requests.single.reply.completeError(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.internalError,
            message: 'The Host could not complete the operation.',
            retryable: true,
          ),
        );
        await service.recover();

        verify(() => tracker.failRecovery()).called(1);
        verify(() => session.onUnhealthy(any())).called(1);
        expect(requests.requests, hasLength(1));
      },
    );

    test(
      'Method recover does not report a non-retryable Host error as unhealthy',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);

        requests.requests.single.reply.completeError(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.temporarilyUnavailable,
            message: 'State is unavailable.',
            retryable: false,
          ),
        );
        await service.recover();

        verify(() => tracker.failRecovery()).called(1);
        expect(requests.requests, hasLength(1));
        verifyNever(() => session.onUnhealthy(any()));
        verifyNever(
          () => session.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
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

    test(
      'Method recover discards a Snapshot from a superseded state authority',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);
        replaceCurrentState(
          stateAuthorityId: 'authority-2',
          playContextId: 'context-1',
          revision: 5,
          value: 20,
        );

        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(
            revision: 6,
            value: 60,
            stateAuthorityId: 'authority-1',
          ),
        );
        await service.recover();

        verifyNever(
          () => tracker.applySnapshot(
            stateAuthorityId: any(named: 'stateAuthorityId'),
            playContextId: any(named: 'playContextId'),
            revision: any(named: 'revision'),
            value: any(named: 'value'),
            isUnavailable: any(named: 'isUnavailable'),
          ),
        );
        expect(currentState.stateAuthorityId, 'authority-2');
        expect(currentState.value, 20);
      },
    );

    test(
      'Method recover discards a Snapshot from a superseded play context',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);
        replaceCurrentState(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-2',
          revision: 5,
          value: 20,
        );

        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(
            revision: 6,
            value: 60,
            playContextId: 'context-1',
          ),
        );
        await service.recover();

        verifyNever(
          () => tracker.applySnapshot(
            stateAuthorityId: any(named: 'stateAuthorityId'),
            playContextId: any(named: 'playContextId'),
            revision: any(named: 'revision'),
            value: any(named: 'value'),
            isUnavailable: any(named: 'isUnavailable'),
          ),
        );
        expect(currentState.playContextId, 'context-2');
        expect(currentState.value, 20);
      },
    );

    test(
      'Method recover discards a Snapshot at an already-reached revision',
      () async {
        emitStaleState();
        await Future<void>.delayed(Duration.zero);
        replaceCurrentState(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          revision: 6,
          value: 60,
        );

        requests.requests.single.reply.complete(
          buildStateSnapshotEnvelope(revision: 5, value: 50),
        );
        await service.recover();

        verifyNever(
          () => tracker.applySnapshot(
            stateAuthorityId: any(named: 'stateAuthorityId'),
            playContextId: any(named: 'playContextId'),
            revision: any(named: 'revision'),
            value: any(named: 'value'),
            isUnavailable: any(named: 'isUnavailable'),
          ),
        );
        expect(currentState.revision, 6);
        expect(currentState.value, 60);
      },
    );

    test('Method recover rejects a Snapshot for another state area', () async {
      emitStaleState();
      await Future<void>.delayed(Duration.zero);

      requests.requests.single.reply.complete(
        buildStateSnapshotEnvelope(
          revision: 5,
          value: 50,
          stateArea: 'character_health',
        ),
      );
      await service.recover();

      verify(() => tracker.failRecovery()).called(1);
      final DovahLinkProtocolException error =
          verify(
                () => session.onProtocolViolation(
                  captureAny(),
                  orphanRetrySafeOperations: false,
                ),
              ).captured.single
              as DovahLinkProtocolException;
      expect(error.code, ProtocolErrorCode.malformedMessage);
      expect(error.retryable, isFalse);
      expect(error.message, contains('character_level'));
    });

    test('Method recover rejects a malformed outer Snapshot payload', () async {
      emitStaleState();
      await Future<void>.delayed(Duration.zero);

      requests.requests.single.reply.complete(
        Fixtures.buildEnvelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-5',
          correlationId: 'request-5',
          payload: const <String, dynamic>{},
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
        ),
      );
      await service.recover();

      verify(() => tracker.failRecovery()).called(1);
      final DovahLinkProtocolException error =
          verify(
                () => session.onProtocolViolation(
                  captureAny(),
                  orphanRetrySafeOperations: false,
                ),
              ).captured.single
              as DovahLinkProtocolException;
      expect(error.code, ProtocolErrorCode.malformedMessage);
      expect(error.retryable, isFalse);
    });

    test('Method recover rejects malformed state data', () async {
      when(() => domain.decodeState(any())).thenThrow(
        const DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'State level is outside its valid range.',
          retryable: false,
        ),
      );
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
      final DovahLinkProtocolException error =
          verify(
                () => session.onProtocolViolation(
                  captureAny(),
                  orphanRetrySafeOperations: false,
                ),
              ).captured.single
              as DovahLinkProtocolException;
      expect(error.code, ProtocolErrorCode.malformedMessage);
      expect(error.retryable, isFalse);
      verifyNever(() => session.onUnhealthy(any()));
      verify(() => domain.decodeState(any())).called(1);
    });
  });
}
