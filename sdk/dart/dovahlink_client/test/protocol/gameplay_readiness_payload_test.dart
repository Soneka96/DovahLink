import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/gameplay_readiness_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Expects [GameplayReadinessPayload.fromJson] to reject [json].
void _expectRejected(Object? json) {
  expect(
    () => GameplayReadinessPayload.fromJson(json! as JsonMap),
    throwsA(isA<ProtocolFormatException>()),
    reason: '$json must be malformed',
  );
}

/// Runs [GameplayReadinessPayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson matches the canonical ready fixture', () {
      final GameplayReadinessPayload payload =
          GameplayReadinessPayload.fromJson(
            _readPayload('readiness/gameplay-readiness-ready.json'),
          );

      expect(payload.ready, isTrue);
      expect(payload.sequence, 2);
    });

    test('Method fromJson matches the canonical not-ready fixture', () {
      final GameplayReadinessPayload payload =
          GameplayReadinessPayload.fromJson(
            _readPayload('readiness/gameplay-readiness-not-ready.json'),
          );

      expect(payload.ready, isFalse);
    });

    test(
      'Method fromJson accepts the smallest and the largest representable sequence',
      () {
        expect(
          GameplayReadinessPayload.fromJson(<String, dynamic>{
            'ready': true,
            'sequence': 1,
          }).sequence,
          1,
        );
        expect(
          GameplayReadinessPayload.fromJson(<String, dynamic>{
            'ready': false,
            'sequence': 9223372036854775807,
          }).sequence,
          9223372036854775807,
        );
      },
    );

    test('Method fromJson rejects the canonical zero-sequence fixture', () {
      _expectRejected(
        _readPayload('readiness/gameplay-readiness-sequence-zero.json'),
      );
    });

    test('Method fromJson rejects the canonical unknown-field fixture', () {
      _expectRejected(
        _readPayload('readiness/gameplay-readiness-unknown-payload-field.json'),
      );
    });

    test(
      'Method fromJson rejects zero, negative, fractional, and non-numeric sequences',
      () {
        for (final Object? sequence in <Object?>[
          0,
          -1,
          1.5,
          2.0,
          '2',
          true,
          null,
          // Beyond int64: a JSON number that does not fit decodes as a double.
          jsonDecode('18446744073709551616'),
        ]) {
          _expectRejected(<String, dynamic>{
            'ready': true,
            'sequence': sequence,
          });
        }
      },
    );

    test('Method fromJson rejects a missing sequence', () {
      _expectRejected(<String, dynamic>{'ready': true});
    });

    test('Method fromJson rejects a missing or non-boolean ready', () {
      _expectRejected(<String, dynamic>{'sequence': 1});
      for (final Object? ready in <Object?>[null, 'true', 1, 0]) {
        _expectRejected(<String, dynamic>{'ready': ready, 'sequence': 1});
      }
    });

    test('Method fromJson rejects any additional payload field', () {
      _expectRejected(<String, dynamic>{
        'ready': true,
        'sequence': 1,
        'reason': 'loading',
      });
    });
  });
}
