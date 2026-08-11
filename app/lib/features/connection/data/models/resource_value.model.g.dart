// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'resource_value.model.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ResourceValueModel _$ResourceValueModelFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ResourceValueModel', json, ($checkedConvert) {
      final val = ResourceValueModel(
        current: $checkedConvert('current', (v) => (v as num).toDouble()),
        maximum: $checkedConvert('maximum', (v) => (v as num).toDouble()),
      );
      return val;
    });

Map<String, dynamic> _$ResourceValueModelToJson(ResourceValueModel instance) =>
    <String, dynamic>{'current': instance.current, 'maximum': instance.maximum};
