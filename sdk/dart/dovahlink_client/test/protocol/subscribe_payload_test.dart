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
    test(
      'Method toJson matches the canonical single-area subscription fixture',
      () {
        const SubscribePayload payload = SubscribePayload(
          stateAreas: <String>['character_xp'],
        );

        expect(payload.toJson(), _readPayload('subscriptions/subscribe.json'));
      },
    );

    test('Method toJson matches the canonical complete multi-area set', () {
      const SubscribePayload payload = SubscribePayload(
        stateAreas: <String>['character_xp', 'character_health'],
      );

      expect(
        payload.toJson(),
        _readPayload('subscriptions/subscribe-add-area.json'),
      );
    });

    test('Method toJson matches the canonical replacement set', () {
      const SubscribePayload payload = SubscribePayload(
        stateAreas: <String>['character_health'],
      );

      expect(
        payload.toJson(),
        _readPayload('subscriptions/subscribe-replacement.json'),
      );
    });

    test('Method toJson matches the canonical empty desired set', () {
      const SubscribePayload payload = SubscribePayload(stateAreas: <String>[]);

      expect(
        payload.toJson(),
        _readPayload('subscriptions/subscribe-empty.json'),
      );
    });
  });
}
