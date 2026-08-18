import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

/// Reads one canonical protocol fixture, relative to `protocol/fixtures/`.
JsonMap _readFixture(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  return jsonDecode(file.readAsStringSync()) as JsonMap;
}

/// Exercises [Envelope]'s always-present identity-field mapping against the canonical shared
/// fixtures.
void main() {
  group('Envelope', () {
    group('methods', () {
      test(
        'fromJson/toJson round-trips a hello with no identity established yet',
        () {
          final JsonMap json = _readFixture('connection/hello.json');

          final Envelope envelope = Envelope.fromJson(json);

          expect(envelope.messageType, 'hello');
          expect(envelope.sessionId, isNull);
          expect(envelope.bridgeInstanceId, isNull);
          expect(envelope.clientId, isNull);
          expect(envelope.toJson(), json);
        },
      );

      test(
        'fromJson/toJson round-trips a hello_ack with an established identity',
        () {
          final JsonMap json = _readFixture('connection/hello-ack.json');

          final Envelope envelope = Envelope.fromJson(json);

          expect(envelope.messageType, 'hello_ack');
          expect(envelope.sessionId, 'session-1');
          expect(envelope.correlationId, 'message-hello-1');
          expect(envelope.bridgeInstanceId, 'bridge-1');
          expect(envelope.playContextId, isNull);
          expect(envelope.clientId, 'client-1');
          expect(envelope.toJson(), json);
        },
      );

      test(
        'fromJson/toJson round-trips a hello_ack with an active play context',
        () {
          final JsonMap json = _readFixture(
            'connection/hello-ack-active-context.json',
          );

          final Envelope envelope = Envelope.fromJson(json);

          expect(envelope.playContextId, 'context-1');
          expect(envelope.clientId, 'client-2');
          expect(envelope.toJson(), json);
        },
      );

      test(
        'fromJson rejects a payload with the wrong type for a required identity key',
        () {
          final JsonMap withWrongType = _readFixture(
            'connection/hello-ack.json',
          );
          withWrongType['clientId'] = 42;

          expect(
            () => Envelope.fromJson(withWrongType),
            throwsA(isA<ProtocolFormatException>()),
          );
        },
      );

      test('fromJson rejects a payload missing a required identity key', () {
        final JsonMap withMissingKey = _readFixture('connection/hello-ack.json')
          ..remove('playContextId');

        expect(
          () => Envelope.fromJson(withMissingKey),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test('toJson always includes the identity keys, value or null', () {
        const Envelope envelope = Envelope(
          messageType: 'pong',
          messageId: 'message-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          bridgeInstanceId: null,
          playContextId: null,
          clientId: null,
        );

        final JsonMap json = envelope.toJson();

        expect(json.containsKey('bridgeInstanceId'), isTrue);
        expect(json.containsKey('playContextId'), isTrue);
        expect(json.containsKey('clientId'), isTrue);
        expect(json['bridgeInstanceId'], isNull);
        expect(json['playContextId'], isNull);
        expect(json['clientId'], isNull);
      });
    });
  });
}
