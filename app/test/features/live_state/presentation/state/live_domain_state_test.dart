import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';

/// Exercises one app-owned synchronization-aware domain projection.
void main() {
  group('LiveDomainState constructor behaves correctly', () {
    test(
      'LiveDomainState constructor retains stale value and authority metadata',
      () {
        const LiveDomainState<double> state = LiveDomainState<double>(
          status: LiveStateStatus.stale,
          value: 73.5,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 12,
        );

        expect(state.status, isA<LiveStateStatus>());
        expect(state.status, LiveStateStatus.stale);
        expect(state.value, isA<double>());
        expect(state.value, 73.5);
        expect(state.stateAuthorityId, 'authority-a');
        expect(state.playContextId, 'context-a');
        expect(state.revision, 12);
      },
    );
  });

  group('LiveDomainState notSubscribed behaves correctly', () {
    test(
      'LiveDomainState notSubscribed has no value or authority metadata',
      () {
        const LiveDomainState<double> state =
            LiveDomainState<double>.notSubscribed();

        expect(state.status, LiveStateStatus.notSubscribed);
        expect(state.value, isNull);
        expect(state.stateAuthorityId, isNull);
        expect(state.playContextId, isNull);
        expect(state.revision, isNull);
      },
    );
  });

  group('LiveDomainState equality behaves correctly', () {
    test('LiveDomainState equality considers every synchronization field', () {
      const LiveDomainState<double> current = LiveDomainState<double>(
        status: LiveStateStatus.synchronized,
        value: 73.5,
        stateAuthorityId: 'authority-a',
        playContextId: 'context-a',
        revision: 12,
      );
      expect(
        current ==
            const LiveDomainState<double>(
              status: LiveStateStatus.stale,
              value: 73.5,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 12,
            ),
        isFalse,
      );
      expect(
        current ==
            const LiveDomainState<double>(
              status: LiveStateStatus.synchronized,
              value: 74.5,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 12,
            ),
        isFalse,
      );
      expect(
        current ==
            const LiveDomainState<double>(
              status: LiveStateStatus.synchronized,
              value: 73.5,
              stateAuthorityId: 'authority-b',
              playContextId: 'context-a',
              revision: 12,
            ),
        isFalse,
      );
      expect(
        current ==
            const LiveDomainState<double>(
              status: LiveStateStatus.synchronized,
              value: 73.5,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-b',
              revision: 12,
            ),
        isFalse,
      );
      expect(
        current ==
            const LiveDomainState<double>(
              status: LiveStateStatus.synchronized,
              value: 73.5,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 13,
            ),
        isFalse,
      );
    });
  });
}
