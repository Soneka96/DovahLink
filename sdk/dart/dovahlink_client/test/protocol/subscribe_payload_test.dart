import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/subscribe_payload.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [SubscribePayload.toJson] behavior tests.
void main() {
  group('Method toJson behaves correctly', () {
    test('Method toJson matches the canonical subscribe fixture', () {
      const SubscribePayload payload = SubscribePayload(stateAreas: <String>['example_area']);

      expect(payload.toJson(), _readPayload('subscriptions/subscribe.json'));
    });

    test('Method toJson encodes an empty stateAreas list as an unsubscribe', () {
      const SubscribePayload payload = SubscribePayload(stateAreas: <String>[]);

      expect(payload.toJson(), <String, dynamic>{'stateAreas': <String>[]});
    });

    test('Method toJson encodes multiple requested state areas', () {
      const SubscribePayload payload = SubscribePayload(
        stateAreas: <String>['area_a', 'area_b'],
      );

      expect(payload.toJson(), <String, dynamic>{
        'stateAreas': <String>['area_a', 'area_b'],
      });
    });
  });
}
