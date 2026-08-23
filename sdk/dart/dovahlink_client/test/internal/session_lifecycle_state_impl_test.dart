import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/session_lifecycle_state_impl.dart';
import 'package:dovahlink_client_sdk/src/internal/session_state.dart';

/// Mock session state used to isolate [SessionLifecycleStateImpl]'s forwarding behavior, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionState extends Mock implements SessionState {}

/// Runs session-lifecycle-state-view behavior tests.
void main() {
  late MockSessionState state;
  late SessionLifecycleStateImpl lifecycleState;

  setUp(() {
    state = MockSessionState();
    lifecycleState = SessionLifecycleStateImpl(state);
  });

  group('Property isAdministrativelyInvalidated behaves correctly', () {
    test(
      'Property isAdministrativelyInvalidated forwards to SessionState.isAdministrativelyInvalidated',
      () {
        when(() => state.isAdministrativelyInvalidated).thenReturn(true);

        expect(lifecycleState.isAdministrativelyInvalidated, isTrue);
      },
    );
  });

  group('Property connectionGeneration behaves correctly', () {
    test(
      'Property connectionGeneration forwards to SessionState.connectionGeneration',
      () {
        when(() => state.connectionGeneration).thenReturn(3);

        expect(lifecycleState.connectionGeneration, 3);
      },
    );
  });

  group('Method bumpConnectionGeneration behaves correctly', () {
    test(
      'Method bumpConnectionGeneration forwards to SessionState.bumpGeneration',
      () {
        lifecycleState.bumpConnectionGeneration();

        verify(() => state.bumpGeneration()).called(1);
      },
    );
  });

  group('Method detachMessageSubscription behaves correctly', () {
    test(
      'Method detachMessageSubscription forwards to SessionState.detachMessageSubscription',
      () {
        final StreamSubscription<String> subscription =
            const Stream<String>.empty().listen((_) {});
        addTearDown(subscription.cancel);
        when(() => state.detachMessageSubscription()).thenReturn(subscription);

        expect(lifecycleState.detachMessageSubscription(), same(subscription));
      },
    );
  });

  group('Method resetAfterConnectionTeardown behaves correctly', () {
    test(
      'Method resetAfterConnectionTeardown forwards to SessionState.resetAfterTeardown',
      () {
        lifecycleState.resetAfterConnectionTeardown(preserveReconnecting: true);

        verify(
          () => state.resetAfterTeardown(preserveReconnecting: true),
        ).called(1);
      },
    );
  });
}
