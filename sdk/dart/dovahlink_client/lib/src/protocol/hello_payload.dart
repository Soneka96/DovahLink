import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/hello_auth_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'hello_payload.g.dart';

/// Outgoing `hello` payload (`protocol/schema/README.md`'s `hello`). Encode-only: the client
/// never decodes its own `hello`.
@JsonSerializable(createFactory: false, explicitToJson: true)
class HelloPayload {
  /// Creates a hello payload. [authToken] is required for [AuthMethod.oneTimeLocalToken] and
  /// [AuthMethod.trustedDeviceCredential], and must be absent (not merely null) for
  /// [AuthMethod.unpaired] -- a contextual invariant this constructor validates directly, since
  /// generated serialization only knows how to encode a field's declared shape, not the
  /// relationship between two of them.
  HelloPayload({
    required this.clientId,
    required AuthMethod authMethod,
    String? authToken,
  }) : auth = HelloAuthPayload(method: authMethod, token: authToken),
       assert(
         (authMethod == AuthMethod.unpaired) == (authToken == null),
         'authToken must be absent for unpaired, and present for every other auth method.',
       );

  /// The fixed endpoint identity every `hello` this client sends carries.
  final String endpoint = 'client';

  /// The logical client/installation identity, independent of any connection.
  final String clientId;

  /// The auth method and credential presented in `auth`.
  final HelloAuthPayload auth;

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => _$HelloPayloadToJson(this);
}
