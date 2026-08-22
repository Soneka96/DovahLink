import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_status_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

void main() {
  group('PairingStatusPayload', () {
    group('methods', () {
      const Map<PairingAvailability, String> stateFixtures =
          <PairingAvailability, String>{
            PairingAvailability.unavailable:
                'pairing/pairing-status-unavailable.json',
            PairingAvailability.available:
                'pairing/pairing-status-available.json',
            PairingAvailability.inProgress:
                'pairing/pairing-status-in-progress.json',
            PairingAvailability.otherDevicePairing:
                'pairing/pairing-status-other-device.json',
          };
      for (final MapEntry<PairingAvailability, String> entry
          in stateFixtures.entries) {
        test('fromJson decodes the canonical ${entry.key} fixture', () {
          final PairingStatusPayload payload = PairingStatusPayload.fromJson(
            _readPayload(entry.value),
          );

          expect(payload.state, entry.key);
        });
      }

      test('fromJson decodes expiresInSeconds for an available fixture', () {
        final PairingStatusPayload payload = PairingStatusPayload.fromJson(
          _readPayload('pairing/pairing-status-available.json'),
        );

        expect(payload.expiresInSeconds, 300);
      });

      test('fromJson decodes expiresInSeconds for an in_progress fixture', () {
        final PairingStatusPayload payload = PairingStatusPayload.fromJson(
          _readPayload('pairing/pairing-status-in-progress.json'),
        );

        expect(payload.expiresInSeconds, 187);
      });

      test(
        'fromJson decodes expiresInSeconds as null for an unavailable fixture',
        () {
          final PairingStatusPayload payload = PairingStatusPayload.fromJson(
            _readPayload('pairing/pairing-status-unavailable.json'),
          );

          expect(payload.expiresInSeconds, isNull);
        },
      );

      test(
        'fromJson decodes expiresInSeconds as null for an other_device_pairing fixture',
        () {
          final PairingStatusPayload payload = PairingStatusPayload.fromJson(
            _readPayload('pairing/pairing-status-other-device.json'),
          );

          expect(payload.expiresInSeconds, isNull);
        },
      );

      test('fromJson rejects a payload missing the required state key', () {
        expect(
          () => PairingStatusPayload.fromJson(<String, dynamic>{
            'expiresInSeconds': null,
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test(
        'fromJson decodes a payload with expiresInSeconds genuinely omitted as null, matching '
        'other_device_pairing\'s wire contract',
        () {
          final PairingStatusPayload payload = PairingStatusPayload.fromJson(
            <String, dynamic>{'state': 'other_device_pairing'},
          );

          expect(payload.state, PairingAvailability.otherDevicePairing);
          expect(payload.expiresInSeconds, isNull);
        },
      );

      test(
        'fromJson rejects an unrecognized state as ProtocolFormatException',
        () {
          expect(
            () => PairingStatusPayload.fromJson(<String, dynamic>{
              'state': 'not-a-real-state',
              'expiresInSeconds': null,
            }),
            throwsA(isA<ProtocolFormatException>()),
          );
        },
      );

      test(
        'fromJson rejects a payload with the wrong type for the required state key',
        () {
          expect(
            () => PairingStatusPayload.fromJson(<String, dynamic>{
              'state': 7,
              'expiresInSeconds': null,
            }),
            throwsA(isA<ProtocolFormatException>()),
          );
        },
      );

      test(
        'fromJson rejects a payload with the wrong type for expiresInSeconds',
        () {
          expect(
            () => PairingStatusPayload.fromJson(<String, dynamic>{
              'state': 'available',
              'expiresInSeconds': 'soon',
            }),
            throwsA(isA<ProtocolFormatException>()),
          );
        },
      );
    });
  });
}
