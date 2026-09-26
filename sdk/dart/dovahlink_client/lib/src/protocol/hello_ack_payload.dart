import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/host_identity_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'hello_ack_payload.g.dart';

/// Incoming `hello_ack` payload (`protocol/schema/README.md`'s `hello_ack`). Decode-only: the
/// client never sends its own `hello_ack`.
@JsonSerializable(checked: true, createToJson: false)
class HelloAckPayload {
  /// Creates a validated hello-ack payload value.
  /// @param hostId The persistent UUID for the connected Host installation.
  /// @param hostName The Host's current OS computer name.
  /// @param hostVersion The Host release version used for compatibility checks.
  /// @param clientIdentityKind The wire trust identity kind assigned to this client.
  const HelloAckPayload({
    required this.hostId,
    required this.hostName,
    required this.hostVersion,
    required this.clientIdentityKind,
  });

  /// Decodes and validates one `hello_ack` payload.
  /// @param json The decoded payload object.
  factory HelloAckPayload.fromJson(JsonMap json) {
    try {
      final HelloAckPayload payload = _$HelloAckPayloadFromJson(json);
      if (!isValidHostId(payload.hostId)) {
        throw const ProtocolFormatException(
          'hostId must be a non-empty UUID string.',
        );
      }
      final String hostName = payload.hostName;
      if (!isValidHostName(hostName)) {
        throw const ProtocolFormatException(
          'hostName must be non-empty, contain no control characters, and fit its UTF-8 byte limit.',
        );
      }
      if (payload.hostVersion.isEmpty) {
        throw const ProtocolFormatException('hostVersion must not be empty.');
      }
      return payload;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid hello_ack payload: $error');
    }
  }

  /// The persistent DovahLink-generated UUID of this Host installation.
  @JsonKey(required: true)
  final String hostId;

  /// The current operating-system computer name, as mutable display metadata.
  @JsonKey(required: true)
  final String hostName;

  /// The Host's own release version, the compatibility authority.
  @JsonKey(required: true)
  final String hostVersion;

  /// The wire vocabulary of `hello_ack.clientIdentityKind`. Mapped to [DovahLinkTrustState] by
  /// `AuthenticationService`, not here.
  @JsonKey(required: true)
  final ClientIdentityKind clientIdentityKind;
}
