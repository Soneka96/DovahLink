import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/game_time_state_module.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/game_time_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Builds one complete Skyrim calendar state-area data object.
/// @param year The game year.
/// @param month The public one-based month.
/// @param monthName The localized month name.
/// @param day The Skyrim calendar day.
/// @param hour The whole game hour.
/// @param minute The derived minute.
/// @return The state-area data object.
Map<String, Object?> _gameTimeData({
  int year = 201,
  int month = 9,
  String monthName = 'Hearthfire',
  int day = 17,
  int hour = 17,
  int minute = 45,
}) => <String, Object?>{
  'value': <String, Object?>{
    'year': year,
    'month': month,
    'monthName': monthName,
    'day': day,
    'hour': hour,
    'minute': minute,
  },
};

/// Tests the independent game-time registration and Snapshot synchronization path.
void main() {
  late IGameTimeStateModule module;

  setUp(() {
    module = GameTimeStateModule();
  });

  group('Property domain behaves correctly', () {
    test('Property domain registers the Snapshot-only game_time area', () {
      expect(module.domain.stateArea, 'game_time');
      expect(
        module.domain.tracker.current.status,
        DovahLinkStateStatus.notSubscribed,
      );
    });

    test(
      'Property changes replays notSubscribed synchronization state',
      () async {
        expect(
          (await module.changes.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );

    test('Property domain rejects Events for this Snapshot-only area', () {
      const Envelope envelope = Envelope(
        messageType: ProtocolMessageType.stateEvent,
        messageId: 'event-1',
        sessionId: 'session-1',
        correlationId: null,
        payload: <String, dynamic>{},
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        clientId: null,
      );
      const StateEventPayload event = StateEventPayload(
        stateArea: 'game_time',
        baseRevision: 1,
        revision: 2,
        occurredAt: '2026-10-05T12:00:01Z',
        data: <String, dynamic>{'value': null},
      );

      expect(
        () => module.domain.applyEvent(envelope: envelope, payload: event),
        throwsA(isA<DovahLinkProtocolException>()),
      );
    });
  });

  group('Method applySnapshot behaves correctly', () {
    test(
      'Method applySnapshot synchronizes, becomes stale, recovers, and preserves unavailability',
      () async {
        const Envelope envelope = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          clientId: null,
        );

        module.domain.applySnapshot(
          envelope: envelope,
          payload: StateSnapshotPayload(
            stateArea: 'game_time',
            revision: 1,
            occurredAt: '2026-10-05T12:00:00Z',
            data: _gameTimeData(),
          ),
        );
        StateSynchronization<GameTimeState?> state = await module.changes.first;
        expect(state.status, DovahLinkStateStatus.synchronized);
        expect(state.value?.monthName, 'Hearthfire');
        expect(state.value?.minute, 45);
        expect(state.stateAuthorityId, 'authority-1');
        expect(state.playContextId, 'context-1');
        expect(state.revision, 1);

        final GameTimeState previousValue =
            state.value ??
            (throw StateError(
              'The available Snapshot should contain game time.',
            ));
        module.domain.tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 1,
          revision: 3,
          value: previousValue,
          isUnavailable: false,
        );
        state = await module.changes.first;
        expect(state.status, DovahLinkStateStatus.stale);
        expect(state.revision, 1);

        module.domain.tracker.beginRecovery();
        expect(
          (await module.changes.first).status,
          DovahLinkStateStatus.recovering,
        );
        module.domain.applySnapshot(
          envelope: envelope,
          payload: const StateSnapshotPayload(
            stateArea: 'game_time',
            revision: 2,
            occurredAt: '2026-10-05T12:00:02Z',
            data: <String, dynamic>{'value': null},
          ),
        );
        state = await module.changes.first;
        expect(state.status, DovahLinkStateStatus.unavailable);
        expect(state.value?.monthName, 'Hearthfire');
        expect(state.value?.minute, 45);
        expect(state.revision, 2);

        module.domain.tracker.failRecovery();
        expect(
          (await module.changes.first).status,
          DovahLinkStateStatus.failed,
        );
      },
    );

    test(
      'Method applySnapshot replaces baselines after authority and play-context changes',
      () async {
        const Envelope first = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          clientId: null,
        );
        const Envelope newAuthority = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-2',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-2',
          playContextId: 'context-1',
          clientId: null,
        );
        const Envelope newContext = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-3',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-2',
          playContextId: 'context-2',
          clientId: null,
        );

        module.domain.applySnapshot(
          envelope: first,
          payload: StateSnapshotPayload(
            stateArea: 'game_time',
            revision: 8,
            occurredAt: '2026-10-05T12:00:00Z',
            data: _gameTimeData(),
          ),
        );
        module.domain.applySnapshot(
          envelope: newAuthority,
          payload: StateSnapshotPayload(
            stateArea: 'game_time',
            revision: 1,
            occurredAt: '2026-10-05T12:00:01Z',
            data: _gameTimeData(month: 10, monthName: 'Frostfall'),
          ),
        );
        StateSynchronization<GameTimeState?> state = await module.changes.first;
        expect(state.stateAuthorityId, 'authority-2');
        expect(state.playContextId, 'context-1');
        expect(state.revision, 1);

        module.domain.applySnapshot(
          envelope: newContext,
          payload: StateSnapshotPayload(
            stateArea: 'game_time',
            revision: 1,
            occurredAt: '2026-10-05T12:00:02Z',
            data: _gameTimeData(month: 11, monthName: 'Sun’s Dusk'),
          ),
        );
        state = await module.changes.first;
        expect(state.value?.month, 11);
        expect(state.value?.monthName, 'Sun’s Dusk');
        expect(state.stateAuthorityId, 'authority-2');
        expect(state.playContextId, 'context-2');
        expect(state.revision, 1);
      },
    );

    test(
      'Method applySnapshot rejects malformed values without changing synchronization state',
      () {
        const Envelope envelope = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          clientId: null,
        );

        expect(
          () => module.domain.applySnapshot(
            envelope: envelope,
            payload: const StateSnapshotPayload(
              stateArea: 'game_time',
              revision: 1,
              occurredAt: '2026-10-05T12:00:00Z',
              data: <String, dynamic>{
                'value': <String, dynamic>{'month': 9},
              },
            ),
          ),
          throwsA(isA<DovahLinkProtocolException>()),
        );
        expect(
          module.domain.tracker.current.status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );
  });

  group('Method resetToNotSubscribed behaves correctly', () {
    test(
      'Method resetToNotSubscribed clears a previous calendar baseline',
      () async {
        const Envelope envelope = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          clientId: null,
        );
        module.domain.applySnapshot(
          envelope: envelope,
          payload: StateSnapshotPayload(
            stateArea: 'game_time',
            revision: 1,
            occurredAt: '2026-10-05T12:00:00Z',
            data: _gameTimeData(),
          ),
        );

        module.domain.tracker.resetToNotSubscribed();
        final StateSynchronization<GameTimeState?> state =
            await module.changes.first;
        expect(state.status, DovahLinkStateStatus.notSubscribed);
        expect(state.value, isNull);
        expect(state.stateAuthorityId, isNull);
        expect(state.playContextId, isNull);
        expect(state.revision, isNull);
      },
    );
  });
}
