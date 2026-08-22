import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Classifies one decoded reply against the operation's expected message type.
class ReplyValidator {
  /// Returns [envelope] when its type matches, or throws a typed protocol exception otherwise.
  static Envelope validate({
    required ProtocolMessageType expectedType,
    required Envelope envelope,
  }) {
    if (envelope.messageType == ProtocolMessageType.error) {
      try {
        final ErrorPayload error = ErrorPayload.fromJson(envelope.payload);
        throw DovahLinkProtocolException(
          code: error.code,
          message: error.message,
          retryable: error.retryable,
        );
      } on ProtocolFormatException catch (error) {
        throw DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: error.message,
          retryable: false,
        );
      }
    }
    if (envelope.messageType != expectedType) {
      throw DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'Expected $expectedType but received ${envelope.messageType}.',
        retryable: false,
      );
    }
    return envelope;
  }
}
