import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/character_health_state.dart';
import 'mock_state_revision_tracker.dart';

/// Builds one valid Snapshot payload for the registered test area.
/// @param value The current value or explicit unavailable representation.
/// @return A decoded Snapshot payload.
StateSnapshotPayload buildSnapshotPayload(Object? value) =>
    StateSnapshotPayload(
      stateArea: 'character_health',
      revision: 4,
      occurredAt: '2026-09-23T12:00:00Z',
      data: <String, dynamic>{'value': value},
    );

/// Builds one valid Event payload for the registered test area.
/// @param value The complete post-change value.
/// @return A decoded Event payload.
StateEventPayload buildEventPayload(Object? value) => StateEventPayload(
  stateArea: 'character_health',
  baseRevision: 4,
  revision: 5,
  occurredAt: '2026-09-23T12:00:01Z',
  data: <String, dynamic>{'value': value},
);

/// Builds a state envelope with the test authority and play-context identity.
/// @param messageType The Snapshot or Event message type.
/// @return A decoded envelope ready for a domain definition.
Envelope buildStateEnvelope(ProtocolMessageType messageType) => Envelope(
  messageType: messageType,
  messageId: 'state-message-1',
  sessionId: 'session-1',
  correlationId: null,
  payload: const <String, dynamic>{},
  stateAuthorityId: 'authority-1',
  playContextId: 'context-1',
  clientId: null,
);

/// Runs typed state-domain definition behavior tests.
void main() {
  late MockStateRevisionTracker<CharacterHealthState> tracker;
  late StateDomainDefinition<CharacterHealthState> definition;

  setUpAll(() {
    registerFallbackValue(const CharacterHealthState(value: null));
  });

  setUp(() {
    tracker = MockStateRevisionTracker<CharacterHealthState>();
    when(
      () => tracker.applySnapshot(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(true);
    when(
      () => tracker.applyEvent(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        baseRevision: any(named: 'baseRevision'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(StateEventApplyResult.applied);
    definition = StateDomainDefinition<CharacterHealthState>(
      stateArea: 'character_health',
      decode: CharacterHealthState.fromJson,
      tracker: tracker,
      isUnavailable: (CharacterHealthState value) => value.value == null,
      supportsEvents: true,
    );
  });

  group('Method decodeState behaves correctly', () {
    test('Method decodeState returns the typed available value and status', () {
      final ({CharacterHealthState value, bool isUnavailable}) decoded =
          definition.decodeState(const <String, dynamic>{'value': 87.5});

      expect(decoded.value.value, 87.5);
      expect(decoded.isUnavailable, isFalse);
    });

    test('Method decodeState marks an explicit null value unavailable', () {
      final ({CharacterHealthState value, bool isUnavailable}) decoded =
          definition.decodeState(const <String, dynamic>{'value': null});

      expect(decoded.value.value, isNull);
      expect(decoded.isUnavailable, isTrue);
    });

    test(
      'Method decodeState reports malformed typed data as a protocol error',
      () {
        expect(
          () => definition.decodeState(const <String, dynamic>{
            'value': 'not a number',
          }),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException error) => error.code,
                  'code',
                  ProtocolErrorCode.malformedMessage,
                )
                .having(
                  (DovahLinkProtocolException error) => error.retryable,
                  'retryable',
                  isFalse,
                ),
          ),
        );
      },
    );
  });

  group('Method applySnapshot behaves correctly', () {
    test(
      'Method applySnapshot decodes the value and forwards its identity and revision',
      () {
        definition.applySnapshot(
          envelope: buildStateEnvelope(ProtocolMessageType.stateSnapshot),
          payload: buildSnapshotPayload(87.5),
        );

        final CharacterHealthState value =
            verify(
                  () => tracker.applySnapshot(
                    stateAuthorityId: 'authority-1',
                    playContextId: 'context-1',
                    revision: 4,
                    value: captureAny(named: 'value'),
                    isUnavailable: false,
                  ),
                ).captured.single
                as CharacterHealthState;
        expect(value.value, 87.5);
      },
    );

    test(
      'Method applySnapshot marks an explicit null value as unavailable',
      () {
        definition.applySnapshot(
          envelope: buildStateEnvelope(ProtocolMessageType.stateSnapshot),
          payload: buildSnapshotPayload(null),
        );

        final CharacterHealthState value =
            verify(
                  () => tracker.applySnapshot(
                    stateAuthorityId: 'authority-1',
                    playContextId: 'context-1',
                    revision: 4,
                    value: captureAny(named: 'value'),
                    isUnavailable: true,
                  ),
                ).captured.single
                as CharacterHealthState;
        expect(value.value, isNull);
      },
    );

    test(
      'Method applySnapshot rejects malformed typed data as a protocol error',
      () {
        expect(
          () => definition.applySnapshot(
            envelope: buildStateEnvelope(ProtocolMessageType.stateSnapshot),
            payload: buildSnapshotPayload('not a number'),
          ),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(
          () => tracker.applySnapshot(
            stateAuthorityId: any(named: 'stateAuthorityId'),
            playContextId: any(named: 'playContextId'),
            revision: any(named: 'revision'),
            value: any(named: 'value'),
            isUnavailable: any(named: 'isUnavailable'),
          ),
        );
      },
    );
  });

  group('Method applyEvent behaves correctly', () {
    test('Method applyEvent decodes and forwards a supported Event', () {
      definition.applyEvent(
        envelope: buildStateEnvelope(ProtocolMessageType.stateEvent),
        payload: buildEventPayload(91.0),
      );

      final CharacterHealthState value =
          verify(
                () => tracker.applyEvent(
                  stateAuthorityId: 'authority-1',
                  playContextId: 'context-1',
                  baseRevision: 4,
                  revision: 5,
                  value: captureAny(named: 'value'),
                  isUnavailable: false,
                ),
              ).captured.single
              as CharacterHealthState;
      expect(value.value, 91.0);
    });

    test('Method applyEvent marks an explicit null value as unavailable', () {
      definition.applyEvent(
        envelope: buildStateEnvelope(ProtocolMessageType.stateEvent),
        payload: buildEventPayload(null),
      );

      final CharacterHealthState value =
          verify(
                () => tracker.applyEvent(
                  stateAuthorityId: 'authority-1',
                  playContextId: 'context-1',
                  baseRevision: 4,
                  revision: 5,
                  value: captureAny(named: 'value'),
                  isUnavailable: true,
                ),
              ).captured.single
              as CharacterHealthState;
      expect(value.value, isNull);
    });

    test(
      'Method applyEvent rejects malformed typed data as a protocol error',
      () {
        expect(
          () => definition.applyEvent(
            envelope: buildStateEnvelope(ProtocolMessageType.stateEvent),
            payload: buildEventPayload('not a number'),
          ),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(
          () => tracker.applyEvent(
            stateAuthorityId: any(named: 'stateAuthorityId'),
            playContextId: any(named: 'playContextId'),
            baseRevision: any(named: 'baseRevision'),
            revision: any(named: 'revision'),
            value: any(named: 'value'),
            isUnavailable: any(named: 'isUnavailable'),
          ),
        );
      },
    );

    test(
      'Method applyEvent rejects an Event when the domain is Snapshot-only',
      () {
        final StateDomainDefinition<CharacterHealthState> snapshotOnly =
            StateDomainDefinition<CharacterHealthState>(
              stateArea: 'character_health',
              decode: CharacterHealthState.fromJson,
              tracker: tracker,
              isUnavailable: (CharacterHealthState value) =>
                  value.value == null,
            );

        expect(
          () => snapshotOnly.applyEvent(
            envelope: buildStateEnvelope(ProtocolMessageType.stateEvent),
            payload: buildEventPayload(91.0),
          ),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(
          () => tracker.applyEvent(
            stateAuthorityId: any(named: 'stateAuthorityId'),
            playContextId: any(named: 'playContextId'),
            baseRevision: any(named: 'baseRevision'),
            revision: any(named: 'revision'),
            value: any(named: 'value'),
            isUnavailable: any(named: 'isUnavailable'),
          ),
        );
      },
    );
  });
}
