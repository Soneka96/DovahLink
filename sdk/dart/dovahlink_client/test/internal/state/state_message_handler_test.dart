import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'mock_session_service.dart';
import 'mock_state_revision_tracker.dart';

/// Mock state-domain definition used to isolate handler routing behavior.
class MockStateDomainDefinition extends Mock
    implements IStateDomainDefinition<Object?> {}

/// Builds a state Snapshot envelope with one registered area.
/// @param area The canonical state area on the payload.
/// @param revision The authoritative revision.
/// @param data The complete typed area value.
/// @return A Snapshot envelope ready for the state handler.
Envelope buildSnapshotEnvelope({
  required String area,
  required int revision,
  required JsonMap data,
}) => Envelope(
  messageType: ProtocolMessageType.stateSnapshot,
  messageId: 'snapshot-$revision',
  sessionId: 'session-1',
  correlationId: null,
  payload: <String, dynamic>{
    'stateArea': area,
    'revision': revision,
    'occurredAt': '2026-09-23T12:00:00Z',
    'data': data,
  },
  stateAuthorityId: 'authority-1',
  playContextId: 'context-1',
  clientId: null,
);

/// Builds a state Event envelope with one complete post-change value.
/// @param area The canonical state area on the payload.
/// @param baseRevision The revision the Event expects.
/// @param revision The Event's resulting revision.
/// @param data The complete post-change area value.
/// @return An Event envelope ready for the state handler.
Envelope buildEventEnvelope({
  required String area,
  required int baseRevision,
  required int revision,
  required JsonMap data,
}) => Envelope(
  messageType: ProtocolMessageType.stateEvent,
  messageId: 'event-$revision',
  sessionId: 'session-1',
  correlationId: null,
  payload: <String, dynamic>{
    'stateArea': area,
    'baseRevision': baseRevision,
    'revision': revision,
    'occurredAt': '2026-09-23T12:00:01Z',
    'data': data,
  },
  stateAuthorityId: 'authority-1',
  playContextId: 'context-1',
  clientId: null,
);

/// Builds a handler over explicit domain registrations.
/// @param session The session lifecycle dependency.
/// @param domains The registered state-area definitions.
/// @return The state-message handler under test.
IStateMessageHandler buildStateMessageHandler({
  required MockSessionService session,
  required List<IStateDomainDefinition<Object?>> domains,
}) {
  final StateMessageHandler handler = StateMessageHandler(
    sessionService: session,
    domains: domains,
  );
  handler.setSubscribedStateAreas(<String>{
    for (final IStateDomainDefinition<Object?> domain in domains)
      domain.stateArea,
  });
  return handler;
}

/// Runs state-message-handler behavior tests.
void main() {
  late MockSessionService session;
  late MockStateDomainDefinition domainA;
  late MockStateDomainDefinition domainB;
  late MockStateDomainDefinition domainC;
  late MockStateDomainDefinition domainD;
  late MockStateDomainDefinition domainE;
  late MockStateRevisionTracker<Object?> trackerA;
  late MockStateRevisionTracker<Object?> trackerB;
  late MockStateRevisionTracker<Object?> trackerC;
  late MockStateRevisionTracker<Object?> trackerD;
  late MockStateRevisionTracker<Object?> trackerE;
  late IStateMessageHandler handler;

  setUpAll(() {
    registerFallbackValue(
      buildSnapshotEnvelope(
        area: 'fallback',
        revision: 1,
        data: const <String, dynamic>{},
      ),
    );
    registerFallbackValue(
      buildEventEnvelope(
        area: 'fallback',
        baseRevision: 1,
        revision: 2,
        data: const <String, dynamic>{},
      ),
    );
    registerFallbackValue(
      const StateSnapshotPayload(
        stateArea: 'fallback',
        revision: 1,
        occurredAt: '2026-09-23T12:00:00Z',
        data: <String, dynamic>{},
      ),
    );
    registerFallbackValue(
      const StateEventPayload(
        stateArea: 'fallback',
        baseRevision: 1,
        revision: 2,
        occurredAt: '2026-09-23T12:00:00Z',
        data: <String, dynamic>{},
      ),
    );
    registerFallbackValue(Exception('fallback for any()'));
  });

  setUp(() {
    session = MockSessionService();
    domainA = MockStateDomainDefinition();
    domainB = MockStateDomainDefinition();
    domainC = MockStateDomainDefinition();
    domainD = MockStateDomainDefinition();
    domainE = MockStateDomainDefinition();
    trackerA = MockStateRevisionTracker<Object?>();
    trackerB = MockStateRevisionTracker<Object?>();
    trackerC = MockStateRevisionTracker<Object?>();
    trackerD = MockStateRevisionTracker<Object?>();
    trackerE = MockStateRevisionTracker<Object?>();
    when(() => domainA.stateArea).thenReturn('area_a');
    when(() => domainB.stateArea).thenReturn('area_b');
    when(() => domainC.stateArea).thenReturn('area_c');
    when(() => domainD.stateArea).thenReturn('area_d');
    when(() => domainE.stateArea).thenReturn('area_e');
    when(() => domainA.tracker).thenReturn(trackerA);
    when(() => domainB.tracker).thenReturn(trackerB);
    when(() => domainC.tracker).thenReturn(trackerC);
    when(() => domainD.tracker).thenReturn(trackerD);
    when(() => domainE.tracker).thenReturn(trackerE);
    for (final MockStateRevisionTracker<Object?> tracker
        in <MockStateRevisionTracker<Object?>>[
          trackerA,
          trackerB,
          trackerC,
          trackerD,
          trackerE,
        ]) {
      when(() => tracker.beginRecovery()).thenAnswer((_) {});
      when(() => tracker.resetToNotSubscribed()).thenAnswer((_) {});
    }
    when(
      () => session.onProtocolViolation(
        any(),
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
      ),
    ).thenAnswer((_) {});
    handler = buildStateMessageHandler(
      session: session,
      domains: <IStateDomainDefinition<Object?>>[
        domainA,
        domainB,
        domainC,
        domainD,
        domainE,
      ],
    );
  });

  group('Method handle behaves correctly', () {
    test('Method handle routes every registered Snapshot by state area', () {
      handler.handle(
        buildSnapshotEnvelope(
          area: 'area_a',
          revision: 1,
          data: const <String, dynamic>{'value': 42.5},
        ),
      );
      handler.handle(
        buildSnapshotEnvelope(
          area: 'area_b',
          revision: 2,
          data: const <String, dynamic>{'value': 87.5},
        ),
      );
      handler.handle(
        buildSnapshotEnvelope(
          area: 'area_c',
          revision: 3,
          data: const <String, dynamic>{'value': 31.25},
        ),
      );
      handler.handle(
        buildSnapshotEnvelope(
          area: 'area_d',
          revision: 4,
          data: const <String, dynamic>{'value': 15},
        ),
      );
      handler.handle(
        buildSnapshotEnvelope(
          area: 'area_e',
          revision: 5,
          data: const <String, dynamic>{'value': 10},
        ),
      );

      expect(
        (verify(
                  () => domainA.applySnapshot(
                    envelope: any(named: 'envelope'),
                    payload: captureAny(named: 'payload'),
                  ),
                ).captured.single
                as StateSnapshotPayload)
            .stateArea,
        'area_a',
      );
      expect(
        (verify(
                  () => domainB.applySnapshot(
                    envelope: any(named: 'envelope'),
                    payload: captureAny(named: 'payload'),
                  ),
                ).captured.single
                as StateSnapshotPayload)
            .stateArea,
        'area_b',
      );
      expect(
        (verify(
                  () => domainC.applySnapshot(
                    envelope: any(named: 'envelope'),
                    payload: captureAny(named: 'payload'),
                  ),
                ).captured.single
                as StateSnapshotPayload)
            .stateArea,
        'area_c',
      );
      expect(
        (verify(
                  () => domainD.applySnapshot(
                    envelope: any(named: 'envelope'),
                    payload: captureAny(named: 'payload'),
                  ),
                ).captured.single
                as StateSnapshotPayload)
            .stateArea,
        'area_d',
      );
      expect(
        (verify(
                  () => domainE.applySnapshot(
                    envelope: any(named: 'envelope'),
                    payload: captureAny(named: 'payload'),
                  ),
                ).captured.single
                as StateSnapshotPayload)
            .stateArea,
        'area_e',
      );
      verifyNever(
        () => session.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
    });

    test(
      'Method handle routes a newly registered area without area-specific routing',
      () {
        final MockStateDomainDefinition customDomain =
            MockStateDomainDefinition();
        final MockStateRevisionTracker<Object?> customTracker =
            MockStateRevisionTracker<Object?>();
        when(() => customDomain.stateArea).thenReturn('custom_area');
        when(() => customDomain.tracker).thenReturn(customTracker);
        when(() => customTracker.beginRecovery()).thenAnswer((_) {});
        final IStateMessageHandler customHandler = StateMessageHandler(
          sessionService: session,
          domains: <IStateDomainDefinition<Object?>>[customDomain],
        );
        customHandler.setSubscribedStateAreas(<String>{'custom_area'});

        customHandler.handle(
          buildSnapshotEnvelope(
            area: 'custom_area',
            revision: 1,
            data: const <String, dynamic>{'value': 7},
          ),
        );

        final StateSnapshotPayload payload =
            verify(
                  () => customDomain.applySnapshot(
                    envelope: any(named: 'envelope'),
                    payload: captureAny(named: 'payload'),
                  ),
                ).captured.single
                as StateSnapshotPayload;
        expect(payload.stateArea, 'custom_area');
        verifyNever(
          () => session.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handle ignores state messages for removed areas and resets their trackers',
      () {
        handler.setSubscribedStateAreas(<String>{'area_b'});

        handler.handle(
          buildSnapshotEnvelope(
            area: 'area_a',
            revision: 2,
            data: const <String, dynamic>{'value': 42.5},
          ),
        );
        handler.handle(
          buildEventEnvelope(
            area: 'area_a',
            baseRevision: 1,
            revision: 2,
            data: const <String, dynamic>{'value': 42.5},
          ),
        );

        verify(() => trackerA.resetToNotSubscribed()).called(1);
        verifyNever(
          () => domainA.applySnapshot(
            envelope: any(named: 'envelope'),
            payload: any(named: 'payload'),
          ),
        );
        verifyNever(
          () => domainA.applyEvent(
            envelope: any(named: 'envelope'),
            payload: any(named: 'payload'),
          ),
        );
        verifyNever(
          () => session.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handle routes Event payloads through their registered definition',
      () {
        handler.handle(
          buildEventEnvelope(
            area: 'area_e',
            baseRevision: 1,
            revision: 2,
            data: const <String, dynamic>{'value': 11},
          ),
        );

        final StateEventPayload payload =
            verify(
                  () => domainE.applyEvent(
                    envelope: any(named: 'envelope'),
                    payload: captureAny(named: 'payload'),
                  ),
                ).captured.single
                as StateEventPayload;
        expect(payload.stateArea, 'area_e');
        verifyNever(
          () => session.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test('Method handle rejects an unregistered state area', () {
      handler.handle(
        buildSnapshotEnvelope(
          area: 'unknown_area',
          revision: 1,
          data: const <String, dynamic>{'value': 10},
        ),
      );

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

    test('Method handle rejects an unregistered Event area', () {
      handler.handle(
        buildEventEnvelope(
          area: 'unknown_area',
          baseRevision: 1,
          revision: 2,
          data: const <String, dynamic>{'value': 10},
        ),
      );

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

    test(
      'Method handle reports malformed Event payloads as protocol violations',
      () {
        handler.handle(
          const Envelope(
            messageType: ProtocolMessageType.stateEvent,
            messageId: 'invalid-event',
            sessionId: 'session-1',
            correlationId: null,
            payload: <String, dynamic>{},
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            clientId: null,
          ),
        );

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
      },
    );

    test('Method handle reports non-state messages as protocol violations', () {
      handler.handle(
        const Envelope(
          messageType: ProtocolMessageType.pong,
          messageId: 'pong-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: null,
          playContextId: null,
          clientId: null,
        ),
      );

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

    test(
      'Method handle reports registered-domain Event failures to the session',
      () {
        const DovahLinkProtocolException failure = DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'Event rejected by its domain.',
          retryable: false,
        );
        when(
          () => domainB.applyEvent(
            envelope: any(named: 'envelope'),
            payload: any(named: 'payload'),
          ),
        ).thenThrow(failure);

        handler.handle(
          buildEventEnvelope(
            area: 'area_b',
            baseRevision: 1,
            revision: 2,
            data: const <String, dynamic>{'value': 10},
          ),
        );

        verify(
          () => session.onProtocolViolation(
            failure,
            orphanRetrySafeOperations: false,
          ),
        ).called(1);
      },
    );

    test(
      'Method handle reports registered-domain Snapshot failures to the session',
      () {
        const DovahLinkProtocolException failure = DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'Snapshot rejected by its domain.',
          retryable: false,
        );
        when(
          () => domainB.applySnapshot(
            envelope: any(named: 'envelope'),
            payload: any(named: 'payload'),
          ),
        ).thenThrow(failure);

        handler.handle(
          buildSnapshotEnvelope(
            area: 'area_b',
            revision: 1,
            data: const <String, dynamic>{'value': 10},
          ),
        );

        verify(
          () => session.onProtocolViolation(
            failure,
            orphanRetrySafeOperations: false,
          ),
        ).called(1);
      },
    );

    test(
      'Method handle reports malformed Snapshot payloads as protocol violations',
      () {
        handler.handle(
          const Envelope(
            messageType: ProtocolMessageType.stateSnapshot,
            messageId: 'invalid-snapshot',
            sessionId: 'session-1',
            correlationId: null,
            payload: <String, dynamic>{},
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            clientId: null,
          ),
        );

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
      },
    );
  });

  group('Method setSubscribedStateAreas behaves correctly', () {
    test(
      'Method setSubscribedStateAreas marks newly accepted areas as recovering',
      () {
        clearInteractions(trackerB);
        clearInteractions(trackerA);
        handler.setSubscribedStateAreas(<String>{});
        handler.setSubscribedStateAreas(<String>{'area_b'});
        handler.setSubscribedStateAreas(<String>{'area_b'});

        verify(() => trackerB.beginRecovery()).called(1);
        verifyNever(() => trackerA.beginRecovery());
      },
    );
  });
}
