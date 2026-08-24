import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

/// Validates revision fields shared by state snapshots and state events.
class ProtocolRevisionValidator {
  /// Rejects [value] when it is not an integer greater than or equal to zero.
  ///
  /// [fieldName] is included in the safe protocol-format diagnostic.
  ///
  /// @param value The decoded JSON value to validate.
  /// @param fieldName The protocol field name used in the diagnostic.
  static void validateNonNegativeInt(Object? value, String fieldName) {
    if (value is! int || value < 0) {
      throw ProtocolFormatException(
        '$fieldName must be a non-negative integer.',
      );
    }
  }
}
