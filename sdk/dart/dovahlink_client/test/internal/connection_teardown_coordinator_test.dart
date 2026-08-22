import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_failure_handler.dart';
import 'package:dovahlink_client_sdk/src/internal/session_lifecycle_state.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Mock transport used to isolate teardown coordination from socket I/O.
class MockDovahLinkTransport extends Mock implements DovahLinkTransport {}

/// Mock pending-operation failure handler used to capture teardown failure policy.
class MockPendingOperationFailureHandler extends Mock
    implements PendingOperationFailureHandler {}

/// Minimal session-owned state port used to verify coordinator transitions.
class FakeSessionLifecycleState implements SessionLifecycleState {
  /// Whether teardown should treat the session as administratively invalidated.
  bool administrativelyInvalidated = false;

  /// The current connection generation.
  int generation = 0;

  /// The subscription currently owned by the fake session.
  StreamSubscription<String>? subscription;

  /// Whether the coordinator requested the disconnected-state reset.
  bool resetCalled = false;

  /// Implements [SessionLifecycleState.isAdministrativelyInvalidated].
  @override
  bool get isAdministrativelyInvalidated => administrativelyInvalidated;

  /// Implements [SessionLifecycleState.connectionGeneration].
  @override
  int get connectionGeneration => generation;

  /// Implements [SessionLifecycleState.bumpConnectionGeneration].
  @override
  void bumpConnectionGeneration() {
    generation++;
  }

  /// Implements [SessionLifecycleState.detachMessageSubscription].
  @override
  StreamSubscription<String>? detachMessageSubscription() {
    final StreamSubscription<String>? detached = subscription;
    subscription = null;
    return detached;
  }

  /// Implements [SessionLifecycleState.resetAfterConnectionTeardown].
  @override
  void resetAfterConnectionTeardown() {
    resetCalled = true;
  }
}

/// Builds a coordinator from the supplied test doubles and state port.
ConnectionTeardownCoordinator buildCoordinator({
  required DovahLinkTransport transport,
  required PendingOperationFailureHandler pendingOperationFailureHandler,
  required SessionLifecycleState state,
}) => ConnectionTeardownCoordinator(
  transport: transport,
  lifecycleQueue: LifecycleOperationQueue(),
  pendingOperationFailureHandler: pendingOperationFailureHandler,
  state: state,
);

/// Runs connection-teardown coordinator behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockPendingOperationFailureHandler pendingOperationFailureHandler;
  late FakeSessionLifecycleState state;

  setUpAll(() {
    registerFallbackValue(Exception('fallback for any()'));
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    pendingOperationFailureHandler = MockPendingOperationFailureHandler();
    state = FakeSessionLifecycleState();
    when(() => transport.close()).thenAnswer((_) async {});
  });

  group('Method tearDown behaves correctly', () {
    test(
      'Method tearDown closes resources, resets state, and fails pending operations',
      () async {
        final StreamController<String> messages = StreamController<String>();
        addTearDown(messages.close);
        state.subscription = messages.stream.listen((_) {});
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('connection lost'),
        );

        expect(state.generation, 1);
        expect(state.subscription, isNull);
        expect(state.resetCalled, isTrue);
        verifyInOrder([
          () => transport.close(),
          () => pendingOperationFailureHandler.failAll(
            any(),
            orphanRetrySafeOperations: true,
          ),
        ]);
      },
    );

    test(
      'Method tearDown passes false orphaning policy through to pending-operation failure',
      () async {
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('deliberate disconnect'),
          orphanRetrySafeOperations: false,
        );

        verify(
          () => pendingOperationFailureHandler.failAll(
            any(),
            orphanRetrySafeOperations: false,
          ),
        ).called(1);
      },
    );

    test(
      'Method tearDown resets state even when transport close fails',
      () async {
        when(
          () => transport.close(),
        ).thenAnswer((_) async => throw StateError('close failed'));
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('connection lost'),
        );

        expect(state.resetCalled, isTrue);
        verify(
          () => pendingOperationFailureHandler.failAll(
            any(),
            orphanRetrySafeOperations: true,
          ),
        ).called(1);
      },
    );

    test(
      'Method tearDown continues cleanup when subscription cancellation fails',
      () async {
        final StreamController<String> messages = StreamController<String>(
          onCancel: () => throw StateError('cancel failed'),
        );
        addTearDown(messages.close);
        state.subscription = messages.stream.listen((_) {});
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('connection lost'),
        );

        expect(state.resetCalled, isTrue);
        verify(() => transport.close()).called(1);
        verify(
          () => pendingOperationFailureHandler.failAll(
            any(),
            orphanRetrySafeOperations: true,
          ),
        ).called(1);
      },
    );

    test(
      'Method tearDown does nothing after administrative invalidation',
      () async {
        state.administrativelyInvalidated = true;
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('late transport close'),
        );

        expect(state.generation, 0);
        expect(state.resetCalled, isFalse);
        verifyNever(() => transport.close());
        verifyNever(
          () => pendingOperationFailureHandler.failAll(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );
  });

  group('Method closeAfterInvalidation behaves correctly', () {
    test('Method closeAfterInvalidation ignores a stale generation', () async {
      state.generation = 2;
      final ConnectionTeardownCoordinator coordinator = buildCoordinator(
        transport: transport,
        pendingOperationFailureHandler: pendingOperationFailureHandler,
        state: state,
      );

      await coordinator.closeAfterInvalidation(1);

      verifyNever(() => transport.close());
      expect(state.resetCalled, isFalse);
    });

    test(
      'Method closeAfterInvalidation does not close a newer generation after cancellation yields',
      () async {
        final Completer<void> cancellation = Completer<void>();
        final StreamController<String> messages = StreamController<String>(
          onCancel: () => cancellation.future,
        );
        addTearDown(messages.close);
        state.generation = 1;
        state.subscription = messages.stream.listen((_) {});
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        final Future<void> closeFuture = coordinator.closeAfterInvalidation(1);
        await pumpEventQueue();
        state.generation = 2;
        cancellation.complete();
        await closeFuture;

        verifyNever(() => transport.close());
      },
    );

    test(
      'Method closeAfterInvalidation closes the current generation without resetting state',
      () async {
        final StreamController<String> messages = StreamController<String>();
        addTearDown(messages.close);
        state.generation = 1;
        state.subscription = messages.stream.listen((_) {});
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.closeAfterInvalidation(1);

        expect(state.subscription, isNull);
        expect(state.resetCalled, isFalse);
        verify(() => transport.close()).called(1);
        verifyNever(
          () => pendingOperationFailureHandler.failAll(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );
  });
}
