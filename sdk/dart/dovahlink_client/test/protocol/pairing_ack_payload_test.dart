import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_ack_payload.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

void main() {
  group('PairingAckPayload', () {
    group('methods', () {
      test('toJson matches the canonical fixture', () {
        const PairingAckPayload payload = PairingAckPayload(
          credential: 'a1b2c3d4e5f6',
        );

        expect(payload.toJson(), _readPayload('pairing/pairing-ack.json'));
      });
    });
  });
}
