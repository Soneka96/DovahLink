import 'package:json_annotation/json_annotation.dart';

import 'json_map.dart';

part 'pairing_confirm_payload.g.dart';

/// Outgoing `pairing_confirm` payload (`protocol/schema/README.md`'s `pairing_confirm`).
/// Encode-only: the client never decodes its own `pairing_confirm`.
@JsonSerializable(createFactory: false)
class PairingConfirmPayload {
  /// Creates a pairing-confirm payload.
  const PairingConfirmPayload({required this.code, this.displayName});

  /// The six-digit code the user read from Skyrim and entered.
  final String code;

  /// An optional, presentation-only label for the resulting trusted client. Always present as a
  /// key -- `null` when omitted, per the schema's required-key/nullable-value shape for this
  /// field.
  final String? displayName;

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => _$PairingConfirmPayloadToJson(this);
}
