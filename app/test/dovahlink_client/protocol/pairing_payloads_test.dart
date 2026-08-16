import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/src/dovahlink_client/protocol/json_map.dart';
import 'package:dovahlink_client/src/dovahlink_client/protocol/pairing_payloads.dart';
import 'package:dovahlink_client/src/dovahlink_client/protocol/protocol_format_exception.dart';

/// Reads one canonical protocol fixture, relative to `protocol/fixtures/`.
JsonMap _readFixture(String relativePath) {
  final File file = File('../protocol/fixtures/$relativePath');
  return jsonDecode(file.readAsStringSync()) as JsonMap;
}

/// Reads one canonical protocol fixture's payload object.
JsonMap _readPayload(String relativePath) =>
    _readFixture(relativePath)['payload'] as JsonMap;

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

  group('PairingStatusPayload', () {
    group('methods', () {
      const Map<String, String> stateFixtures = <String, String>{
        'unavailable': 'pairing/pairing-status-unavailable.json',
        'available': 'pairing/pairing-status-available.json',
        'in_progress': 'pairing/pairing-status-in-progress.json',
      };
      for (final MapEntry<String, String> entry in stateFixtures.entries) {
        test('fromJson decodes the canonical ${entry.key} fixture', () {
          final PairingStatusPayload payload = PairingStatusPayload.fromJson(
            _readPayload(entry.value),
          );

          expect(payload.state, entry.key);
        });
      }

      test('fromJson rejects a payload missing the required key', () {
        expect(
          () => PairingStatusPayload.fromJson(<String, dynamic>{}),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test(
        'fromJson rejects a payload with the wrong type for the required key',
        () {
          expect(
            () => PairingStatusPayload.fromJson(<String, dynamic>{'state': 7}),
            throwsA(isA<ProtocolFormatException>()),
          );
        },
      );
    });
  });

  group('PairingOutcomePayload', () {
    group('methods', () {
      test('fromJson decodes credential_issued with no shortId yet', () {
        final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
          _readPayload('pairing/pairing-outcome-credential-issued.json'),
        );

        expect(payload.outcome, 'credential_issued');
        expect(payload.credential, 'a1b2c3d4e5f6');
        expect(payload.shortId, isNull);
        expect(payload.displayName, 'My PC');
      });

      test('fromJson decodes trusted with a credential and shortId', () {
        final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
          _readPayload('pairing/pairing-outcome-trusted.json'),
        );

        expect(payload.outcome, 'trusted');
        expect(payload.credential, 'a1b2c3d4e5f6');
        expect(payload.shortId, '12345');
        expect(payload.displayName, 'My PC');
      });

      test(
        'fromJson decodes already_trusted with a credential and shortId',
        () {
          final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
            _readPayload('pairing/pairing-outcome-already-trusted.json'),
          );

          expect(payload.outcome, 'already_trusted');
          expect(payload.credential, 'a1b2c3d4e5f6');
          expect(payload.shortId, '12345');
          expect(payload.displayName, 'My PC');
        },
      );

      test(
        'fromJson decodes a failure outcome with no credential, shortId, or displayName',
        () {
          final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
            _readPayload('pairing/pairing-outcome-expired.json'),
          );

          expect(payload.outcome, 'expired');
          expect(payload.credential, isNull);
          expect(payload.shortId, isNull);
          expect(payload.displayName, isNull);
        },
      );

      test('fromJson rejects a payload missing a required key', () {
        final JsonMap withMissingKey = _readPayload(
          'pairing/pairing-outcome-trusted.json',
        )..remove('outcome');

        expect(
          () => PairingOutcomePayload.fromJson(withMissingKey),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test(
        'fromJson rejects a payload with the wrong type for the required outcome key',
        () {
          final JsonMap withWrongType = _readPayload(
            'pairing/pairing-outcome-trusted.json',
          );
          withWrongType['outcome'] = 7;

          expect(
            () => PairingOutcomePayload.fromJson(withWrongType),
            throwsA(isA<ProtocolFormatException>()),
          );
        },
      );
    });
  });
}
