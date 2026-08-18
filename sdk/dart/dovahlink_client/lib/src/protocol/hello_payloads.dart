import 'package:json_annotation/json_annotation.dart';

import '../shared/enums.dart';
import 'json_map.dart';
import 'protocol_format_exception.dart';

part 'hello_payloads.g.dart';

/// Outgoing `hello` payload (`protocol/schema/README.md`'s `hello`). Encode-only: the client never
/// decodes its own `hello`, so this is hand-written rather than generated -- a single nested `auth`
/// object is simpler to construct directly than to justify a generated companion for.
class HelloPayload {
  /// Creates a hello payload. [authToken] is required for [AuthMethod.oneTimeLocalToken] and
  /// [AuthMethod.trustedDeviceCredential], and must be absent (not merely null) for
  /// [AuthMethod.unpaired].
  const HelloPayload({
    required this.clientId,
    required this.authMethod,
    this.authToken,
  }) : assert(
         (authMethod == AuthMethod.unpaired) == (authToken == null),
         'authToken must be absent for unpaired, and present for every other auth method.',
       );

  /// The logical client/installation identity, independent of any connection.
  final String clientId;

  /// The auth method presented in `auth.method`.
  final AuthMethod authMethod;

  /// The hex-encoded credential or token, when [authMethod] requires one.
  final String? authToken;

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => <String, dynamic>{
    'endpoint': 'client',
    'clientId': clientId,
    'auth': <String, dynamic>{
      'method': _wireAuthMethod(authMethod),
      if (authToken != null) 'token': authToken,
    },
  };
}

/// Encodes [authMethod] as its `hello.auth.method` wire value.
String _wireAuthMethod(AuthMethod authMethod) => switch (authMethod) {
  AuthMethod.unpaired => 'unpaired',
  AuthMethod.oneTimeLocalToken => 'one_time_local_token',
  AuthMethod.trustedDeviceCredential => 'trusted_device_credential',
};

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

  /// The raw wire value: `"unpaired"` or `"paired"`. Interpreted into a typed value by the client
  /// rather than here, per `ai/context/flutter/architecture.md`'s "keep semantic validation outside
  /// generated code."
  @JsonKey(required: true)
  final String clientIdentityKind;
}
