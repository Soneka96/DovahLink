import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/character_health_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_magicka_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_stamina_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_xp_state.dart';
import 'mock_session_service.dart';
import 'mock_state_revision_tracker.dart';

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

/// Builds a handler over mock domain revision trackers.
/// @param session The session lifecycle dependency.
/// @param experience The character experience tracker.
/// @param health The character health tracker.
/// @param magicka The character magicka tracker.
/// @param stamina The character stamina tracker.
/// @param level The character level tracker.
/// @return The state-message handler under test.
IStateMessageHandler buildStateMessageHandler({
  required MockSessionService session,
  required MockStateRevisionTracker<CharacterXpState> experience,
  required MockStateRevisionTracker<CharacterHealthState> health,
  required MockStateRevisionTracker<CharacterMagickaState> magicka,
  required MockStateRevisionTracker<CharacterStaminaState> stamina,
  required MockStateRevisionTracker<CharacterLevelState> level,
}) => StateMessageHandler(
  sessionService: session,
  characterXp: experience,
  characterHealth: health,
  characterMagicka: magicka,
  characterStamina: stamina,
  characterLevel: level,
);

/// Runs state-message-handler behavior tests.
void main() {
  late MockSessionService session;
  late MockStateRevisionTracker<CharacterXpState> experience;
  late MockStateRevisionTracker<CharacterHealthState> health;
  late MockStateRevisionTracker<CharacterMagickaState> magicka;
  late MockStateRevisionTracker<CharacterStaminaState> stamina;
  late MockStateRevisionTracker<CharacterLevelState> level;
  late IStateMessageHandler handler;

  setUpAll(() {
    registerFallbackValue(Exception('fallback for any()'));
    registerFallbackValue(const CharacterXpState(value: null));
    registerFallbackValue(const CharacterHealthState(value: null));
    registerFallbackValue(const CharacterMagickaState(value: null));
    registerFallbackValue(const CharacterStaminaState(value: null));
    registerFallbackValue(const CharacterLevelState(value: null));
  });

  setUp(() {
    session = MockSessionService();
    experience = MockStateRevisionTracker<CharacterXpState>();
    health = MockStateRevisionTracker<CharacterHealthState>();
    magicka = MockStateRevisionTracker<CharacterMagickaState>();
    stamina = MockStateRevisionTracker<CharacterStaminaState>();
    level = MockStateRevisionTracker<CharacterLevelState>();
    when(
      () => experience.applySnapshot(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(true);
    when(
      () => health.applySnapshot(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(true);
    when(
      () => magicka.applySnapshot(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(true);
    when(
      () => stamina.applySnapshot(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(true);
    when(
      () => level.applySnapshot(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(true);
    when(
      () => level.applyEvent(
        stateAuthorityId: any(named: 'stateAuthorityId'),
        playContextId: any(named: 'playContextId'),
        baseRevision: any(named: 'baseRevision'),
        revision: any(named: 'revision'),
        value: any(named: 'value'),
        isUnavailable: any(named: 'isUnavailable'),
      ),
    ).thenReturn(StateEventApplyResult.applied);
    when(
      () => session.onProtocolViolation(
        any(),
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
      ),
    ).thenAnswer((_) {});
    handler = buildStateMessageHandler(
      session: session,
      experience: experience,
      health: health,
      magicka: magicka,
      stamina: stamina,
      level: level,
    );
  });

  group('Method handle behaves correctly', () {
    test('Method handle decodes each registered Snapshot area', () {
      handler.handle(
        buildSnapshotEnvelope(
          area: 'character_xp',
          revision: 1,
          data: <String, dynamic>{'value': 42.5},
        ),
      );
      handler.handle(
        buildSnapshotEnvelope(
          area: 'character_health',
          revision: 2,
          data: <String, dynamic>{'value': 87.5},
        ),
      );
      handler.handle(
        buildSnapshotEnvelope(
          area: 'character_magicka',
          revision: 3,
          data: <String, dynamic>{'value': 31.25},
        ),
      );
      handler.handle(
        buildSnapshotEnvelope(
          area: 'character_stamina',
          revision: 4,
          data: <String, dynamic>{'value': 15},
        ),
      );
      handler.handle(
        buildSnapshotEnvelope(
          area: 'character_level',
          revision: 5,
          data: <String, dynamic>{'value': 10},
        ),
      );

      expect(
        (verify(
                  () => experience.applySnapshot(
                    stateAuthorityId: 'authority-1',
                    playContextId: 'context-1',
                    revision: 1,
                    value: captureAny(named: 'value'),
                    isUnavailable: false,
                  ),
                ).captured.single
                as CharacterXpState)
            .value,
        42.5,
      );
      expect(
        (verify(
                  () => health.applySnapshot(
                    stateAuthorityId: 'authority-1',
                    playContextId: 'context-1',
                    revision: 2,
                    value: captureAny(named: 'value'),
                    isUnavailable: false,
                  ),
                ).captured.single
                as CharacterHealthState)
            .value,
        87.5,
      );
      expect(
        (verify(
                  () => magicka.applySnapshot(
                    stateAuthorityId: 'authority-1',
                    playContextId: 'context-1',
                    revision: 3,
                    value: captureAny(named: 'value'),
                    isUnavailable: false,
                  ),
                ).captured.single
                as CharacterMagickaState)
            .value,
        31.25,
      );
      expect(
        (verify(
                  () => stamina.applySnapshot(
                    stateAuthorityId: 'authority-1',
                    playContextId: 'context-1',
                    revision: 4,
                    value: captureAny(named: 'value'),
                    isUnavailable: false,
                  ),
                ).captured.single
                as CharacterStaminaState)
            .value,
        15.0,
      );
      expect(
        (verify(
                  () => level.applySnapshot(
                    stateAuthorityId: 'authority-1',
                    playContextId: 'context-1',
                    revision: 5,
                    value: captureAny(named: 'value'),
                    isUnavailable: false,
                  ),
                ).captured.single
                as CharacterLevelState)
            .value,
        10,
      );
    });

    test('Method handle preserves unavailable state as an explicit null', () {
      handler.handle(
        buildSnapshotEnvelope(
          area: 'character_health',
          revision: 1,
          data: <String, dynamic>{'value': null},
        ),
      );

      final CharacterHealthState state =
          verify(
                () => health.applySnapshot(
                  stateAuthorityId: 'authority-1',
                  playContextId: 'context-1',
                  revision: 1,
                  value: captureAny(named: 'value'),
                  isUnavailable: true,
                ),
              ).captured.single
              as CharacterHealthState;
      expect(state.value, isNull);
    });

    test(
      'Method handle applies a level Event through the revision tracker',
      () {
        handler.handle(
          buildEventEnvelope(
            area: 'character_level',
            baseRevision: 1,
            revision: 2,
            data: <String, dynamic>{'value': 11},
          ),
        );

        final CharacterLevelState state =
            verify(
                  () => level.applyEvent(
                    stateAuthorityId: 'authority-1',
                    playContextId: 'context-1',
                    baseRevision: 1,
                    revision: 2,
                    value: captureAny(named: 'value'),
                    isUnavailable: false,
                  ),
                ).captured.single
                as CharacterLevelState;
        expect(state.value, 11);
      },
    );

    test('Method handle rejects an Event for a Snapshot-only state area', () {
      handler.handle(
        buildEventEnvelope(
          area: 'character_xp',
          baseRevision: 1,
          revision: 2,
          data: <String, dynamic>{'value': 11.0},
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
      verifyNever(
        () => experience.applyEvent(
          stateAuthorityId: any(named: 'stateAuthorityId'),
          playContextId: any(named: 'playContextId'),
          baseRevision: any(named: 'baseRevision'),
          revision: any(named: 'revision'),
          value: any(named: 'value'),
          isUnavailable: any(named: 'isUnavailable'),
        ),
      );
    });

    test('Method handle rejects an unregistered state area', () {
      handler.handle(
        buildSnapshotEnvelope(
          area: 'unknown_area',
          revision: 1,
          data: <String, dynamic>{'value': 10},
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

    test('Method handle fails closed on malformed registered state data', () {
      handler.handle(
        buildSnapshotEnvelope(
          area: 'character_xp',
          revision: 1,
          data: <String, dynamic>{'value': 'not a number'},
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
  });
}
