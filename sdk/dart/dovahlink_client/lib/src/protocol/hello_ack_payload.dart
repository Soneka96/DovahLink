import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'hello_ack_payload.g.dart';

/// Incoming `hello_ack` payload (`protocol/schema/README.md`'s `hello_ack`). Decode-only: the
/// client never sends its own `hello_ack`.
@JsonSerializable(checked: true, createToJson: false)
class HelloAckPayload {
  /// Creates a hello-ack payload.
  const HelloAckPayload({
    required this.hostVersion,
    required this.clientIdentityKind,
  });

  /// Decodes and validates one `hello_ack` payload.
  factory HelloAckPayload.fromJson(JsonMap json) {
    try {
      final HelloAckPayload payload = _$HelloAckPayloadFromJson(json);
      if (payload.hostVersion.isEmpty) {
        throw const ProtocolFormatException('hostVersion must not be empty.');
      }
      return payload;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid hello_ack payload: $error');
    }
  }

  /// The Host's own release version, the compatibility authority.
  @JsonKey(required: true)
  final String hostVersion;

  /// The wire vocabulary of `hello_ack.clientIdentityKind`. Mapped to [DovahLinkTrustState] by
  /// `AuthenticationService`, not here.
  @JsonKey(required: true)
  final ClientIdentityKind clientIdentityKind;
}
