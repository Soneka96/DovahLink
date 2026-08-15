import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/data/models/json_map.dart';
import 'package:dovahlink_client/features/connection/data/models/protocol_envelope.model.dart';
import 'package:dovahlink_client/features/connection/domain/entities/protocol_envelope.entity.dart';

/// Reads one canonical protocol fixture, relative to `protocol/fixtures/`.
JsonMap _readFixture(String relativePath) {
  final File file = File('../protocol/fixtures/$relativePath');
  return jsonDecode(file.readAsStringSync()) as JsonMap;
}

/// Exercises [ProtocolEnvelopeModel]'s v1/v2 identity-field mapping against
/// the canonical shared fixtures.
void main() {
  group('ProtocolEnvelopeModel', () {
    group('identity', () {
      test('is usable as a ProtocolEnvelopeEntity', () {
        final ProtocolEnvelopeModel model = ProtocolEnvelopeModel.fromJson(
          _readFixture('connection/hello-ack.json'),
        );

        expect(model, isA<ProtocolEnvelopeEntity>());
      });
    });

    group('methods', () {
      test(
        'fromJson/toJson round-trips a v1 fixture without v2 identity keys',
        () {
          final JsonMap json = _readFixture('connection/hello-ack.json');

          final ProtocolEnvelopeModel model = ProtocolEnvelopeModel.fromJson(
            json,
          );

          expect(model.protocolVersion, isA<int>());
          expect(model.protocolVersion, 0);
          expect(model.bridgeInstanceId, isNull);
          expect(model.playContextId, isNull);
          expect(model.clientId, isNull);
          expect(model.toJson(), json);
          expect(model.toJson().containsKey('bridgeInstanceId'), isFalse);
          expect(model.toJson().containsKey('playContextId'), isFalse);
          expect(model.toJson().containsKey('clientId'), isFalse);
        },
      );

      test(
        'fromJson/toJson round-trips a v2 fixture with no active play context',
        () {
          final JsonMap json = _readFixture('v2/connection/hello-ack.json');

          final ProtocolEnvelopeModel model = ProtocolEnvelopeModel.fromJson(
            json,
          );

          expect(model.protocolVersion, isA<int>());
          expect(model.protocolVersion, 2);
          expect(model.bridgeInstanceId, 'bridge-1');
          expect(model.playContextId, isNull);
          expect(model.clientId, 'client-1');
          expect(model.toJson(), json);
          expect(model.toJson().containsKey('playContextId'), isTrue);
        },
      );

      test(
        'fromJson/toJson round-trips a v2 fixture with an active play context',
        () {
          final JsonMap json = _readFixture(
            'v2/connection/hello-ack-active-context.json',
          );

          final ProtocolEnvelopeModel model = ProtocolEnvelopeModel.fromJson(
            json,
          );

          expect(model.protocolVersion, isA<int>());
          expect(model.protocolVersion, 2);
          expect(model.bridgeInstanceId, 'bridge-1');
          expect(model.playContextId, 'context-1');
          expect(model.clientId, 'client-2');
          expect(model.toJson(), json);
        },
      );

      test(
        'fromJson tolerates an absent v2 identity key the same as an explicit null',
        () {
          final JsonMap withExplicitNull = _readFixture(
            'v2/connection/hello-ack.json',
          );
          final JsonMap withAbsentKey = JsonMap.of(withExplicitNull)
            ..remove('playContextId');

          final ProtocolEnvelopeModel fromExplicitNull =
              ProtocolEnvelopeModel.fromJson(withExplicitNull);
          final ProtocolEnvelopeModel fromAbsentKey =
              ProtocolEnvelopeModel.fromJson(withAbsentKey);

          expect(fromExplicitNull.playContextId, isNull);
          expect(fromAbsentKey.playContextId, isNull);
        },
      );

      test(
        'toJson omits v2 identity keys for a v1 envelope even when they are set',
        () {
          const ProtocolEnvelopeModel model = ProtocolEnvelopeModel(
            protocolVersion: 1,
            messageType: 'pong',
            messageId: 'message-1',
            sessionId: 'session-1',
            correlationId: null,
            payload: {},
            bridgeInstanceId: 'bridge-1',
            playContextId: 'context-1',
            clientId: 'client-1',
          );

          final JsonMap json = model.toJson();

          expect(json.containsKey('bridgeInstanceId'), isFalse);
          expect(json.containsKey('playContextId'), isFalse);
          expect(json.containsKey('clientId'), isFalse);
        },
      );
    });
  });
}
