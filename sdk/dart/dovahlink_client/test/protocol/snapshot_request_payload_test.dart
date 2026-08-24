import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/snapshot_request_payload.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [SnapshotRequestPayload.toJson] behavior tests.
void main() {
  group('Method toJson behaves correctly', () {
    test('Method toJson matches the canonical snapshot-request fixture', () {
      const SnapshotRequestPayload payload = SnapshotRequestPayload(
        stateArea: 'example_area',
        knownRevision: 2,
      );

      expect(
        payload.toJson(),
        _readPayload('subscriptions/snapshot-request.json'),
      );
    });

    test('Method toJson omits knownRevision entirely when absent', () {
      const SnapshotRequestPayload payload = SnapshotRequestPayload(
        stateArea: 'example_area',
      );

      final JsonMap json = payload.toJson();

      expect(json, <String, dynamic>{'stateArea': 'example_area'});
      expect(json.containsKey('knownRevision'), isFalse);
    });
  });
}
