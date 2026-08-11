// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'character_state.model.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

CharacterStateModel _$CharacterStateModelFromJson(Map<String, dynamic> json) =>
    $checkedCreate('CharacterStateModel', json, ($checkedConvert) {
      $checkKeys(
        json,
        requiredKeys: const ['level', 'health', 'magicka', 'stamina'],
      );
      final val = CharacterStateModel(
        level: $checkedConvert('level', (v) => _readNullableInteger(v)),
        health: $checkedConvert(
          'health',
          (v) => v == null
              ? null
              : ResourceValueModel.fromJson(v as Map<String, dynamic>),
        ),
        magicka: $checkedConvert(
          'magicka',
          (v) => v == null
              ? null
              : ResourceValueModel.fromJson(v as Map<String, dynamic>),
        ),
        stamina: $checkedConvert(
          'stamina',
          (v) => v == null
              ? null
              : ResourceValueModel.fromJson(v as Map<String, dynamic>),
        ),
      );
      return val;
    });

Map<String, dynamic> _$CharacterStateModelToJson(
  CharacterStateModel instance,
) => <String, dynamic>{
  'level': instance.level,
  'health': instance.health?.toJson(),
  'magicka': instance.magicka?.toJson(),
  'stamina': instance.stamina?.toJson(),
};
