import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Validates the cross-field and raw-shape invariants of a decoded pairing-status payload.
class PairingStatusPayloadValidator {
  /// Validates [state] and [expiresInSeconds] against the original decoded [json] shape.
  static void validate({
    required PairingAvailability state,
    required int? expiresInSeconds,
    required JsonMap json,
  }) {
    final bool hasExpiry = json.containsKey('expiresInSeconds');
    final Object? rawExpiry = json['expiresInSeconds'];
    if (rawExpiry != null && rawExpiry is! int) {
      throw const ProtocolFormatException(
        'expiresInSeconds must be an integer or null.',
      );
    }
    if (rawExpiry is int && rawExpiry < 0) {
      throw const ProtocolFormatException(
        'expiresInSeconds must not be negative.',
      );
    }

    final bool isOtherDevicePairing =
        state == PairingAvailability.otherDevicePairing;
    if (isOtherDevicePairing == hasExpiry) {
      throw ProtocolFormatException(
        isOtherDevicePairing
            ? 'expiresInSeconds must be omitted for other_device_pairing.'
            : 'expiresInSeconds must be present for $state.',
      );
    }
    if (state == PairingAvailability.available && expiresInSeconds == null) {
      throw const ProtocolFormatException(
        'expiresInSeconds must be a number for available.',
      );
    }
    if (state == PairingAvailability.unavailable && expiresInSeconds != null) {
      throw const ProtocolFormatException(
        'expiresInSeconds must be null for unavailable.',
      );
    }
  }
}
