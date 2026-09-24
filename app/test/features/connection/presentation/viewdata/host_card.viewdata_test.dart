import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises [HostCardViewData]'s value equality.
void main() {
  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for cards built from equal fields', () {
      final HostCardViewData first = Fixtures.buildHostCardViewData();
      final HostCardViewData second = Fixtures.buildHostCardViewData();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails when any field differs', () {
      final HostCardViewData card = Fixtures.buildHostCardViewData();
      final List<HostCardViewData> others = [
        Fixtures.buildHostCardViewData(
          host: Fixtures.buildHost(displayName: 'Other'),
        ),
        Fixtures.buildHostCardViewData(title: 'Other'),
        Fixtures.buildHostCardViewData(subtitle: 'Other'),
        Fixtures.buildHostCardViewData(detail: 'Other'),
        Fixtures.buildHostCardViewData(state: DovahConnectionCardState.repair),
      ];

      for (final HostCardViewData other in others) {
        expect(card, isNot(other));
      }
    });
  });
}
