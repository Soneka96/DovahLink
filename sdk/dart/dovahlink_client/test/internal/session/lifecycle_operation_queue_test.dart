import 'dart:async';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/session/lifecycle_operation_queue.dart';

/// Runs lifecycle-operation queue behavior tests.
void main() {
  late LifecycleOperationQueue queue;

  setUp(() {
    queue = LifecycleOperationQueue();
  });

  group('Method run behaves correctly', () {
    test('Method run serializes operations in submission order', () async {
      final Completer<void> firstGate = Completer<void>();
      final List<String> order = <String>[];

      final Future<void> first = queue.run(() async {
        order.add('first-start');
        await firstGate.future;
        order.add('first-end');
      });
      final Future<void> second = queue.run(() async {
        order.add('second');
      });

      await pumpEventQueue();
      expect(order, <String>['first-start']);

      firstGate.complete();
      await first;
      await second;

      expect(order, <String>['first-start', 'first-end', 'second']);
    });

    test(
      'Method run continues with later operations after a failure',
      () async {
        final List<String> order = <String>[];
        final Future<void> first = queue.run(() async {
          order.add('first');
          throw StateError('first failed');
        });
        final Future<void> second = queue.run(() async {
          order.add('second');
        });

        await expectLater(first, throwsStateError);
        await expectLater(second, completes);
        expect(order, <String>['first', 'second']);
      },
    );
  });
}
