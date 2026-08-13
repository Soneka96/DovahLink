import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/data/models/resource_value.model.dart';
import 'package:dovahlink_client/features/connection/domain/entities/resource_value.entity.dart';

/// Exercises resource-value model mapping and validation.
void main() {
  group('ResourceValueModel', () {
    test('maps JSON and is usable as its entity', () {
      const Map<String, dynamic> json = {'current': 180.0, 'maximum': 220.0};

      final ResourceValueModel model = ResourceValueModel.fromJson(json);

      expect(model, isA<ResourceValueEntity>());
      expect(model.current, 180.0);
      expect(model.maximum, 220.0);
      expect(model.toJson(), json);
    });

    test('rejects missing and non-finite values', () {
      expect(
        () => ResourceValueModel.fromJson({'current': 1}),
        throwsA(isA<FormatException>()),
      );
      expect(
        () => ResourceValueModel.fromJson({
          'current': double.nan,
          'maximum': 2,
        }),
        throwsA(isA<FormatException>()),
      );
    });
  });
}
