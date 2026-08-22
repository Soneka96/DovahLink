import 'package:json_annotation/json_annotation.dart';

import '../shared/enums.dart';
import 'json_map.dart';
import 'protocol_format_exception.dart';

part 'pairing_status_payload.g.dart';

/// Incoming `pairing_status` payload (`protocol/schema/README.md`'s `pairing_status`).
/// Decode-only: the client never sends `pairing_status`.
@JsonSerializable(checked: true, createToJson: false)
class PairingStatusPayload {
  /// Creates a pairing-status payload.
  const PairingStatusPayload({required this.state, this.expiresInSeconds});

  /// Decodes and validates one `pairing_status` payload.
  factory PairingStatusPayload.fromJson(JsonMap json) {
    try {
      final PairingStatusPayload payload = _$PairingStatusPayloadFromJson(json);
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
          payload.state == PairingAvailability.otherDevicePairing;
      if (isOtherDevicePairing == hasExpiry) {
        throw ProtocolFormatException(
          isOtherDevicePairing
              ? 'expiresInSeconds must be omitted for other_device_pairing.'
              : 'expiresInSeconds must be present for ${payload.state}.',
        );
      }
      if (payload.state == PairingAvailability.available &&
          payload.expiresInSeconds == null) {
        throw const ProtocolFormatException(
          'expiresInSeconds must be a number for available.',
        );
      }
      if (payload.state == PairingAvailability.unavailable &&
          payload.expiresInSeconds != null) {
        throw const ProtocolFormatException(
          'expiresInSeconds must be null for unavailable.',
        );
      }
      return payload;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid pairing_status payload: $error');
    }
  }

  /// The wire vocabulary of `pairing_status.state`.
  @JsonKey(required: true)
  final PairingAvailability state;

  /// The active challenge's remaining *code* validity in seconds -- not a general "how long until
  /// you lose this state" figure, so its presence follows the code's own lifetime, not [state]
  /// alone (`protocol/schema/README.md`'s `pairing_status` section):
  /// - A number for `"available"`, and for `"in_progress"` while the resumed state is still an
  ///   actively counting-down challenge.
  /// - `null` (key present) for `"unavailable"`, and for `"in_progress"` while the resumed state is
  ///   only a pending credential awaiting `pairing_ack` -- the code was already consumed on a
  ///   successful confirm, so there is nothing left to count down even though `"in_progress"` still
  ///   applies.
  /// - Omitted from the wire payload entirely (not merely `null`) for `"other_device_pairing"` --
  ///   this key is therefore optional, unlike this payload's other, always-present fields.
  final int? expiresInSeconds;
}
