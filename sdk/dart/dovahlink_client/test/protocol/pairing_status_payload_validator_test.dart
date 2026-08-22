import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_status_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_status_payload_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Builds a pairing-status payload with representative expiry defaults.
PairingStatusPayload buildPairingStatusPayload({
  PairingAvailability state = PairingAvailability.available,
  int? expiresInSeconds = 300,
}) => PairingStatusPayload(state: state, expiresInSeconds: expiresInSeconds);

/// Runs pairing-status validator behavior tests.
void main() {
  group('Method validate behaves correctly', () {
    test('Method validate accepts an available challenge with an integer expiry', () {
      expect(
        () => PairingStatusPayloadValidator.validate(
          state: PairingAvailability.available,
          expiresInSeconds: 300,
          json: <String, dynamic>{
            'state': 'available',
            'expiresInSeconds': 300,
          },
        ),
        returnsNormally,
      );
    });

    test('Method validate accepts other-device pairing when expiry is omitted', () {
      expect(
        () => PairingStatusPayloadValidator.validate(
          state: PairingAvailability.otherDevicePairing,
          expiresInSeconds: null,
          json: <String, dynamic>{'state': 'other_device_pairing'},
        ),
        returnsNormally,
      );
    });

    test('Method validate accepts in-progress challenges with an integer or null expiry', () {
      for (final int? expiry in <int?>[187, null]) {
        expect(
          () => PairingStatusPayloadValidator.validate(
            state: PairingAvailability.inProgress,
            expiresInSeconds: expiry,
            json: <String, dynamic>{
              'state': 'in_progress',
              'expiresInSeconds': expiry,
            },
          ),
          returnsNormally,
          reason: '$expiry is a valid in-progress expiry',
        );
      }
    });

    test('Method validate rejects non-integral and negative expiry values', () {
      for (final Object value in <Object>[1.5, -1]) {
        expect(
          () => PairingStatusPayloadValidator.validate(
            state: PairingAvailability.available,
            expiresInSeconds: 300,
            json: <String, dynamic>{
              'state': 'available',
              'expiresInSeconds': value,
            },
          ),
          throwsA(isA<ProtocolFormatException>()),
          reason: '$value is not a valid expiry',
        );
      }
    });

    test(
      'Method validate rejects state and expiry combinations that violate the wire contract',
      () {
        const List<JsonMap> invalidCases = <JsonMap>[
          <String, dynamic>{'state': 'available'},
          <String, dynamic>{'state': 'unavailable'},
          <String, dynamic>{'state': 'unavailable', 'expiresInSeconds': 1},
          <String, dynamic>{'state': 'in_progress'},
          <String, dynamic>{
            'state': 'other_device_pairing',
            'expiresInSeconds': null,
          },
          <String, dynamic>{
            'state': 'other_device_pairing',
            'expiresInSeconds': 1,
          },
        ];

        for (final JsonMap json in invalidCases) {
          final PairingStatusPayload payload = switch (json['state']) {
            'available' => buildPairingStatusPayload(),
            'unavailable' => buildPairingStatusPayload(
              state: PairingAvailability.unavailable,
              expiresInSeconds: json['expiresInSeconds'] as int?,
            ),
            'in_progress' => buildPairingStatusPayload(
              state: PairingAvailability.inProgress,
              expiresInSeconds: json['expiresInSeconds'] as int?,
            ),
            _ => buildPairingStatusPayload(
              state: PairingAvailability.otherDevicePairing,
              expiresInSeconds: null,
            ),
          };

          expect(
            () => PairingStatusPayloadValidator.validate(
              state: payload.state,
              expiresInSeconds: payload.expiresInSeconds,
              json: json,
            ),
            throwsA(isA<ProtocolFormatException>()),
            reason: '$json is not a valid pairing_status payload',
          );
        }
      },
    );
  });
}
