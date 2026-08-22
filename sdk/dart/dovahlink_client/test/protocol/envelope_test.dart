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

/// Runs [Envelope] encoding and decoding behavior tests.
void main() {
  group('Behavior envelope round-tripping behaves correctly', () {
    test(
      'Behavior round-trip preserves a hello with no identity established yet',
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
      'Behavior round-trip preserves a hello_ack with an established identity',
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
      'Behavior round-trip preserves a hello_ack with an active play context',
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
  });

  group('Method fromJson behaves correctly', () {
    test(
      'Method fromJson rejects a payload with the wrong type for a required identity key',
      () {
        final JsonMap withWrongType = _readFixture('connection/hello-ack.json');
        withWrongType['clientId'] = 42;

        expect(
          () => Envelope.fromJson(withWrongType),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson rejects a payload missing a required identity key',
      () {
        final JsonMap withMissingKey = _readFixture('connection/hello-ack.json')
          ..remove('playContextId');

        expect(
          () => Envelope.fromJson(withMissingKey),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test('Method fromJson rejects an unrecognized message type', () {
      final JsonMap withUnknownMessageType = _readFixture(
        'connection/hello.json',
      )..['messageType'] = 'future_message';

      expect(
        () => Envelope.fromJson(withUnknownMessageType),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test(
      'Method fromJson rejects empty identifiers and invalid session shapes',
      () {
        final List<JsonMap> invalidPayloads = <JsonMap>[
          _readFixture('connection/hello-ack.json')..['messageId'] = '',
          _readFixture('connection/hello-ack.json')..['correlationId'] = '',
          _readFixture('connection/hello.json')
            ..['messageType'] = 'pong'
            ..['sessionId'] = null,
          _readFixture('connection/hello.json')..['sessionId'] = 'session-1',
          _readFixture('connection/hello-ack.json')
            ..['messageType'] = 'error'
            ..['sessionId'] = '',
        ];

        for (final JsonMap payload in invalidPayloads) {
          expect(
            () => Envelope.fromJson(payload),
            throwsA(isA<ProtocolFormatException>()),
            reason: '$payload is not a valid envelope',
          );
        }
      },
    );

    test('Method fromJson rejects empty optional identity values', () {
      for (final String field in <String>[
        'bridgeInstanceId',
        'playContextId',
        'clientId',
      ]) {
        final JsonMap payload = _readFixture('connection/hello-ack.json');
        payload[field] = '';

        expect(
          () => Envelope.fromJson(payload),
          throwsA(isA<ProtocolFormatException>()),
          reason: '$field must not be empty',
        );
      }
    });

    test(
      'Method fromJson accepts a null sessionId for a pre-session error',
      () {
        final Envelope envelope = Envelope.fromJson(<String, dynamic>{
          'messageType': 'error',
          'messageId': 'error-1',
          'sessionId': null,
          'correlationId': null,
          'payload': <String, dynamic>{},
          'bridgeInstanceId': null,
          'playContextId': null,
          'clientId': null,
        });

        expect(envelope.messageType, ProtocolMessageType.error);
        expect(envelope.sessionId, isNull);
        expect(Envelope.fromJson(envelope.toJson()).sessionId, isNull);
      },
    );

    test(
      'Method fromJson accepts a non-empty sessionId for a session error',
      () {
        final Envelope envelope = Envelope.fromJson(<String, dynamic>{
          'messageType': 'error',
          'messageId': 'error-1',
          'sessionId': 'session-1',
          'correlationId': null,
          'payload': <String, dynamic>{},
          'bridgeInstanceId': null,
          'playContextId': null,
          'clientId': null,
        });

        expect(envelope.sessionId, 'session-1');
        expect(Envelope.fromJson(envelope.toJson()).sessionId, 'session-1');
      },
    );

    test('Method fromJson rejects a non-object payload', () {
      expect(
        () => Envelope.fromJson(<String, dynamic>{
          'messageType': 'ping',
          'messageId': 'message-1',
          'sessionId': 'session-1',
          'correlationId': null,
          'payload': 'not-an-object',
          'bridgeInstanceId': null,
          'playContextId': null,
          'clientId': null,
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects impossible correlation shapes', () {
      final JsonMap helloAckWithoutCorrelation = _readFixture(
        'connection/hello-ack.json',
      )..['correlationId'] = null;
      final JsonMap invalidatedWithCorrelation =
          _readFixture('connection/hello-ack.json')
            ..['messageType'] = 'session_invalidated'
            ..['correlationId'] = 'message-1';
      final JsonMap helloWithCorrelation = _readFixture('connection/hello.json')
        ..['correlationId'] = 'message-1';
      final JsonMap helloAckWithoutClientId = _readFixture(
        'connection/hello-ack.json',
      )..['clientId'] = null;
      final JsonMap helloWithIdentity = _readFixture('connection/hello.json')
        ..['bridgeInstanceId'] = 'bridge-1';
      final JsonMap capabilitiesWithCorrelation =
          _readFixture('connection/hello.json')
            ..['messageType'] = 'capabilities'
            ..['correlationId'] = 'message-1';
      final JsonMap stateEventWithCorrelation =
          _readFixture('connection/hello.json')
            ..['messageType'] = 'state_event'
            ..['correlationId'] = 'message-1';
      final JsonMap pingWithCorrelation = _readFixture('connection/hello.json')
        ..['messageType'] = 'ping'
        ..['correlationId'] = 'message-1';
      final List<JsonMap> clientRequestsWithCorrelation =
          <String>[
            'pairing_request',
            'pairing_confirm',
            'pairing_ack',
            'pairing_renotify',
            'pairing_cancel',
            'rename_request',
            'subscribe',
            'snapshot_request',
          ].map((String messageType) {
            return _readFixture('connection/hello.json')
              ..['messageType'] = messageType
              ..['sessionId'] = 'session-1'
              ..['clientId'] = 'client-1'
              ..['correlationId'] = 'message-1';
          }).toList();

      for (final JsonMap payload in <JsonMap>[
        helloAckWithoutCorrelation,
        invalidatedWithCorrelation,
        helloWithCorrelation,
        helloAckWithoutClientId,
        helloWithIdentity,
        capabilitiesWithCorrelation,
        stateEventWithCorrelation,
        pingWithCorrelation,
        ...clientRequestsWithCorrelation,
      ]) {
        expect(
          () => Envelope.fromJson(payload),
          throwsA(isA<ProtocolFormatException>()),
          reason: '$payload has an impossible correlation shape',
        );
      }
    });

    test(
      'Method fromJson accepts a client capabilities envelope with clientId',
      () {
        final Envelope envelope = Envelope.fromJson(
          _readFixture('capabilities/capabilities-client.json'),
        );

        expect(envelope.messageType, ProtocolMessageType.capabilities);
        expect(envelope.clientId, 'client-1');
        expect(envelope.correlationId, isNull);
      },
    );

    test('Method fromJson rejects clientId on bridge-originated messages', () {
      const Map<String, String?> bridgeMessages = <String, String?>{
        'pairing_status': 'message-1',
        'pairing_outcome': 'message-1',
        'rename_outcome': 'message-1',
        'subscription_ack': 'message-1',
        'state_snapshot': 'message-1',
        'state_event': null,
        'error': null,
        'session_invalidated': null,
        'pong': 'message-1',
      };

      for (final MapEntry<String, String?> entry in bridgeMessages.entries) {
        final JsonMap payload = _readFixture('connection/hello.json')
          ..['messageType'] = entry.key
          ..['sessionId'] = 'session-1'
          ..['correlationId'] = entry.value
          ..['clientId'] = 'client-1';

        expect(
          () => Envelope.fromJson(payload),
          throwsA(
            isA<ProtocolFormatException>().having(
              (ProtocolFormatException error) => error.message,
              'message',
              contains('clientId must be null for'),
            ),
          ),
          reason: '$entry must reject a clientId',
        );
      }
    });
  });

  group('Behavior ProtocolMessageType wire mapping behaves correctly', () {
    test('Behavior wire mapping preserves every canonical message type', () {
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
      const Set<ProtocolMessageType> correlationRequired =
          <ProtocolMessageType>{
            ProtocolMessageType.helloAck,
            ProtocolMessageType.pairingStatus,
            ProtocolMessageType.pairingOutcome,
            ProtocolMessageType.renameOutcome,
            ProtocolMessageType.subscriptionAck,
            ProtocolMessageType.stateSnapshot,
            ProtocolMessageType.pong,
          };
      const Set<ProtocolMessageType> clientIdRequired = <ProtocolMessageType>{
        ProtocolMessageType.pairingRequest,
        ProtocolMessageType.pairingConfirm,
        ProtocolMessageType.pairingAck,
        ProtocolMessageType.pairingRenotify,
        ProtocolMessageType.pairingCancel,
        ProtocolMessageType.renameRequest,
        ProtocolMessageType.subscribe,
        ProtocolMessageType.snapshotRequest,
        ProtocolMessageType.ping,
      };
      for (final MapEntry<ProtocolMessageType, String> entry
          in wireValues.entries) {
        final JsonMap json = _readFixture('connection/hello.json')
          ..['messageType'] = entry.value
          ..['sessionId'] = entry.key == ProtocolMessageType.hello
              ? null
              : 'session-1'
          ..['correlationId'] = correlationRequired.contains(entry.key)
              ? 'message-1'
              : null
          ..['clientId'] =
              entry.key == ProtocolMessageType.helloAck ||
                  clientIdRequired.contains(entry.key)
              ? 'client-1'
              : null;

        final Envelope envelope = Envelope.fromJson(json);

        expect(envelope.messageType, entry.key);
        expect(envelope.toJson()['messageType'], entry.value);
      }
    });
  });

  group('Method toJson behaves correctly', () {
    test('Method toJson always includes the identity keys, value or null', () {
      const Envelope envelope = Envelope(
        messageType: ProtocolMessageType.pong,
        messageId: 'message-1',
        sessionId: 'session-1',
        correlationId: 'ping-1',
        payload: <String, dynamic>{},
        bridgeInstanceId: null,
        playContextId: null,
        clientId: null,
      );

      final JsonMap json = envelope.toJson();
      final Envelope decoded = Envelope.fromJson(json);

      expect(json.containsKey('bridgeInstanceId'), isTrue);
      expect(json.containsKey('playContextId'), isTrue);
      expect(json.containsKey('clientId'), isTrue);
      expect(json['bridgeInstanceId'], isNull);
      expect(json['playContextId'], isNull);
      expect(json['clientId'], isNull);
      expect(json['messageType'], 'pong');
      expect(decoded.messageType, ProtocolMessageType.pong);
      expect(decoded.messageId, 'message-1');
      expect(decoded.sessionId, 'session-1');
    });
  });
}
