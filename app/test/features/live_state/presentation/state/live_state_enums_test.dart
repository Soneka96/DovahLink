import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';

/// Exercises live-state enums used at the SDK/application boundary.
void main() {
  group('LiveStateStatus behaves correctly', () {
    test('LiveStateStatus preserves every SDK synchronization case', () {
      expect(LiveStateStatus.values, [
        LiveStateStatus.notSubscribed,
        LiveStateStatus.unavailable,
        LiveStateStatus.synchronized,
        LiveStateStatus.stale,
        LiveStateStatus.recovering,
        LiveStateStatus.failed,
      ]);
    });
  });

  group('LiveCellKind behaves correctly', () {
    test('LiveCellKind preserves interior and exterior meanings', () {
      expect(LiveCellKind.values, [
        LiveCellKind.interior,
        LiveCellKind.exterior,
      ]);
    });
  });

  group('LiveQuestObjectiveStatus behaves correctly', () {
    test('LiveQuestObjectiveStatus preserves every objective state', () {
      expect(LiveQuestObjectiveStatus.values, [
        LiveQuestObjectiveStatus.dormant,
        LiveQuestObjectiveStatus.displayed,
        LiveQuestObjectiveStatus.completed,
        LiveQuestObjectiveStatus.completedAndDisplayed,
        LiveQuestObjectiveStatus.failed,
        LiveQuestObjectiveStatus.failedAndDisplayed,
      ]);
    });
  });
}
