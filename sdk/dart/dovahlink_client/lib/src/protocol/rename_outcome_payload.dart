import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'rename_outcome_payload.g.dart';

/// Incoming `rename_outcome` payload (`protocol/schema/README.md`'s `rename_outcome`). Decode-only:
/// the client never sends `rename_outcome`.
@JsonSerializable(checked: true, createToJson: false)
class RenameOutcomePayload {
  /// The wire vocabulary of `rename_outcome.outcome`.
  @JsonKey(required: true)
  final RenameOutcome outcome;

  /// Echoes the resulting name, present only for [RenameOutcome.renamed]; `null` otherwise.
  @JsonKey(required: true)
  final String? displayName;

  /// Creates a rename-outcome payload.
  const RenameOutcomePayload({
    required this.outcome,
    required this.displayName,
  });

  /// Decodes and validates one `rename_outcome` payload.
  factory RenameOutcomePayload.fromJson(JsonMap json) {
    try {
      final RenameOutcomePayload payload = _$RenameOutcomePayloadFromJson(json);
      if (payload.outcome != RenameOutcome.renamed &&
          payload.displayName != null) {
        throw const ProtocolFormatException(
          'displayName must be null unless outcome is renamed.',
        );
      }
      return payload;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid rename_outcome payload: $error');
    }
  }
}
