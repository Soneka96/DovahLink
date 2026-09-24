import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/models/host_card.model.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

import '../../../../fixtures/fixtures.dart';

/// Exercises [HostCardModel]'s value equality.
void main() {
  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for cards built from equal fields', () {
      final HostCardModel first = Fixtures.buildHostCardModel();
      final HostCardModel second = Fixtures.buildHostCardModel();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails when any field differs', () {
      final HostCardModel card = Fixtures.buildHostCardModel();
      final List<HostCardModel> others = [
        Fixtures.buildHostCardModel(
          host: Fixtures.buildHost(displayName: 'Other'),
        ),
        Fixtures.buildHostCardModel(title: 'Other'),
        Fixtures.buildHostCardModel(subtitle: 'Other'),
        Fixtures.buildHostCardModel(detail: 'Other'),
        Fixtures.buildHostCardModel(state: DovahConnectionCardState.repair),
      ];

      for (final HostCardModel other in others) {
        expect(card, isNot(other));
      }
    });
  });
}
