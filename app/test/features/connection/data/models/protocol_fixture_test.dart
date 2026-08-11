import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/data/models/character_state.model.dart';
import 'package:dovahlink_client/features/connection/data/models/json_map.dart';
import 'package:dovahlink_client/features/connection/data/models/protocol_envelope.model.dart';

void main() {
  const List<String> fixturePaths = [
    'state/character/character-state-snapshot.json',
    'state/character/character-state-unavailable.json',
    'state/character/character-state-event.json',
  ];

  for (final String fixturePath in fixturePaths) {
    test('shared fixture $fixturePath decodes through the client models', () {
      final File fixture = File('../protocol/fixtures/$fixturePath');
      final JsonMap json = jsonDecode(fixture.readAsStringSync()) as JsonMap;
      final ProtocolEnvelopeModel envelope = ProtocolEnvelopeModel.fromJson(
        json,
      );
      final JsonMap payload = envelope.payload;
      final CharacterStateModel state = CharacterStateModel.fromJson(
        payload['data'] as JsonMap,
      );

      expect(envelope.protocolVersion, 1);
      expect(
        envelope.messageType,
        fixturePath.endsWith('event.json') ? 'state_event' : 'state_snapshot',
      );
      expect(envelope.messageId, isNotEmpty);
      expect(envelope.sessionId, 'session-1');
      expect(payload['stateArea'], 'character');
      expect(payload['revision'], isA<int>());
      expect(payload['occurredAt'], isA<String>());
      expect(state.toJson(), payload['data']);
      expect(envelope.toJson(), json);

      if (fixturePath.endsWith('event.json')) {
        expect(payload['baseRevision'], 1);
        expect(payload['revision'], 2);
        expect(payload['revision'], (payload['baseRevision'] as int) + 1);
      }
      if (fixturePath.endsWith('unavailable.json')) {
        expect(state.level, isNull);
        expect(state.health, isNull);
        expect(state.magicka, isNull);
        expect(state.stamina, isNull);
      }
    });
  }
}
