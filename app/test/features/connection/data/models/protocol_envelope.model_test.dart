import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/data/models/protocol_envelope.model.dart';
import 'package:dovahlink_client/features/connection/data/models/protocol_format_exception.dart';

void main() {
  group('ProtocolEnvelopeModel', () {
    test('parses and serializes a complete envelope', () {
      final ProtocolEnvelopeModel envelope = ProtocolEnvelopeModel.fromJson({
        'protocolVersion': 1,
        'messageType': 'state_snapshot',
        'messageId': 'message-1',
        'sessionId': 'session-1',
        'correlationId': 'subscribe-1',
        'payload': {'stateArea': 'character'},
      });

      expect(envelope.protocolVersion, 1);
      expect(envelope.messageType, 'state_snapshot');
      expect(envelope.sessionId, 'session-1');
      expect(envelope.correlationId, 'subscribe-1');
      expect(envelope.toJson()['payload'], {'stateArea': 'character'});
    });

    test('accepts null session and correlation IDs', () {
      final ProtocolEnvelopeModel envelope = ProtocolEnvelopeModel.fromJson({
        'protocolVersion': 1,
        'messageType': 'capabilities',
        'messageId': 'message-1',
        'sessionId': null,
        'correlationId': null,
        'payload': <String, dynamic>{},
      });

      expect(envelope.sessionId, isNull);
      expect(envelope.correlationId, isNull);
    });

    test('rejects a missing or incorrectly typed required field', () {
      final Map<String, dynamic> json = {
        'protocolVersion': 1,
        'messageType': 'state_snapshot',
        'messageId': 'message-1',
        'sessionId': 'session-1',
        'correlationId': null,
        'payload': <String, dynamic>{},
      };

      expect(
        () => ProtocolEnvelopeModel.fromJson({...json, 'messageId': 4}),
        throwsA(isA<ProtocolFormatException>()),
      );
      expect(
        () => ProtocolEnvelopeModel.fromJson({...json}..remove('payload')),
        throwsA(isA<ProtocolFormatException>()),
      );
      expect(
        () => ProtocolEnvelopeModel.fromJson({...json, 'protocolVersion': -1}),
        throwsA(isA<ProtocolFormatException>()),
      );
      expect(
        () => ProtocolEnvelopeModel.fromJson({...json}..remove('sessionId')),
        throwsA(isA<ProtocolFormatException>()),
      );
      expect(
        () =>
            ProtocolEnvelopeModel.fromJson({...json}..remove('correlationId')),
        throwsA(isA<ProtocolFormatException>()),
      );
      expect(
        () => ProtocolEnvelopeModel.fromJson({...json}..remove('messageType')),
        throwsA(isA<ProtocolFormatException>()),
      );
      expect(
        () => ProtocolEnvelopeModel.fromJson(
          {...json}..remove('protocolVersion'),
        ),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('round-trips the complete envelope without losing fields', () {
      final Map<String, dynamic> json = {
        'protocolVersion': 1,
        'messageType': 'state_snapshot',
        'messageId': 'message-1',
        'sessionId': 'session-1',
        'correlationId': null,
        'payload': {'stateArea': 'character', 'revision': 4},
      };

      expect(ProtocolEnvelopeModel.fromJson(json).toJson(), json);
    });
  });
}
