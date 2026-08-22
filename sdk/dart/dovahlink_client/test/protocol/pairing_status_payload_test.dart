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

/// Runs [PairingStatusPayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    const Map<PairingAvailability, String>
    stateFixtures = <PairingAvailability, String>{
      PairingAvailability.unavailable:
          'pairing/pairing-status-unavailable.json',
      PairingAvailability.available: 'pairing/pairing-status-available.json',
      PairingAvailability.inProgress: 'pairing/pairing-status-in-progress.json',
      PairingAvailability.otherDevicePairing:
          'pairing/pairing-status-other-device.json',
    };
    for (final MapEntry<PairingAvailability, String> entry
        in stateFixtures.entries) {
      test('Method fromJson decodes the canonical ${entry.key} fixture', () {
        final PairingStatusPayload payload = PairingStatusPayload.fromJson(
          _readPayload(entry.value),
        );

        expect(payload.state, entry.key);
      });
    }

    test(
      'Method fromJson decodes expiresInSeconds for an available fixture',
      () {
        final PairingStatusPayload payload = PairingStatusPayload.fromJson(
          _readPayload('pairing/pairing-status-available.json'),
        );

        expect(payload.expiresInSeconds, 300);
      },
    );

    test('Method fromJson accepts zero seconds for an available challenge', () {
      final PairingStatusPayload payload = PairingStatusPayload.fromJson(
        <String, dynamic>{'state': 'available', 'expiresInSeconds': 0},
      );

      expect(payload.expiresInSeconds, 0);
    });

    test(
      'Method fromJson decodes expiresInSeconds for an in_progress fixture',
      () {
        final PairingStatusPayload payload = PairingStatusPayload.fromJson(
          _readPayload('pairing/pairing-status-in-progress.json'),
        );

        expect(payload.expiresInSeconds, 187);
      },
    );

    test(
      'Method fromJson decodes expiresInSeconds as null for an unavailable fixture',
      () {
        final PairingStatusPayload payload = PairingStatusPayload.fromJson(
          _readPayload('pairing/pairing-status-unavailable.json'),
        );

        expect(payload.expiresInSeconds, isNull);
      },
    );

    test(
      'Method fromJson decodes expiresInSeconds as null for an other_device_pairing fixture',
      () {
        final PairingStatusPayload payload = PairingStatusPayload.fromJson(
          _readPayload('pairing/pairing-status-other-device.json'),
        );

        expect(payload.expiresInSeconds, isNull);
      },
    );

    test(
      'Method fromJson decodes an omitted expiry only for other_device_pairing',
      () {
        final PairingStatusPayload payload = PairingStatusPayload.fromJson(
          <String, dynamic>{'state': 'other_device_pairing'},
        );

        expect(payload.state, PairingAvailability.otherDevicePairing);
        expect(payload.expiresInSeconds, isNull);
      },
    );

    test(
      'Method fromJson rejects a payload missing the required state key',
      () {
        expect(
          () => PairingStatusPayload.fromJson(<String, dynamic>{
            'expiresInSeconds': null,
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson rejects impossible state and expiry combinations',
      () {
        const List<JsonMap> invalidPayloads = <JsonMap>[
          <String, dynamic>{'state': 'available', 'expiresInSeconds': null},
          <String, dynamic>{'state': 'available'},
          <String, dynamic>{'state': 'unavailable', 'expiresInSeconds': 1},
          <String, dynamic>{'state': 'unavailable'},
          <String, dynamic>{'state': 'in_progress'},
          <String, dynamic>{
            'state': 'other_device_pairing',
            'expiresInSeconds': null,
          },
        ];

        for (final JsonMap payload in invalidPayloads) {
          expect(
            () => PairingStatusPayload.fromJson(payload),
            throwsA(isA<ProtocolFormatException>()),
            reason: '$payload is not a valid pairing_status payload',
          );
        }
      },
    );

    test('Method fromJson rejects negative and non-integral expiry values', () {
      for (final Object value in <Object>[-1, 1.5]) {
        expect(
          () => PairingStatusPayload.fromJson(<String, dynamic>{
            'state': 'available',
            'expiresInSeconds': value,
          }),
          throwsA(isA<ProtocolFormatException>()),
          reason: '$value is not a valid expiry',
        );
      }
    });

    test('Method fromJson rejects an unrecognized state', () {
      expect(
        () => PairingStatusPayload.fromJson(<String, dynamic>{
          'state': 'not-a-real-state',
          'expiresInSeconds': null,
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test(
      'Method fromJson rejects a payload with the wrong type for the state key',
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
      'Method fromJson rejects a payload with the wrong type for expiresInSeconds',
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
}
