import 'dart:convert';

import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'player_location_state.g.dart';

/// The current cell and its distinct location and worldspace facts.
@JsonSerializable(checked: true, createToJson: false)
class PlayerLocationState {
  /// The current cell's runtime FormID.
  @JsonKey(required: true)
  final int cellId;

  /// Whether the current cell is interior or exterior.
  @JsonKey(required: true)
  final PlayerLocationCellKind cellKind;

  /// The localized cell display name, when available.
  @JsonKey(required: true)
  final String? cellName;

  /// The selected location's runtime FormID, when one exists.
  @JsonKey(required: true)
  final int? locationId;

  /// The selected location's localized display name, when available.
  @JsonKey(required: true)
  final String? locationName;

  /// The current worldspace runtime FormID, when one exists.
  @JsonKey(required: true)
  final int? worldspaceId;

  /// The current worldspace's localized display name, when available.
  @JsonKey(required: true)
  final String? worldspaceName;

  /// Creates one complete available player-location value.
  /// @param cellId The current cell's runtime FormID.
  /// @param cellKind The current cell classification.
  /// @param cellName The localized cell display name, if available.
  /// @param locationId The selected location's runtime FormID, if present.
  /// @param locationName The selected location's localized display name, if available.
  /// @param worldspaceId The current worldspace runtime FormID, if present.
  /// @param worldspaceName The current worldspace's localized display name, if available.
  const PlayerLocationState({
    required this.cellId,
    required this.cellKind,
    required this.cellName,
    required this.locationId,
    required this.locationName,
    required this.worldspaceId,
    required this.worldspaceName,
  });

  /// Decodes one complete available player-location value.
  /// @param json The complete object inside the protocol `value` field.
  /// @return The typed location.
  /// @throws [ProtocolFormatException] if a required field is missing or malformed.
  factory PlayerLocationState.fromJson(JsonMap json) {
    try {
      final PlayerLocationState state = _$PlayerLocationStateFromJson(json);
      final List<String?> names = <String?>[
        state.cellName,
        state.locationName,
        state.worldspaceName,
      ];
      if (state.cellId <= 0 ||
          state.cellId > 0xFFFFFFFF ||
          (state.locationId != null &&
              (state.locationId! <= 0 || state.locationId! > 0xFFFFFFFF)) ||
          (state.worldspaceId != null &&
              (state.worldspaceId! <= 0 || state.worldspaceId! > 0xFFFFFFFF)) ||
          (state.locationId == null && state.locationName != null) ||
          (state.worldspaceId == null && state.worldspaceName != null) ||
          names.any(
            (String? name) =>
                name != null &&
                (name.contains('\u0000') || utf8.encode(name).length > 52),
          )) {
        throw const FormatException(
          'Player location facts are outside their bounds.',
        );
      }
      return state;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid player_location state: $error');
    }
  }
}

/// Decodes an available or unavailable `player_location` state-area value.
/// @param json The protocol state-area `data` object.
/// @return A typed location, or `null` for explicit unavailability.
/// @throws [ProtocolFormatException] if `value` is missing or malformed.
PlayerLocationState? decodePlayerLocationState(JsonMap json) {
  if (!json.containsKey('value')) {
    throw const ProtocolFormatException(
      'Player Location state is missing its value field.',
    );
  }
  final Object? value = json['value'];
  if (value == null) {
    return null;
  }
  if (value is! Map<String, Object?>) {
    throw const ProtocolFormatException(
      'Player Location value must be an object or null.',
    );
  }
  return PlayerLocationState.fromJson(value);
}
