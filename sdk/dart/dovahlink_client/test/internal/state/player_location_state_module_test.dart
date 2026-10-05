import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/player_location_state_module.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/player_location_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Builds one complete protocol data object for a player location.
/// @param cellId The current cell FormID.
/// @param cellKind The cell classification.
/// @param cellName The optional cell name.
/// @param locationId The optional selected location FormID.
/// @param locationName The optional selected location name.
/// @param worldspaceId The optional worldspace FormID.
/// @param worldspaceName The optional worldspace name.
/// @return The state-area `data` object.
Map<String, Object?> _locationData({
  int cellId = 10,
  String cellKind = 'exterior',
  String? cellName = 'WhiterunWorld',
  int? locationId = 20,
  String? locationName = 'Whiterun',
  int? worldspaceId = 30,
  String? worldspaceName = 'Skyrim',
}) => <String, Object?>{
  'value': <String, Object?>{
    'cellId': cellId,
    'cellKind': cellKind,
    'cellName': cellName,
    'locationId': locationId,
    'locationName': locationName,
    'worldspaceId': worldspaceId,
    'worldspaceName': worldspaceName,
  },
};

/// Tests the player-location tracker registration and Snapshot synchronization path.
void main() {
  late IPlayerLocationStateModule module;

  setUp(() {
    module = PlayerLocationStateModule();
  });

  group('Property domain behaves correctly', () {
    test(
      'Property domain registers the Snapshot-only player_location area',
      () {
        expect(module.domain.stateArea, 'player_location');
        expect(
          module.domain.tracker.current.status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );

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
        stateArea: 'player_location',
        baseRevision: 1,
        revision: 2,
        occurredAt: '2026-10-04T12:00:01Z',
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
      'Method applySnapshot synchronizes, goes stale, recovers, and distinguishes unavailable state',
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
          payload: const StateSnapshotPayload(
            stateArea: 'player_location',
            revision: 1,
            occurredAt: '2026-10-04T12:00:00Z',
            data: <String, dynamic>{
              'value': <String, dynamic>{
                'cellId': 10,
                'cellKind': 'exterior',
                'cellName': 'WhiterunWorld',
                'locationId': 20,
                'locationName': 'Whiterun',
                'worldspaceId': 30,
                'worldspaceName': 'Skyrim',
              },
            },
          ),
        );

        StateSynchronization<PlayerLocationState?> state =
            await module.changes.first;
        expect(state.status, DovahLinkStateStatus.synchronized);
        expect(state.value?.locationName, 'Whiterun');
        expect(state.stateAuthorityId, 'authority-1');
        expect(state.playContextId, 'context-1');
        expect(state.revision, 1);
        final PlayerLocationState previousValue =
            state.value ??
            (throw StateError(
              'The available Snapshot should include a location.',
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
        state = await module.changes.first;
        expect(state.status, DovahLinkStateStatus.recovering);

        module.domain.applySnapshot(
          envelope: envelope,
          payload: const StateSnapshotPayload(
            stateArea: 'player_location',
            revision: 2,
            occurredAt: '2026-10-04T12:00:02Z',
            data: <String, dynamic>{'value': null},
          ),
        );
        state = await module.changes.first;
        expect(state.status, DovahLinkStateStatus.unavailable);
        expect(state.value, isNull);
        expect(state.revision, 2);

        module.domain.tracker.failRecovery();
        expect(
          (await module.changes.first).status,
          DovahLinkStateStatus.failed,
        );
      },
    );

    test(
      'Method applySnapshot accepts a new authority and play context as a fresh baseline',
      () async {
        const Envelope firstEnvelope = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          clientId: null,
        );
        const Envelope newEnvelope = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-2',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-2',
          playContextId: 'context-1',
          clientId: null,
        );
        const Envelope newContextEnvelope = Envelope(
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
          envelope: firstEnvelope,
          payload: StateSnapshotPayload(
            stateArea: 'player_location',
            revision: 5,
            occurredAt: '2026-10-04T12:00:00Z',
            data: _locationData(),
          ),
        );
        module.domain.applySnapshot(
          envelope: newEnvelope,
          payload: StateSnapshotPayload(
            stateArea: 'player_location',
            revision: 1,
            occurredAt: '2026-10-04T12:00:01Z',
            data: _locationData(cellId: 11, locationName: 'Rorikstead'),
          ),
        );
        StateSynchronization<PlayerLocationState?> state =
            await module.changes.first;
        expect(state.stateAuthorityId, 'authority-2');
        expect(state.playContextId, 'context-1');
        expect(state.revision, 1);

        module.domain.applySnapshot(
          envelope: newContextEnvelope,
          payload: StateSnapshotPayload(
            stateArea: 'player_location',
            revision: 1,
            occurredAt: '2026-10-04T12:00:02Z',
            data: _locationData(cellId: 12, locationName: 'Riverwood'),
          ),
        );

        state = await module.changes.first;
        expect(state.status, DovahLinkStateStatus.synchronized);
        expect(state.value?.cellId, 12);
        expect(state.value?.locationName, 'Riverwood');
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
              stateArea: 'player_location',
              revision: 1,
              occurredAt: '2026-10-04T12:00:00Z',
              data: <String, dynamic>{
                'value': <String, dynamic>{'cellId': 1},
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
      'Method resetToNotSubscribed clears a previous synchronized baseline',
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
            stateArea: 'player_location',
            revision: 1,
            occurredAt: '2026-10-04T12:00:00Z',
            data: _locationData(),
          ),
        );

        module.domain.tracker.resetToNotSubscribed();

        final StateSynchronization<PlayerLocationState?> state =
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
