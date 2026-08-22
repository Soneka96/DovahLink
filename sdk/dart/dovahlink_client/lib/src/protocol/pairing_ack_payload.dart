import 'package:json_annotation/json_annotation.dart';

import 'json_map.dart';

part 'pairing_ack_payload.g.dart';

/// Outgoing `pairing_ack` payload (`protocol/schema/README.md`'s `pairing_ack`). Encode-only: the
/// client never decodes its own `pairing_ack`.
@JsonSerializable(createFactory: false)
class PairingAckPayload {
  /// Creates a pairing-ack payload.
  const PairingAckPayload({required this.credential});

  /// The hex-encoded credential received in a prior `credential_issued` outcome.
  final String credential;

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => _$PairingAckPayloadToJson(this);
}
