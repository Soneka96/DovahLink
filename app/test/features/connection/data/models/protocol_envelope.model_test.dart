import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/data/models/json_map.dart';
import 'package:dovahlink_client/features/connection/data/models/protocol_envelope.model.dart';
import 'package:dovahlink_client/features/connection/data/models/protocol_format_exception.dart';
import 'package:dovahlink_client/features/connection/domain/entities/protocol_envelope.entity.dart';

/// Reads one canonical protocol fixture, relative to `protocol/fixtures/`.
JsonMap _readFixture(String relativePath) {
  final File file = File('../protocol/fixtures/$relativePath');
  return jsonDecode(file.readAsStringSync()) as JsonMap;
}

/// Exercises [ProtocolEnvelopeModel]'s always-present identity-field mapping
/// against the canonical shared fixtures.
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
        'fromJson/toJson round-trips a hello with no identity established yet',
        () {
          final JsonMap json = _readFixture('connection/hello.json');

          final ProtocolEnvelopeModel model = ProtocolEnvelopeModel.fromJson(
            json,
          );

          expect(model.bridgeInstanceId, isNull);
          expect(model.playContextId, isNull);
          expect(model.clientId, isNull);
          expect(model.toJson(), json);
        },
      );

      test(
        'fromJson/toJson round-trips a hello_ack with no active play context',
        () {
          final JsonMap json = _readFixture('connection/hello-ack.json');

          final ProtocolEnvelopeModel model = ProtocolEnvelopeModel.fromJson(
            json,
          );

          expect(model.bridgeInstanceId, 'bridge-1');
          expect(model.playContextId, isNull);
          expect(model.clientId, 'client-1');
          expect(model.toJson(), json);
          expect(model.toJson().containsKey('playContextId'), isTrue);
        },
      );

      test(
        'fromJson/toJson round-trips a hello_ack with an active play context',
        () {
          final JsonMap json = _readFixture(
            'connection/hello-ack-active-context.json',
          );

          final ProtocolEnvelopeModel model = ProtocolEnvelopeModel.fromJson(
            json,
          );

          expect(model.bridgeInstanceId, 'bridge-1');
          expect(model.playContextId, 'context-1');
          expect(model.clientId, 'client-2');
          expect(model.toJson(), json);
        },
      );

      test('fromJson rejects a payload missing a required identity key', () {
        final JsonMap withMissingKey = _readFixture('connection/hello-ack.json')
          ..remove('playContextId');

        expect(
          () => ProtocolEnvelopeModel.fromJson(withMissingKey),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test(
        'toJson always includes the identity keys, value or null',
        () {
          const ProtocolEnvelopeModel model = ProtocolEnvelopeModel(
            messageType: 'pong',
            messageId: 'message-1',
            sessionId: 'session-1',
            correlationId: null,
            payload: {},
            bridgeInstanceId: null,
            playContextId: null,
            clientId: null,
          );

          final JsonMap json = model.toJson();

          expect(json.containsKey('bridgeInstanceId'), isTrue);
          expect(json.containsKey('playContextId'), isTrue);
          expect(json.containsKey('clientId'), isTrue);
          expect(json['bridgeInstanceId'], isNull);
          expect(json['playContextId'], isNull);
          expect(json['clientId'], isNull);
        },
      );
    });
  });
}
