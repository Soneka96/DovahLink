import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/tracked_quests_state_module.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import 'package:dovahlink_client_sdk/src/state/tracked_quests_state.dart';

/// Builds one available tracked-quest state-area value.
/// @param questId The runtime quest FormID.
/// @param title The localized quest title.
/// @param objectives The current objective objects.
/// @return The complete state-area data object.
Map<String, Object?> _trackedQuestsData({
  required int questId,
  required String title,
  required List<Map<String, Object?>> objectives,
}) => <String, Object?>{
  'value': <String, Object?>{
    'quests': <Map<String, Object?>>[
      <String, Object?>{
        'questId': questId,
        'title': title,
        'type': 8,
        'objectives': objectives,
      },
    ],
  },
};

/// Builds one Snapshot envelope for a tracked-quests baseline.
/// @param authority The state authority identity.
/// @param context The play-context identity.
/// @return The Snapshot envelope with the requested identities.
Envelope _buildSnapshotEnvelope({
  String authority = 'authority-1',
  String context = 'context-1',
}) => Envelope(
  messageType: ProtocolMessageType.stateSnapshot,
  messageId: 'snapshot-$authority-$context',
  sessionId: 'session-1',
  correlationId: null,
  payload: const <String, dynamic>{},
  stateAuthorityId: authority,
  playContextId: context,
  clientId: null,
);

/// Builds one complete Snapshot payload.
/// @param revision The state-area revision.
/// @param data The complete available or unavailable state data.
/// @return The Snapshot payload for tracked quests.
StateSnapshotPayload _buildSnapshot({
  required int revision,
  required Map<String, Object?> data,
}) => StateSnapshotPayload(
  stateArea: 'tracked_quests',
  revision: revision,
  occurredAt: '2026-10-05T12:00:00Z',
  data: data,
);

/// Tests tracked-quests registration and Snapshot synchronization behavior.
void main() {
  late ITrackedQuestsStateModule module;

  setUp(() {
    module = TrackedQuestsStateModule();
  });

  group('Property domain behaves correctly', () {
    test('Property domain registers the Snapshot-only tracked_quests area', () {
      expect(module.domain.stateArea, 'tracked_quests');
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
      final Envelope envelope = _buildSnapshotEnvelope();
      const StateEventPayload event = StateEventPayload(
        stateArea: 'tracked_quests',
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
      'Method applySnapshot synchronizes full collections, empty truth, unavailable, and recovery states',
      () async {
        final Envelope envelope = _buildSnapshotEnvelope();
        module.domain.applySnapshot(
          envelope: envelope,
          payload: _buildSnapshot(
            revision: 1,
            data: <String, Object?>{
              'value': <String, Object?>{
                'quests': <Map<String, Object?>>[
                  <String, Object?>{
                    'questId': 10,
                    'title': 'Localized quest',
                    'type': 8,
                    'objectives': <Map<String, Object?>>[
                      <String, Object?>{
                        'index': 20,
                        'instanceId': 5,
                        'text': 'Authored objective',
                        'state': 'dormant',
                      },
                      <String, Object?>{
                        'index': 30,
                        'instanceId': 5,
                        'text': null,
                        'state': 'failed_and_displayed',
                      },
                    ],
                  },
                  <String, Object?>{
                    'questId': 20,
                    'title': 'Second quest',
                    'type': 254,
                    'objectives': <Map<String, Object?>>[],
                  },
                ],
              },
            },
          ),
        );
        StateSynchronization<TrackedQuestsState?> state =
            await module.changes.first;
        expect(state.status, DovahLinkStateStatus.synchronized);
        expect(state.value?.quests.map((quest) => quest.questId), [10, 20]);
        expect(state.value?.quests.first.title, 'Localized quest');
        expect(state.value?.quests.first.objectives, hasLength(2));
        expect(state.stateAuthorityId, 'authority-1');
        expect(state.playContextId, 'context-1');
        expect(state.revision, 1);

        module.domain.tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 1,
          revision: 3,
          value: state.value,
          isUnavailable: false,
        );
        expect((await module.changes.first).status, DovahLinkStateStatus.stale);

        module.domain.tracker.beginRecovery();
        expect(
          (await module.changes.first).status,
          DovahLinkStateStatus.recovering,
        );
        module.domain.applySnapshot(
          envelope: envelope,
          payload: _buildSnapshot(
            revision: 2,
            data: const <String, Object?>{
              'value': <String, Object?>{'quests': <Object>[]},
            },
          ),
        );
        state = await module.changes.first;
        expect(state.status, DovahLinkStateStatus.synchronized);
        expect(state.value?.quests, isEmpty);
        expect(state.revision, 2);

        module.domain.applySnapshot(
          envelope: envelope,
          payload: const StateSnapshotPayload(
            stateArea: 'tracked_quests',
            revision: 3,
            occurredAt: '2026-10-05T12:00:03Z',
            data: <String, dynamic>{'value': null},
          ),
        );
        state = await module.changes.first;
        expect(state.status, DovahLinkStateStatus.unavailable);
        expect(state.value, isNull);
        expect(state.revision, 3);

        module.domain.tracker.beginRecovery();
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
        module.domain.applySnapshot(
          envelope: _buildSnapshotEnvelope(),
          payload: _buildSnapshot(
            revision: 8,
            data: _trackedQuestsData(
              questId: 10,
              title: 'First quest',
              objectives: <Map<String, Object?>>[],
            ),
          ),
        );
        module.domain.applySnapshot(
          envelope: _buildSnapshotEnvelope(authority: 'authority-2'),
          payload: _buildSnapshot(
            revision: 1,
            data: _trackedQuestsData(
              questId: 20,
              title: 'New authority quest',
              objectives: <Map<String, Object?>>[],
            ),
          ),
        );
        StateSynchronization<TrackedQuestsState?> state =
            await module.changes.first;
        expect(state.stateAuthorityId, 'authority-2');
        expect(state.revision, 1);

        module.domain.applySnapshot(
          envelope: _buildSnapshotEnvelope(
            authority: 'authority-2',
            context: 'context-2',
          ),
          payload: _buildSnapshot(
            revision: 1,
            data: _trackedQuestsData(
              questId: 30,
              title: 'New play context quest',
              objectives: <Map<String, Object?>>[],
            ),
          ),
        );
        state = await module.changes.first;
        expect(state.stateAuthorityId, 'authority-2');
        expect(state.playContextId, 'context-2');
        expect(state.value?.quests.single.title, 'New play context quest');
        expect(state.revision, 1);
      },
    );

    test(
      'Method applySnapshot rejects malformed data without replacing the prior state',
      () async {
        final Envelope envelope = _buildSnapshotEnvelope();
        module.domain.applySnapshot(
          envelope: envelope,
          payload: _buildSnapshot(
            revision: 1,
            data: _trackedQuestsData(
              questId: 10,
              title: 'Valid quest',
              objectives: <Map<String, Object?>>[],
            ),
          ),
        );
        final StateSynchronization<TrackedQuestsState?> previous =
            await module.changes.first;

        expect(
          () => module.domain.applySnapshot(
            envelope: envelope,
            payload: _buildSnapshot(
              revision: 2,
              data: const <String, Object?>{
                'value': <String, Object?>{'quests': 'malformed'},
              },
            ),
          ),
          throwsA(isA<DovahLinkProtocolException>()),
        );
        expect(module.domain.tracker.current, same(previous));
      },
    );
  });

  group('Method resetToNotSubscribed behaves correctly', () {
    test(
      'Method resetToNotSubscribed clears the previous quest baseline',
      () async {
        module.domain.applySnapshot(
          envelope: _buildSnapshotEnvelope(),
          payload: _buildSnapshot(
            revision: 1,
            data: _trackedQuestsData(
              questId: 10,
              title: 'Quest',
              objectives: <Map<String, Object?>>[],
            ),
          ),
        );

        module.domain.tracker.resetToNotSubscribed();
        final StateSynchronization<TrackedQuestsState?> state =
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
