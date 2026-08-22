import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_confirm_payload.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

void main() {
  group('PairingConfirmPayload', () {
    group('methods', () {
      test('toJson matches the canonical fixture with a display name', () {
        const PairingConfirmPayload payload = PairingConfirmPayload(
          code: '123456',
          displayName: 'My PC',
        );

        expect(payload.toJson(), _readPayload('pairing/pairing-confirm.json'));
      });

      test('toJson includes displayName as null, not absent, when omitted', () {
        const PairingConfirmPayload payload = PairingConfirmPayload(
          code: '123456',
        );

        final JsonMap json = payload.toJson();

        expect(json.containsKey('displayName'), isTrue);
        expect(json['displayName'], isNull);
      });
    });
  });
}
