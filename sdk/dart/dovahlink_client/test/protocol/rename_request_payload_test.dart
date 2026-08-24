import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/rename_request_payload.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [RenameRequestPayload.toJson] behavior tests.
void main() {
  group('Method toJson behaves correctly', () {
    test('Method toJson matches the canonical rename-request fixture', () {
      const RenameRequestPayload payload = RenameRequestPayload(displayName: 'New Name');

      expect(payload.toJson(), _readPayload('rename/rename-request.json'));
    });

    test('Method toJson encodes an empty displayName', () {
      const RenameRequestPayload payload = RenameRequestPayload(displayName: '');

      expect(payload.toJson(), <String, dynamic>{'displayName': ''});
    });
  });
}
