import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/session/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/session/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Mock transport used to isolate teardown coordination from socket I/O.
class MockDovahLinkTransport extends Mock implements DovahLinkTransport {}

/// Mock session state used to verify coordinator transitions, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries". Stubbed with closures over this test
/// file's own local variables to simulate the same statefulness a real implementation would have.
class MockSessionState extends Mock implements SessionState {}

/// Mock lifecycle queue used per `ai/context/sdk/testing.md`'s "Service test boundaries" -- the
/// queue's own scheduling behavior stays owned by `lifecycle_operation_queue_test.dart`. Stubbed to
/// delegate to a real, test-local [LifecycleOperationQueue] instance purely so this file's
/// queued-call tests exercise [ConnectionTeardownCoordinator]'s own generation-check logic under
/// genuine concurrent-call scheduling, without the coordinator itself ever depending on anything
/// but the mock.
class MockLifecycleOperationQueue extends Mock
    implements LifecycleOperationQueue {}

/// Builds a coordinator from the supplied test doubles and state.
ConnectionTeardownCoordinator buildCoordinator({
  required DovahLinkTransport transport,
  required LifecycleOperationQueue lifecycleQueue,
  required void Function(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  })
  pendingOperationFailureHandler,
  required SessionState state,
}) => ConnectionTeardownCoordinator(
  transport: transport,
  lifecycleQueue: lifecycleQueue,
  pendingOperationFailureHandler: pendingOperationFailureHandler,
  state: state,
);

/// Runs connection-teardown coordinator behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockLifecycleOperationQueue lifecycleQueue;
  late List<Exception> failedReasons;
  late List<bool> failedOrphanFlags;
  late void Function(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  })
  pendingOperationFailureHandler;
  late MockSessionState state;
  late bool administrativelyInvalidated;
  late int generation;
  late StreamSubscription<String>? subscription;
  late bool resetCalled;
  late bool? lastPreserveReconnecting;

  setUp(() {
    transport = MockDovahLinkTransport();
    final LifecycleOperationQueue realScheduling = LifecycleOperationQueue();
    lifecycleQueue = MockLifecycleOperationQueue();
    when(() => lifecycleQueue.run(any())).thenAnswer(
      (Invocation invocation) => realScheduling.run(
        invocation.positionalArguments[0] as Future<void> Function(),
      ),
    );
    failedReasons = <Exception>[];
    failedOrphanFlags = <bool>[];
    pendingOperationFailureHandler =
        (Exception reason, {required bool orphanRetrySafeOperations}) {
          failedReasons.add(reason);
          failedOrphanFlags.add(orphanRetrySafeOperations);
        };
    administrativelyInvalidated = false;
    generation = 0;
    subscription = null;
    resetCalled = false;
    lastPreserveReconnecting = null;
    state = MockSessionState();
    when(
      () => state.isAdministrativelyInvalidated,
    ).thenAnswer((_) => administrativelyInvalidated);
    when(() => state.connectionGeneration).thenAnswer((_) => generation);
    when(() => state.bumpGeneration()).thenAnswer((_) => generation++);
    when(() => state.detachMessageSubscription()).thenAnswer((_) {
      final StreamSubscription<String>? detached = subscription;
      subscription = null;
      return detached;
    });
    when(
      () => state.resetAfterTeardown(
        preserveReconnecting: any(named: 'preserveReconnecting'),
      ),
    ).thenAnswer((Invocation invocation) {
      resetCalled = true;
      lastPreserveReconnecting =
          invocation.namedArguments[#preserveReconnecting] as bool;
    });
    when(() => transport.close()).thenAnswer((_) async {});
  });

  group('Method tearDown behaves correctly', () {
    test(
      'Method tearDown closes resources, resets state, and fails pending operations',
      () async {
        final List<String> order = <String>[];
        when(() => transport.close()).thenAnswer((_) async {
          order.add('close');
        });
        final StreamController<String> messages = StreamController<String>();
        addTearDown(messages.close);
        subscription = messages.stream.listen((_) {});
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler:
              (Exception reason, {required bool orphanRetrySafeOperations}) {
                order.add('failAll');
                pendingOperationFailureHandler(
                  reason,
                  orphanRetrySafeOperations: orphanRetrySafeOperations,
                );
              },
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('connection lost'),
        );

        expect(generation, 1);
        expect(subscription, isNull);
        expect(resetCalled, isTrue);
        expect(lastPreserveReconnecting, isTrue);
        expect(order, <String>['close', 'failAll']);
        expect(failedOrphanFlags, <bool>[true]);
      },
    );

    test(
      'Method tearDown passes false orphaning policy through to pending-operation failure',
      () async {
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('deliberate disconnect'),
          orphanRetrySafeOperations: false,
        );

        expect(failedOrphanFlags, <bool>[false]);
        expect(lastPreserveReconnecting, isFalse);
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
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('connection lost'),
        );

        expect(resetCalled, isTrue);
        expect(failedOrphanFlags, <bool>[true]);
      },
    );

    test(
      'Method tearDown continues cleanup when subscription cancellation fails',
      () async {
        final StreamController<String> messages = StreamController<String>(
          onCancel: () => throw StateError('cancel failed'),
        );
        addTearDown(messages.close);
        subscription = messages.stream.listen((_) {});
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('connection lost'),
        );

        expect(resetCalled, isTrue);
        verify(() => transport.close()).called(1);
        expect(failedOrphanFlags, <bool>[true]);
      },
    );

    test(
      'Method tearDown does nothing after administrative invalidation',
      () async {
        administrativelyInvalidated = true;
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.tearDown(
          const DovahLinkConnectionException('late transport close'),
        );

        expect(generation, 0);
        expect(resetCalled, isFalse);
        verifyNever(() => transport.close());
        expect(failedReasons, isEmpty);
      },
    );

    test('Method tearDown is a no-op when queued behind an earlier call for the same connection '
        'generation', () async {
      // Mirrors a stream's onError and onDone both firing for one dead subscription: both calls
      // are issued before either has run, so the second must not double-run cleanup or failAll.
      final ConnectionTeardownCoordinator coordinator = buildCoordinator(
        transport: transport,
        lifecycleQueue: lifecycleQueue,
        pendingOperationFailureHandler: pendingOperationFailureHandler,
        state: state,
      );

      final Future<void> first = coordinator.tearDown(
        const DovahLinkConnectionException('onError'),
      );
      final Future<void> second = coordinator.tearDown(
        const DovahLinkConnectionException('onDone'),
      );
      await first;
      await second;

      expect(generation, 1);
      verify(() => transport.close()).called(1);
      expect(failedReasons, hasLength(1));
      expect(
        (failedReasons.single as DovahLinkConnectionException).message,
        'onError',
      );
    });

    test(
      'Method tearDown is a no-op three calls deep when all three are queued behind the first',
      () async {
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        final Future<void> first = coordinator.tearDown(
          const DovahLinkConnectionException('onError'),
        );
        final Future<void> second = coordinator.tearDown(
          const DovahLinkConnectionException('onDone'),
        );
        final Future<void> third = coordinator.tearDown(
          const DovahLinkConnectionException('late timeout'),
        );
        await first;
        await second;
        await third;

        expect(generation, 1);
        verify(() => transport.close()).called(1);
        expect(failedReasons, hasLength(1));
      },
    );

    test(
      'Method tearDown still defers to a racing administrative invalidation for every queued call',
      () async {
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        final Future<void> first = coordinator.tearDown(
          const DovahLinkConnectionException('onError'),
        );
        final Future<void> second = coordinator.tearDown(
          const DovahLinkConnectionException('onDone'),
        );
        // Mirrors onSessionInvalidated setting this flag directly (bypassing tearDown) while both
        // calls above are still queued, before either's body has run.
        administrativelyInvalidated = true;
        await first;
        await second;

        expect(generation, 0);
        verifyNever(() => transport.close());
        expect(failedReasons, isEmpty);
      },
    );

    test('Method tearDown runs again when called after an earlier call for this generation fully '
        'completed', () async {
      // Mirrors a deliberate second teardown issued only after the first finished -- for example
      // finalizing operations an earlier, still-recovering teardown preserved for retry. Must not
      // be mistaken for the queued-behind-an-earlier-call race the previous test guards against.
      final ConnectionTeardownCoordinator coordinator = buildCoordinator(
        transport: transport,
        lifecycleQueue: lifecycleQueue,
        pendingOperationFailureHandler: pendingOperationFailureHandler,
        state: state,
      );

      await coordinator.tearDown(
        const DovahLinkConnectionException('first'),
        orphanRetrySafeOperations: true,
      );
      await coordinator.tearDown(
        const DovahLinkConnectionException('second'),
        orphanRetrySafeOperations: false,
      );

      expect(generation, 2);
      verify(() => transport.close()).called(2);
      expect(failedOrphanFlags, <bool>[true, false]);
    });
  });

  group('Method closeAfterInvalidation behaves correctly', () {
    test('Method closeAfterInvalidation ignores a stale generation', () async {
      generation = 2;
      final ConnectionTeardownCoordinator coordinator = buildCoordinator(
        transport: transport,
        lifecycleQueue: lifecycleQueue,
        pendingOperationFailureHandler: pendingOperationFailureHandler,
        state: state,
      );

      await coordinator.closeAfterInvalidation(1);

      verifyNever(() => transport.close());
      expect(resetCalled, isFalse);
    });

    test(
      'Method closeAfterInvalidation does not close a newer generation after cancellation yields',
      () async {
        final Completer<void> cancellation = Completer<void>();
        final StreamController<String> messages = StreamController<String>(
          onCancel: () => cancellation.future,
        );
        addTearDown(messages.close);
        generation = 1;
        subscription = messages.stream.listen((_) {});
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        final Future<void> closeFuture = coordinator.closeAfterInvalidation(1);
        await pumpEventQueue();
        generation = 2;
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
        generation = 1;
        subscription = messages.stream.listen((_) {});
        final ConnectionTeardownCoordinator coordinator = buildCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler: pendingOperationFailureHandler,
          state: state,
        );

        await coordinator.closeAfterInvalidation(1);

        expect(subscription, isNull);
        expect(resetCalled, isFalse);
        verify(() => transport.close()).called(1);
        expect(failedReasons, isEmpty);
      },
    );
  });
}
