import 'package:json_annotation/json_annotation.dart';

import '../shared/enums.dart';
import 'json_map.dart';
import 'protocol_format_exception.dart';

part 'hello_ack_payload.g.dart';

/// Incoming `hello_ack` payload (`protocol/schema/README.md`'s `hello_ack`). Decode-only: the
/// client never sends its own `hello_ack`.
@JsonSerializable(checked: true, createToJson: false)
class HelloAckPayload {
  /// Creates a hello-ack payload.
  const HelloAckPayload({
    required this.bridgeVersion,
    required this.clientIdentityKind,
  });

  /// Decodes and validates one `hello_ack` payload.
  factory HelloAckPayload.fromJson(JsonMap json) {
    try {
      return _$HelloAckPayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid hello_ack payload: $error');
    }
  }

  /// The DovahLink Bridge/mod release version.
  @JsonKey(required: true)
  final String bridgeVersion;

  /// The wire vocabulary of `hello_ack.clientIdentityKind`. Mapped to [DovahLinkTrustState] by
  /// `AuthenticationService`, not here.
  @JsonKey(required: true)
  final ClientIdentityKind clientIdentityKind;
}
