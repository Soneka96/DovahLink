import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/subscription_ack_payload.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [SubscriptionAckPayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson matches the canonical subscription-ack fixture', () {
      final SubscriptionAckPayload payload = SubscriptionAckPayload.fromJson(
        _readPayload('subscriptions/subscription-ack.json'),
      );

      expect(payload.acceptedStateAreas, isEmpty);
      expect(payload.rejectedStateAreas, <String>['example_area']);
    });

    test('Method fromJson decodes a non-empty acceptedStateAreas list', () {
      final SubscriptionAckPayload payload = SubscriptionAckPayload.fromJson(<String, dynamic>{
        'acceptedStateAreas': <String>['area_a'],
        'rejectedStateAreas': <String>[],
      });

      expect(payload.acceptedStateAreas, <String>['area_a']);
      expect(payload.rejectedStateAreas, isEmpty);
    });

    test('Method fromJson throws ProtocolFormatException when acceptedStateAreas is missing', () {
      expect(
        () => SubscriptionAckPayload.fromJson(<String, dynamic>{'rejectedStateAreas': <String>[]}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when rejectedStateAreas is missing', () {
      expect(
        () => SubscriptionAckPayload.fromJson(<String, dynamic>{'acceptedStateAreas': <String>[]}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when acceptedStateAreas is not a list', () {
      expect(
        () => SubscriptionAckPayload.fromJson(<String, dynamic>{
          'acceptedStateAreas': 'not-a-list',
          'rejectedStateAreas': <String>[],
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when an entry is not a string', () {
      expect(
        () => SubscriptionAckPayload.fromJson(<String, dynamic>{
          'acceptedStateAreas': <dynamic>[1],
          'rejectedStateAreas': <String>[],
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
