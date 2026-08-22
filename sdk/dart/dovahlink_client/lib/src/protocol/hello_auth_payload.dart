import 'package:json_annotation/json_annotation.dart';

import '../shared/enums.dart';
import 'json_map.dart';

part 'hello_auth_payload.g.dart';

/// The nested `hello.auth` object (`protocol/schema/README.md`'s `hello`). Encode-only: the
/// client never decodes its own `hello`.
@JsonSerializable(createFactory: false)
class HelloAuthPayload {
  /// Creates a hello-auth payload.
  const HelloAuthPayload({required this.method, this.token});

  /// The auth method presented in `auth.method`.
  final AuthMethod method;

  /// The hex-encoded credential or token, when [method] requires one. Omitted from the encoded
  /// object entirely (not merely `null`) when absent, per `hello`'s wire shape.
  @JsonKey(includeIfNull: false)
  final String? token;

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => _$HelloAuthPayloadToJson(this);
}
