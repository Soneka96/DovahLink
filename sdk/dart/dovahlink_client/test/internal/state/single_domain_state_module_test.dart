import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/single_domain_state_module.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import '../../fixtures/fixtures.dart';

/// Decodes the nullable numeric value used by this generic module test.
/// @param json The arbitrary synthetic-domain payload.
/// @return The numeric value, or null for explicit unavailability.
num? _decodeSyntheticValue(JsonMap json) => json['value'] as num?;

/// Decodes a required numeric value for this generic module test.
/// @param json The arbitrary synthetic-domain payload.
/// @return The numeric value.
/// @throws [ProtocolFormatException] when the value is not numeric.
num _decodeStrictValue(JsonMap json) {
  final Object? value = json['value'];
  if (value is num) {
    return value;
  }
  throw const ProtocolFormatException('Expected a numeric test value.');
}

/// Builds a synthetic Snapshot payload.
/// @param revision The authoritative revision.
/// @param value The value or explicit unavailable representation.
/// @return A decoded Snapshot payload.
StateSnapshotPayload _buildSnapshot(int revision, Object? value) =>
    StateSnapshotPayload(
      stateArea: 'synthetic_area',
      revision: revision,
      occurredAt: '2026-10-05T12:00:00Z',
      data: <String, dynamic>{'value': value},
    );

/// Builds a synthetic Event payload.
/// @param baseRevision The revision the Event applies to.
/// @param revision The resulting revision.
/// @param value The complete post-change value.
/// @return A decoded Event payload.
StateEventPayload _buildEvent(int baseRevision, int revision, Object? value) =>
    StateEventPayload(
      stateArea: 'synthetic_area',
      baseRevision: baseRevision,
      revision: revision,
      occurredAt: '2026-10-05T12:00:01Z',
      data: <String, dynamic>{'value': value},
    );

/// Builds a state envelope carrying the shared test identity.
/// @param messageType The Snapshot or Event message type.
/// @return The envelope.
Envelope _buildEnvelope(ProtocolMessageType messageType) =>
    Fixtures.buildEnvelope(
      messageType: messageType,
      stateAuthorityId: 'authority-1',
      playContextId: 'context-1',
    );

/// Runs generic single-domain module tests over a synthetic nullable state domain.
void main() {
  late SingleDomainStateModule<num?> module;

  setUp(() {
    module = SingleDomainStateModule<num?>(
      stateArea: 'synthetic_area',
      decode: _decodeSyntheticValue,
      isUnavailable: (num? value) => value == null,
    );
  });

  group('Property domain behaves correctly', () {
    test('Property domain registers the supplied Snapshot-only area', () {
      expect(module.domain.stateArea, 'synthetic_area');
      expect(module.domain.supportsEvents, isFalse);
      expect(
        module.domain.tracker.current.status,
        DovahLinkStateStatus.notSubscribed,
      );
    });

    test('Property domain carries a requested Event capability', () {
      final SingleDomainStateModule<num?> eventModule =
          SingleDomainStateModule<num?>(
            stateArea: 'synthetic_event_area',
            decode: _decodeSyntheticValue,
            isUnavailable: (num? value) => value == null,
            supportsEvents: true,
          );

      expect(eventModule.domain.supportsEvents, isTrue);
    });

    test('Property domain rejects Events for a Snapshot-only area', () {
      expect(
        () => module.domain.applyEvent(
          envelope: _buildEnvelope(ProtocolMessageType.stateEvent),
          payload: _buildEvent(1, 2, 5),
        ),
        throwsA(isA<DovahLinkProtocolException>()),
      );
    });
  });

  group('Property changes behaves correctly', () {
    test('Property changes replays notSubscribed state', () async {
      expect(
        (await module.changes.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
    });

    test(
      'Property changes exposes the definition tracker stream rather than a copy',
      () async {
        module.domain.applySnapshot(
          envelope: _buildEnvelope(ProtocolMessageType.stateSnapshot),
          payload: _buildSnapshot(1, 12.5),
        );

        final StateSynchronization<num?> viaModule = await module.changes.first;
        final StateSynchronization<num?> viaTracker =
            await module.domain.tracker.changes.first;

        expect(viaModule.status, DovahLinkStateStatus.synchronized);
        expect(viaModule.value, 12.5);
        expect(viaModule.revision, 1);
        expect(viaModule, same(module.domain.tracker.current));
        expect(viaTracker, same(viaModule));
      },
    );

    test('Property changes publishes nullable unavailability', () async {
      module.domain.applySnapshot(
        envelope: _buildEnvelope(ProtocolMessageType.stateSnapshot),
        payload: _buildSnapshot(1, null),
      );

      final StateSynchronization<num?> state = await module.changes.first;

      expect(state.status, DovahLinkStateStatus.unavailable);
      expect(state.value, isNull);
    });

    test('Property changes does not share state between modules', () async {
      final SingleDomainStateModule<num?> other = SingleDomainStateModule<num?>(
        stateArea: 'synthetic_other_area',
        decode: _decodeSyntheticValue,
        isUnavailable: (num? value) => value == null,
      );

      module.domain.applySnapshot(
        envelope: _buildEnvelope(ProtocolMessageType.stateSnapshot),
        payload: _buildSnapshot(1, 7),
      );

      expect(
        (await other.changes.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(other.domain.tracker, isNot(same(module.domain.tracker)));
    });
  });

  group('Method domain applySnapshot behaves correctly', () {
    test(
      'Method applySnapshot uses the supplied unavailability rule',
      () async {
        final SingleDomainStateModule<num?> zeroIsUnavailable =
            SingleDomainStateModule<num?>(
              stateArea: 'synthetic_area',
              decode: _decodeSyntheticValue,
              isUnavailable: (num? value) => value == 0,
            );

        zeroIsUnavailable.domain.applySnapshot(
          envelope: _buildEnvelope(ProtocolMessageType.stateSnapshot),
          payload: _buildSnapshot(1, 0),
        );

        expect(
          (await zeroIsUnavailable.changes.first).status,
          DovahLinkStateStatus.unavailable,
        );
      },
    );

    test('Method applySnapshot publishes the envelope identity', () async {
      module.domain.applySnapshot(
        envelope: _buildEnvelope(ProtocolMessageType.stateSnapshot),
        payload: _buildSnapshot(3, 1),
      );

      final StateSynchronization<num?> state = await module.changes.first;
      expect(state.stateAuthorityId, 'authority-1');
      expect(state.playContextId, 'context-1');
      expect(state.revision, 3);
    });

    test(
      'Method applySnapshot leaves state unchanged for malformed data',
      () async {
        final SingleDomainStateModule<num> strict =
            SingleDomainStateModule<num>(
              stateArea: 'synthetic_area',
              decode: _decodeStrictValue,
              isUnavailable: (num value) => false,
            );

        expect(
          () => strict.domain.applySnapshot(
            envelope: _buildEnvelope(ProtocolMessageType.stateSnapshot),
            payload: _buildSnapshot(1, 'not a number'),
          ),
          throwsA(isA<DovahLinkProtocolException>()),
        );
        expect(
          (await strict.changes.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );
  });

  group('Method domain applyEvent behaves correctly', () {
    test('Method applyEvent advances an Event-capable module', () async {
      final SingleDomainStateModule<num?> eventModule =
          SingleDomainStateModule<num?>(
            stateArea: 'synthetic_area',
            decode: _decodeSyntheticValue,
            isUnavailable: (num? value) => value == null,
            supportsEvents: true,
          );
      eventModule.domain.applySnapshot(
        envelope: _buildEnvelope(ProtocolMessageType.stateSnapshot),
        payload: _buildSnapshot(1, 10),
      );

      eventModule.domain.applyEvent(
        envelope: _buildEnvelope(ProtocolMessageType.stateEvent),
        payload: _buildEvent(1, 2, 11),
      );

      final StateSynchronization<num?> state = await eventModule.changes.first;
      expect(state.status, DovahLinkStateStatus.synchronized);
      expect(state.value, 11);
      expect(state.revision, 2);
    });

    test('Method applyEvent marks an Event revision gap stale', () async {
      final SingleDomainStateModule<num?> eventModule =
          SingleDomainStateModule<num?>(
            stateArea: 'synthetic_area',
            decode: _decodeSyntheticValue,
            isUnavailable: (num? value) => value == null,
            supportsEvents: true,
          );
      eventModule.domain.applySnapshot(
        envelope: _buildEnvelope(ProtocolMessageType.stateSnapshot),
        payload: _buildSnapshot(1, 10),
      );

      eventModule.domain.applyEvent(
        envelope: _buildEnvelope(ProtocolMessageType.stateEvent),
        payload: _buildEvent(5, 6, 11),
      );

      final StateSynchronization<num?> state = await eventModule.changes.first;
      expect(state.status, DovahLinkStateStatus.stale);
      expect(state.value, 10);
      expect(state.revision, 1);
    });
  });
}
