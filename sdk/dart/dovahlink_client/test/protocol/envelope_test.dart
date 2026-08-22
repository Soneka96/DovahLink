import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

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

          expect(envelope.messageType, ProtocolMessageType.hello);
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

          expect(envelope.messageType, ProtocolMessageType.helloAck);
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

      test('fromJson rejects an unrecognized message type', () {
        final JsonMap withUnknownMessageType = _readFixture(
          'connection/hello.json',
        )..['messageType'] = 'future_message';

        expect(
          () => Envelope.fromJson(withUnknownMessageType),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test('maps every canonical message type to its exact wire value', () {
        const Map<ProtocolMessageType, String> wireValues =
            <ProtocolMessageType, String>{
              ProtocolMessageType.hello: 'hello',
              ProtocolMessageType.helloAck: 'hello_ack',
              ProtocolMessageType.pairingRequest: 'pairing_request',
              ProtocolMessageType.pairingStatus: 'pairing_status',
              ProtocolMessageType.pairingConfirm: 'pairing_confirm',
              ProtocolMessageType.pairingAck: 'pairing_ack',
              ProtocolMessageType.pairingRenotify: 'pairing_renotify',
              ProtocolMessageType.pairingCancel: 'pairing_cancel',
              ProtocolMessageType.pairingOutcome: 'pairing_outcome',
              ProtocolMessageType.renameRequest: 'rename_request',
              ProtocolMessageType.renameOutcome: 'rename_outcome',
              ProtocolMessageType.capabilities: 'capabilities',
              ProtocolMessageType.subscribe: 'subscribe',
              ProtocolMessageType.subscriptionAck: 'subscription_ack',
              ProtocolMessageType.snapshotRequest: 'snapshot_request',
              ProtocolMessageType.stateSnapshot: 'state_snapshot',
              ProtocolMessageType.stateEvent: 'state_event',
              ProtocolMessageType.error: 'error',
              ProtocolMessageType.sessionInvalidated: 'session_invalidated',
              ProtocolMessageType.ping: 'ping',
              ProtocolMessageType.pong: 'pong',
            };

        expect(wireValues.keys, ProtocolMessageType.values);
        for (final MapEntry<ProtocolMessageType, String> entry
            in wireValues.entries) {
          final JsonMap json = _readFixture('connection/hello.json')
            ..['messageType'] = entry.value;

          final Envelope envelope = Envelope.fromJson(json);

          expect(envelope.messageType, entry.key);
          expect(envelope.toJson()['messageType'], entry.value);
        }
      });

      test('toJson always includes the identity keys, value or null', () {
        const Envelope envelope = Envelope(
          messageType: ProtocolMessageType.pong,
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
        expect(json['messageType'], 'pong');
      });
    });
  });
}
