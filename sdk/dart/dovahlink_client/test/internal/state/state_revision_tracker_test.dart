import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import '../../fixtures/fixtures.dart';
import 'mock_current_value_stream.dart';

/// Builds a revision tracker over a fresh, unsubscribed state stream.
/// @param initialState The test-specific synchronization state, when required.
/// @return An isolated tracker with its initial current-value stream.
IStateRevisionTracker<int?> buildStateRevisionTracker({
  StateSynchronization<int?>? initialState,
}) {
  final MockCurrentValueStream<StateSynchronization<int?>> state =
      buildStateSynchronizationStream(
        initialState ?? Fixtures.buildStateSynchronization<int?>(),
      );
  return StateRevisionTracker<int?>(state: state);
}

/// Builds a mocked current-value stream that records tracker updates.
/// @param initialState The synchronization view reported before an update.
/// @return A stream contract double whose value changes when [update] is called.
MockCurrentValueStream<StateSynchronization<int?>>
buildStateSynchronizationStream(StateSynchronization<int?> initialState) {
  final MockCurrentValueStream<StateSynchronization<int?>> stream =
      MockCurrentValueStream<StateSynchronization<int?>>();
  StateSynchronization<int?> currentState = initialState;
  when(() => stream.value).thenAnswer((_) => currentState);
  when(
    () => stream.stream,
  ).thenAnswer((_) => const Stream<StateSynchronization<int?>>.empty());
  when(() => stream.update(any())).thenAnswer((Invocation invocation) {
    currentState =
        invocation.positionalArguments.single as StateSynchronization<int?>;
  });
  return stream;
}

/// Runs state-revision-tracker behavior tests.
void main() {
  setUpAll(() {
    registerFallbackValue(Fixtures.buildStateSynchronization<int?>());
  });

  group('Property current behaves correctly', () {
    test('Property current starts as not subscribed', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();

      expect(tracker.current.status, DovahLinkStateStatus.notSubscribed);
      expect(tracker.current.value, isNull);
      expect(tracker.current.revision, isNull);
    });
  });

  group('Property changes behaves correctly', () {
    test('Property changes exposes the injected current-state stream', () {
      final StateSynchronization<int?> initialState =
          Fixtures.buildStateSynchronization<int?>();
      final MockCurrentValueStream<StateSynchronization<int?>> state =
          buildStateSynchronizationStream(initialState);
      const Stream<StateSynchronization<int?>> changes =
          Stream<StateSynchronization<int?>>.empty();
      when(() => state.stream).thenAnswer((_) => changes);
      final IStateRevisionTracker<int?> tracker = StateRevisionTracker<int?>(
        state: state,
      );

      expect(identical(tracker.changes, changes), isTrue);
    });
  });

  group('Method applySnapshot behaves correctly', () {
    test('Method applySnapshot reports whether a baseline was accepted', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();

      final bool accepted = tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 1,
        value: 10,
        isUnavailable: false,
      );
      final bool stale = tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 0,
        value: 0,
        isUnavailable: false,
      );

      expect(accepted, isTrue);
      expect(stale, isFalse);
    });

    test('Method applySnapshot establishes an available baseline', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();

      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 5,
        value: 10,
        isUnavailable: false,
      );

      expect(tracker.current.status, DovahLinkStateStatus.synchronized);
      expect(tracker.current.value, 10);
      expect(tracker.current.stateAuthorityId, 'authority-1');
      expect(tracker.current.playContextId, 'context-1');
      expect(tracker.current.revision, 5);
    });

    test(
      'Method applySnapshot represents a valid unavailable value as null',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();

        tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          revision: 0,
          value: null,
          isUnavailable: true,
        );

        expect(tracker.current.status, DovahLinkStateStatus.unavailable);
        expect(tracker.current.value, isNull);
      },
    );

    test('Method applySnapshot ignores duplicate and older baselines', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 2,
        value: 20,
        isUnavailable: false,
      );

      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 2,
        value: 200,
        isUnavailable: false,
      );
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 1,
        value: 10,
        isUnavailable: false,
      );

      expect(tracker.current.value, 20);
      expect(tracker.current.revision, 2);
    });

    test('Method applySnapshot detects an authority change independently', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 8,
        value: 80,
        isUnavailable: false,
      );

      tracker.applySnapshot(
        stateAuthorityId: 'authority-2',
        playContextId: 'context-1',
        revision: 1,
        value: 10,
        isUnavailable: false,
      );

      expect(tracker.current.stateAuthorityId, 'authority-2');
      expect(tracker.current.playContextId, 'context-1');
      expect(tracker.current.revision, 1);
    });

    test(
      'Method applySnapshot detects returning to no loaded play context',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
        tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          revision: 8,
          value: 80,
          isUnavailable: false,
        );

        tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          revision: 1,
          value: 10,
          isUnavailable: false,
        );

        expect(tracker.current.stateAuthorityId, 'authority-1');
        expect(tracker.current.playContextId, isNull);
        expect(tracker.current.revision, 1);
      },
    );

    test(
      'Method applySnapshot accepts the same revision during recovery and replays buffered Events',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
        tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          revision: 3,
          value: 30,
          isUnavailable: false,
        );
        tracker.beginRecovery();
        tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          baseRevision: 3,
          revision: 4,
          value: 40,
          isUnavailable: false,
        );

        tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          revision: 3,
          value: 30,
          isUnavailable: false,
        );

        expect(tracker.current.status, DovahLinkStateStatus.synchronized);
        expect(tracker.current.value, 40);
        expect(tracker.current.revision, 4);
      },
    );

    test(
      'Method applySnapshot accepts the same revision after recovery failure',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker(
          initialState: Fixtures.buildStateSynchronization<int?>(
            status: DovahLinkStateStatus.failed,
            value: 30,
            stateAuthorityId: 'authority-1',
            playContextId: null,
            revision: 3,
          ),
        );

        tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          revision: 3,
          value: 30,
          isUnavailable: false,
        );

        expect(tracker.current.status, DovahLinkStateStatus.synchronized);
        expect(tracker.current.value, 30);
      },
    );

    test('Method applySnapshot supersedes and replays buffered Events', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 1,
        value: 10,
        isUnavailable: false,
      );
      tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );
      expect(
        tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          baseRevision: 5,
          revision: 6,
          value: 60,
          isUnavailable: false,
        ),
        StateEventApplyResult.buffered,
      );

      final bool accepted = tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 5,
        value: 50,
        isUnavailable: false,
      );

      expect(accepted, isTrue);
      expect(tracker.current.status, DovahLinkStateStatus.synchronized);
      expect(tracker.current.value, 60);
      expect(tracker.current.revision, 6);
    });

    test(
      'Method applySnapshot restarts after the Event buffer reaches its limit',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
        tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          revision: 1,
          value: 10,
          isUnavailable: false,
        );
        tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        );
        for (
          int index = 0;
          index < kStateRecoveryEventBufferLimit - 1;
          index++
        ) {
          tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: null,
            baseRevision: index + 1,
            revision: index + 2,
            value: index + 2,
            isUnavailable: false,
          );
        }
        expect(
          tracker.applyEvent(
            stateAuthorityId: 'authority-1',
            playContextId: null,
            baseRevision: 128,
            revision: 129,
            value: 129,
            isUnavailable: false,
          ),
          StateEventApplyResult.recoveryRequired,
        );

        expect(
          tracker.applySnapshot(
            stateAuthorityId: 'authority-1',
            playContextId: null,
            revision: 129,
            value: 129,
            isUnavailable: false,
          ),
          isFalse,
        );
        tracker.beginRecovery();

        expect(tracker.current.status, DovahLinkStateStatus.recovering);
        expect(
          tracker.applySnapshot(
            stateAuthorityId: 'authority-1',
            playContextId: null,
            revision: 129,
            value: 129,
            isUnavailable: false,
          ),
          isTrue,
        );
        expect(tracker.current.status, DovahLinkStateStatus.synchronized);
        expect(tracker.current.value, 129);
      },
    );
  });

  group('Method applyEvent behaves correctly', () {
    test('Method applyEvent applies the next complete state revision', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 1,
        value: 10,
        isUnavailable: false,
      );

      final StateEventApplyResult result = tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        baseRevision: 1,
        revision: 2,
        value: 11,
        isUnavailable: false,
      );

      expect(result, StateEventApplyResult.applied);
      expect(tracker.current.status, DovahLinkStateStatus.synchronized);
      expect(tracker.current.value, 11);
      expect(tracker.current.revision, 2);
    });

    test('Method applyEvent ignores duplicate and stale revisions', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 2,
        value: 20,
        isUnavailable: false,
      );

      final StateEventApplyResult duplicate = tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 1,
        revision: 2,
        value: 200,
        isUnavailable: false,
      );
      final StateEventApplyResult stale = tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 0,
        revision: 1,
        value: 10,
        isUnavailable: false,
      );

      expect(duplicate, StateEventApplyResult.ignored);
      expect(stale, StateEventApplyResult.ignored);
      expect(tracker.current.value, 20);
      expect(tracker.current.revision, 2);
    });

    test(
      'Method applyEvent marks a revision gap stale and requires a snapshot',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
        tracker.applySnapshot(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          revision: 1,
          value: 10,
          isUnavailable: false,
        );

        final StateEventApplyResult result = tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          baseRevision: 4,
          revision: 5,
          value: 50,
          isUnavailable: false,
        );

        expect(result, StateEventApplyResult.recoveryRequired);
        expect(tracker.current.status, DovahLinkStateStatus.stale);
        expect(tracker.current.value, 10);
        expect(tracker.current.revision, 1);
      },
    );

    test('Method applyEvent requires a Snapshot before the first baseline', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();

      final StateEventApplyResult result = tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 0,
        revision: 1,
        value: 10,
        isUnavailable: false,
      );

      expect(result, StateEventApplyResult.recoveryRequired);
      expect(tracker.current.status, DovahLinkStateStatus.recovering);
      expect(tracker.current.value, isNull);
      expect(tracker.current.revision, isNull);
    });

    test('Method applyEvent discards a cached value when identity changes', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 4,
        value: 40,
        isUnavailable: false,
      );

      final StateEventApplyResult result = tracker.applyEvent(
        stateAuthorityId: 'authority-2',
        playContextId: 'context-2',
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );

      expect(result, StateEventApplyResult.recoveryRequired);
      expect(tracker.current.status, DovahLinkStateStatus.recovering);
      expect(tracker.current.value, isNull);
      expect(tracker.current.stateAuthorityId, 'authority-2');
      expect(tracker.current.playContextId, 'context-2');
      expect(tracker.current.revision, isNull);
    });

    test('Method applyEvent ignores newer Events until the stale baseline is '
        'restored', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 1,
        value: 10,
        isUnavailable: false,
      );
      tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 4,
        revision: 5,
        value: 50,
        isUnavailable: false,
      );

      final StateEventApplyResult result = tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 1,
        revision: 2,
        value: 20,
        isUnavailable: false,
      );

      expect(result, StateEventApplyResult.buffered);
      expect(tracker.current.status, DovahLinkStateStatus.stale);
      expect(tracker.current.value, 10);
      expect(tracker.current.revision, 1);
    });

    test('Method applyEvent ignores updates after recovery has failed', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker(
        initialState: Fixtures.buildStateSynchronization<int?>(
          status: DovahLinkStateStatus.failed,
          value: 10,
          stateAuthorityId: 'authority-1',
          playContextId: null,
          revision: 1,
        ),
      );

      final StateEventApplyResult result = tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 1,
        revision: 2,
        value: 20,
        isUnavailable: false,
      );

      expect(result, StateEventApplyResult.ignored);
      expect(tracker.current.status, DovahLinkStateStatus.failed);
      expect(tracker.current.value, 10);
      expect(tracker.current.revision, 1);
    });

    test('Method applyEvent does not accept a nonconsecutive revision', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 1,
        value: 10,
        isUnavailable: false,
      );

      final StateEventApplyResult result = tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 1,
        revision: 3,
        value: 30,
        isUnavailable: false,
      );

      expect(result, StateEventApplyResult.recoveryRequired);
      expect(tracker.current.status, DovahLinkStateStatus.stale);
      expect(tracker.current.value, 10);
      expect(tracker.current.revision, 1);
    });

    test('Method applyEvent changes status when availability changes', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        revision: 1,
        value: null,
        isUnavailable: true,
      );
      expect(tracker.current.status, DovahLinkStateStatus.unavailable);

      tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 1,
        revision: 2,
        value: 20,
        isUnavailable: false,
      );
      expect(tracker.current.status, DovahLinkStateStatus.synchronized);
      expect(tracker.current.value, 20);

      tracker.applyEvent(
        stateAuthorityId: 'authority-1',
        playContextId: null,
        baseRevision: 2,
        revision: 3,
        value: null,
        isUnavailable: true,
      );

      expect(tracker.current.status, DovahLinkStateStatus.unavailable);
      expect(tracker.current.value, isNull);
    });

    test(
      'Method applyEvent requests recovery without repeating the transition',
      () {
        final MockCurrentValueStream<StateSynchronization<int?>> state =
            buildStateSynchronizationStream(
              Fixtures.buildStateSynchronization<int?>(
                status: DovahLinkStateStatus.recovering,
                stateAuthorityId: 'authority-1',
                playContextId: null,
              ),
            );
        final IStateRevisionTracker<int?> tracker = StateRevisionTracker<int?>(
          state: state,
        );

        final StateEventApplyResult first = tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          baseRevision: 0,
          revision: 1,
          value: 10,
          isUnavailable: false,
        );
        final StateEventApplyResult repeated = tracker.applyEvent(
          stateAuthorityId: 'authority-1',
          playContextId: null,
          baseRevision: 1,
          revision: 2,
          value: 20,
          isUnavailable: false,
        );

        expect(first, StateEventApplyResult.buffered);
        expect(repeated, StateEventApplyResult.buffered);
        expect(tracker.current.status, DovahLinkStateStatus.recovering);
        expect(tracker.current.revision, isNull);
        verifyNever(() => state.update(any()));
      },
    );
  });

  group('Method beginRecovery behaves correctly', () {
    test(
      'Method beginRecovery marks the baseline recovering and preserves it',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker(
          initialState: Fixtures.buildStateSynchronization<int?>(
            status: DovahLinkStateStatus.stale,
            value: 10,
            stateAuthorityId: 'authority-1',
            playContextId: null,
            revision: 1,
          ),
        );

        tracker.beginRecovery();

        expect(tracker.current.status, DovahLinkStateStatus.recovering);
        expect(tracker.current.value, 10);
        expect(tracker.current.stateAuthorityId, 'authority-1');
        expect(tracker.current.revision, 1);
      },
    );
  });

  group('Method resetToNotSubscribed behaves correctly', () {
    test('Method resetToNotSubscribed clears the cached baseline', () {
      final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
      tracker.applySnapshot(
        stateAuthorityId: 'authority-1',
        playContextId: 'context-1',
        revision: 9,
        value: 90,
        isUnavailable: false,
      );

      tracker.resetToNotSubscribed();

      expect(tracker.current.status, DovahLinkStateStatus.notSubscribed);
      expect(tracker.current.value, isNull);
      expect(tracker.current.stateAuthorityId, isNull);
      expect(tracker.current.playContextId, isNull);
      expect(tracker.current.revision, isNull);
    });

    test(
      'Method resetToNotSubscribed is a no-op before any baseline exists',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker();
        final StateSynchronization<int?> initial = tracker.current;

        tracker.resetToNotSubscribed();

        expect(identical(tracker.current, initial), isTrue);
      },
    );
  });

  group('Method failRecovery behaves correctly', () {
    test(
      'Method failRecovery marks failure and preserves the last known state',
      () {
        final IStateRevisionTracker<int?> tracker = buildStateRevisionTracker(
          initialState: Fixtures.buildStateSynchronization<int?>(
            status: DovahLinkStateStatus.recovering,
            value: 10,
            stateAuthorityId: 'authority-1',
            playContextId: 'context-1',
            revision: 1,
          ),
        );

        tracker.failRecovery();

        expect(tracker.current.status, DovahLinkStateStatus.failed);
        expect(tracker.current.value, 10);
        expect(tracker.current.stateAuthorityId, 'authority-1');
        expect(tracker.current.playContextId, 'context-1');
        expect(tracker.current.revision, 1);
      },
    );
  });
}
