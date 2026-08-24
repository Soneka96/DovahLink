import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/hello_ack_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Reads one canonical protocol fixture, relative to `protocol/fixtures/`.
JsonMap _readFixture(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  return jsonDecode(file.readAsStringSync()) as JsonMap;
}

/// Runs [HelloAckPayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the canonical hello_ack fixture', () {
      final JsonMap json =
          _readFixture('connection/hello-ack.json')['payload'] as JsonMap;

      final HelloAckPayload payload = HelloAckPayload.fromJson(json);

      expect(payload.bridgeVersion, '0.3.3');
      expect(payload.clientIdentityKind, ClientIdentityKind.unpaired);
    });

    test('Method fromJson decodes a paired clientIdentityKind', () {
      final JsonMap json =
          _readFixture('connection/hello-ack-paired.json')['payload']
              as JsonMap;
      final HelloAckPayload payload = HelloAckPayload.fromJson(json);

      expect(payload.clientIdentityKind, ClientIdentityKind.paired);
    });

    test(
      'Method fromJson rejects an unrecognized clientIdentityKind as ProtocolFormatException',
      () {
        expect(
          () => HelloAckPayload.fromJson(<String, dynamic>{
            'bridgeVersion': '0.2.0',
            'clientIdentityKind': 'not-a-real-kind',
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test('Method fromJson rejects a payload missing a required key', () {
      final JsonMap withMissingKey =
          (_readFixture('connection/hello-ack.json')['payload'] as JsonMap)
            ..remove('bridgeVersion');

      expect(
        () => HelloAckPayload.fromJson(withMissingKey),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test(
      'Method fromJson rejects a payload with the wrong type for a required key',
      () {
        final JsonMap withWrongType =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        withWrongType['bridgeVersion'] = 42;

        expect(
          () => HelloAckPayload.fromJson(withWrongType),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test('Method fromJson rejects a payload missing clientIdentityKind', () {
      final JsonMap withMissingKey =
          (_readFixture('connection/hello-ack.json')['payload'] as JsonMap)
            ..remove('clientIdentityKind');

      expect(
        () => HelloAckPayload.fromJson(withMissingKey),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test(
      'Method fromJson rejects a payload with the wrong type for clientIdentityKind',
      () {
        final JsonMap withWrongType =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        withWrongType['clientIdentityKind'] = 42;

        expect(
          () => HelloAckPayload.fromJson(withWrongType),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );
    test('Method fromJson rejects an empty bridgeVersion', () {
      expect(
        () => HelloAckPayload.fromJson(<String, dynamic>{
          'bridgeVersion': '',
          'clientIdentityKind': 'unpaired',
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
