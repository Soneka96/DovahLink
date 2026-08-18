import 'package:json_annotation/json_annotation.dart';

import 'json_map.dart';
import 'protocol_format_exception.dart';

part 'pairing_payloads.g.dart';

/// Outgoing `pairing_confirm` payload (`protocol/schema/README.md`'s `pairing_confirm`).
/// Encode-only, hand-written: a single flat shape with no nested object.
class PairingConfirmPayload {
  /// Creates a pairing-confirm payload.
  const PairingConfirmPayload({required this.code, this.displayName});

  /// The six-digit code the user read from Skyrim and entered.
  final String code;

  /// An optional, presentation-only label for the resulting trusted client.
  final String? displayName;

  /// Encodes this payload as a JSON object. `displayName` is always present as a key -- `null`
  /// when omitted, per the schema's required-key/nullable-value shape for this field.
  JsonMap toJson() => <String, dynamic>{
    'code': code,
    'displayName': displayName,
  };
}

/// Outgoing `pairing_ack` payload (`protocol/schema/README.md`'s `pairing_ack`). Encode-only,
/// hand-written: a single flat shape with one field.
class PairingAckPayload {
  /// Creates a pairing-ack payload.
  const PairingAckPayload({required this.credential});

  /// The hex-encoded credential received in a prior `credential_issued` outcome.
  final String credential;

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => <String, dynamic>{'credential': credential};
}

/// Incoming `pairing_status` payload (`protocol/schema/README.md`'s `pairing_status`).
/// Decode-only: the client never sends `pairing_status`.
@JsonSerializable(checked: true, createToJson: false)
class PairingStatusPayload {
  /// Creates a pairing-status payload.
  const PairingStatusPayload({required this.state, this.expiresInSeconds});

  /// Decodes and validates one `pairing_status` payload.
  factory PairingStatusPayload.fromJson(JsonMap json) {
    try {
      return _$PairingStatusPayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid pairing_status payload: $error');
    }
  }

  /// The raw wire value: `"unavailable"`, `"available"`, `"in_progress"`, or
  /// `"other_device_pairing"`. Interpreted into a typed value by the client rather than here.
  @JsonKey(required: true)
  final String state;

  /// The active challenge's remaining code validity in seconds, present for `"available"` and
  /// `"in_progress"`; `null` otherwise.
  @JsonKey(required: true)
  final int? expiresInSeconds;
}

/// Incoming `pairing_outcome` payload (`protocol/schema/README.md`'s `pairing_outcome`).
/// Decode-only: the client never sends `pairing_outcome`.
@JsonSerializable(checked: true, createToJson: false)
class PairingOutcomePayload {
  /// Creates a pairing-outcome payload.
  const PairingOutcomePayload({
    required this.outcome,
    required this.credential,
    required this.shortId,
    required this.displayName,
    this.retryAfterSeconds,
  });

  /// Decodes and validates one `pairing_outcome` payload.
  factory PairingOutcomePayload.fromJson(JsonMap json) {
    try {
      return _$PairingOutcomePayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid pairing_outcome payload: $error');
    }
  }

  /// The raw wire value, one of `"credential_issued"`, `"trusted"`, `"already_trusted"`,
  /// `"expired"`, `"invalid"`, `"pacing_limited"`, `"hard_limit_reached"`, `"pending_not_found"`,
  /// `"renotified"`, `"renotify_cooldown"`, `"cancelled"`, or `"already_idle"`. Interpreted into a
  /// typed value by the client rather than here.
  @JsonKey(required: true)
  final String outcome;

  /// Present only for `"credential_issued"`, `"trusted"`, and `"already_trusted"`.
  @JsonKey(required: true)
  final String? credential;

  /// The administration-only identifier, present only for `"trusted"` and `"already_trusted"`.
  @JsonKey(required: true)
  final String? shortId;

  /// Echoes the client-supplied label, present only alongside [credential]/[shortId] when the
  /// client supplied one.
  @JsonKey(required: true)
  final String? displayName;

  /// The remaining wait in seconds before the next attempt is accepted, present for
  /// `"pacing_limited"` (next `pairing_confirm`) and `"renotify_cooldown"` (next
  /// `pairing_renotify`); `null` otherwise.
  @JsonKey(required: true)
  final int? retryAfterSeconds;
}
