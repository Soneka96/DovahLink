// ignore_for_file: overridden_fields

import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client/features/connection/data/models/json_map.dart';
import 'package:dovahlink_client/features/connection/data/models/protocol_format_exception.dart';
import 'package:dovahlink_client/features/connection/data/models/resource_value.model.dart';
import 'package:dovahlink_client/features/connection/domain/entities/character_state.entity.dart';

part 'character_state.model.g.dart';

/// A generated JSON adapter for [CharacterStateEntity].
@JsonSerializable(checked: true, explicitToJson: true)
class CharacterStateModel extends CharacterStateEntity {
  /// Creates a decoded character state.
  const CharacterStateModel({
    required this.level,
    required this.health,
    required this.magicka,
    required this.stamina,
  }) : super(level: level, health: health, magicka: magicka, stamina: stamina);

  /// Decodes and validates one character state payload.
  factory CharacterStateModel.fromJson(JsonMap json) {
    try {
      return _$CharacterStateModelFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid character state: $error');
    }
  }

  /// The player's level, or `null` when unavailable.
  @JsonKey(required: true, fromJson: _readNullableInteger)
  @override
  final int? level;

  /// The player's health pool, or `null` when unavailable.
  @JsonKey(required: true)
  @override
  final ResourceValueModel? health;

  /// The player's magicka pool, or `null` when unavailable.
  @JsonKey(required: true)
  @override
  final ResourceValueModel? magicka;

  /// The player's stamina pool, or `null` when unavailable.
  @JsonKey(required: true)
  @override
  final ResourceValueModel? stamina;

  /// Encodes this character state as a JSON object.
  JsonMap toJson() => _$CharacterStateModelToJson(this);
}

/// Reads an integer that may explicitly be unavailable.
int? _readNullableInteger(Object? value) {
  if (value == null || value is int) return value as int?;
  throw const FormatException('value must be an integer or null');
}
