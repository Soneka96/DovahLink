import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Decodes one typed protocol payload, translating a DTO decode failure into the SDK's existing
/// public boundary exception, per `ai/context/sdk/api-design.md`'s "Protocol DTO decoding".
class ProtocolPayloadDecoder {
  /// Returns `decode(json)`, or throws [DovahLinkProtocolException] with
  /// [ProtocolErrorCode.malformedMessage] when [decode] throws [ProtocolFormatException].
  static T decode<T>(T Function(JsonMap json) decode, JsonMap json) {
    try {
      return decode(json);
    } on ProtocolFormatException catch (error) {
      throw DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: error.message,
        retryable: false,
      );
    }
  }
}
