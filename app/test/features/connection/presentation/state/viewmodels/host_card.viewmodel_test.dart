import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/host_card.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

import '../../../../../fixtures/fixtures.dart';

/// Exercises [HostCardViewModel]'s value equality.
void main() {
  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for cards built from equal fields', () {
      final HostCardViewModel first = Fixtures.buildHostCardViewModel();
      final HostCardViewModel second = Fixtures.buildHostCardViewModel();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails when any field differs', () {
      final HostCardViewModel card = Fixtures.buildHostCardViewModel();
      final List<HostCardViewModel> others = [
        Fixtures.buildHostCardViewModel(
          host: Fixtures.buildHostEntity(displayName: 'Other'),
        ),
        Fixtures.buildHostCardViewModel(title: 'Other'),
        Fixtures.buildHostCardViewModel(subtitle: 'Other'),
        Fixtures.buildHostCardViewModel(detail: 'Other'),
        Fixtures.buildHostCardViewModel(state: DovahConnectionCardState.repair),
      ];

      for (final HostCardViewModel other in others) {
        expect(card, isNot(other));
      }
    });
  });
}
