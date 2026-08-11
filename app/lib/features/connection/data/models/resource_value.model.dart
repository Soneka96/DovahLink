import 'package:json_annotation/json_annotation.dart';

import '../../domain/entities/resource_value.entity.dart';
import 'json_map.dart';
import 'protocol_format_exception.dart';

part 'resource_value.model.g.dart';

@JsonSerializable(checked: true)
/// A generated JSON adapter for [ResourceValueEntity].
class ResourceValueModel extends ResourceValueEntity {
  /// Creates a resource model in game units.
  const ResourceValueModel({required super.current, required super.maximum});

  /// Decodes and validates one resource value.
  factory ResourceValueModel.fromJson(JsonMap json) {
    try {
      final ResourceValueModel model = _$ResourceValueModelFromJson(json);
      if (!model.current.isFinite || !model.maximum.isFinite) {
        throw const FormatException('resource values must be finite');
      }
      return model;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid resource value: $error');
    }
  }

  /// Encodes this resource value as a JSON object.
  JsonMap toJson() => _$ResourceValueModelToJson(this);
}
