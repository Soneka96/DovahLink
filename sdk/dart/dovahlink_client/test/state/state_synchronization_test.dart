import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import '../fixtures/fixtures.dart';

/// Runs [StateSynchronization] value behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor preserves the supplied state', () {
      final StateSynchronization<int> state =
          Fixtures.buildStateSynchronization<int>(
            status: DovahLinkStateStatus.synchronized,
            value: 12,
            stateAuthorityId: 'authority-1',
            playContextId: null,
            revision: 4,
          );

      expect(state.status, DovahLinkStateStatus.synchronized);
      expect(state.value, 12);
      expect(state.stateAuthorityId, 'authority-1');
      expect(state.playContextId, isNull);
      expect(state.revision, 4);
    });

    test(
      'Method constructor keeps a retained value visible while unavailable',
      () {
        final StateSynchronization<int> state =
            Fixtures.buildStateSynchronization<int>(
              status: DovahLinkStateStatus.unavailable,
              value: 12,
              stateAuthorityId: 'authority-1',
              playContextId: 'context-1',
              revision: 5,
            );

        expect(state.status, DovahLinkStateStatus.unavailable);
        expect(state.value, 12);
        expect(state.revision, 5);
      },
    );
  });
}
