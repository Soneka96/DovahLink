import 'dart:async';

import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/shared/utils/app_shutdown_service.dart';
import 'package:dovahlink_client/shared/utils/existing_dovahlink_client.dart';

/// Mocks app-owned pairing recovery cleanup.
class MockPairingMiddleware extends Mock implements IPairingMiddleware {}

/// Mocks cancellation of the connection middleware's SDK stream listener.
class MockConnectionMiddleware extends Mock implements IConnectionMiddleware {}

/// Mocks shutdown access to an existing SDK client.
class MockExistingDovahLinkClient extends Mock
    implements IExistingDovahLinkClient {}

/// Exercises shared cleanup ordering, deduplication, failures, and timeout behavior.
void main() {
  late MockPairingMiddleware pairingMiddleware;
  late MockConnectionMiddleware connectionMiddleware;
  late MockExistingDovahLinkClient existingClient;
  late AppShutdownService service;

  setUp(() {
    pairingMiddleware = MockPairingMiddleware();
    connectionMiddleware = MockConnectionMiddleware();
    existingClient = MockExistingDovahLinkClient();
    when(() => pairingMiddleware.shutdown()).thenAnswer((_) async {});
    when(() => connectionMiddleware.shutdown()).thenAnswer((_) async {});
    when(() => existingClient.closeIfCreated()).thenAnswer((_) async {});
    service = AppShutdownService(
      connectionMiddleware: connectionMiddleware,
      pairingMiddleware: pairingMiddleware,
      existingClient: existingClient,
    );
  });

  group('Method shutdown behaves correctly', () {
    test(
      'Method shutdown stops middleware before closing an existing client',
      () async {
        final List<String> cleanupOrder = <String>[];
        when(() => pairingMiddleware.shutdown()).thenAnswer((_) async {
          cleanupOrder.add('pairing');
        });
        when(() => connectionMiddleware.shutdown()).thenAnswer((_) async {
          cleanupOrder.add('connection');
        });
        when(() => existingClient.closeIfCreated()).thenAnswer((_) async {
          cleanupOrder.add('client');
        });

        await service.shutdown();

        expect(cleanupOrder, <String>['connection', 'pairing', 'client']);
      },
    );

    test(
      'Method shutdown shares one cleanup operation across simultaneous callers',
      () async {
        final Completer<void> disconnectCompleter = Completer<void>();
        when(
          () => existingClient.closeIfCreated(),
        ).thenAnswer((_) => disconnectCompleter.future);

        final Future<void> first = service.shutdown();
        final Future<void> second = service.shutdown();

        expect(identical(first, second), isTrue);
        disconnectCompleter.complete();
        await Future.wait(<Future<void>>[first, second]);
        verify(() => pairingMiddleware.shutdown()).called(1);
        verify(() => connectionMiddleware.shutdown()).called(1);
        verify(() => existingClient.closeIfCreated()).called(1);
      },
    );

    test('Method shutdown contains synchronous cleanup failures', () async {
      when(
        () => pairingMiddleware.shutdown(),
      ).thenThrow(StateError('pairing cleanup failed'));
      when(
        () => existingClient.closeIfCreated(),
      ).thenThrow(StateError('client close failed'));

      await expectLater(service.shutdown(), completes);
      verify(() => pairingMiddleware.shutdown()).called(1);
      verify(() => connectionMiddleware.shutdown()).called(1);
      verify(() => existingClient.closeIfCreated()).called(1);
    });

    test('Method shutdown contains asynchronous cleanup failures', () async {
      when(() => pairingMiddleware.shutdown()).thenAnswer((_) async {
        throw StateError('pairing cleanup failed');
      });
      when(() => existingClient.closeIfCreated()).thenAnswer((_) async {
        throw StateError('client close failed');
      });

      await expectLater(service.shutdown(), completes);
      verify(() => pairingMiddleware.shutdown()).called(1);
      verify(() => connectionMiddleware.shutdown()).called(1);
      verify(() => existingClient.closeIfCreated()).called(1);
    });

    test('Method shutdown completes when client close exceeds its budget', () {
      fakeAsync((FakeAsync async) {
        final Completer<void> disconnectCompleter = Completer<void>();
        bool disconnectCompleted = false;
        when(() => existingClient.closeIfCreated()).thenAnswer(
          (_) => disconnectCompleter.future.whenComplete(() {
            disconnectCompleted = true;
          }),
        );
        bool shutdownCompleted = false;
        service.shutdown().then((_) => shutdownCompleted = true);
        async.flushMicrotasks();

        expect(shutdownCompleted, isFalse);
        async.elapse(const Duration(seconds: 3));
        async.flushMicrotasks();

        expect(shutdownCompleted, isTrue);
        expect(disconnectCompleted, isFalse);
        disconnectCompleter.complete();
        async.flushMicrotasks();
        expect(disconnectCompleted, isTrue);
        verify(() => pairingMiddleware.shutdown()).called(1);
        verify(() => connectionMiddleware.shutdown()).called(1);
        verify(() => existingClient.closeIfCreated()).called(1);
      });
    });

    test(
      'Method shutdown completes when connection subscription cancellation exceeds its budget',
      () {
        fakeAsync((FakeAsync async) {
          final Completer<void> connectionCompleter = Completer<void>();
          when(
            () => connectionMiddleware.shutdown(),
          ).thenAnswer((_) => connectionCompleter.future);
          bool shutdownCompleted = false;
          service.shutdown().then((_) => shutdownCompleted = true);
          async.flushMicrotasks();

          expect(shutdownCompleted, isFalse);
          async.elapse(const Duration(seconds: 3));
          async.flushMicrotasks();

          expect(shutdownCompleted, isTrue);
          connectionCompleter.complete();
          async.flushMicrotasks();
          verify(() => connectionMiddleware.shutdown()).called(1);
          verify(() => pairingMiddleware.shutdown()).called(1);
          verify(() => existingClient.closeIfCreated()).called(1);
        });
      },
    );

    test(
      'Method shutdown starts client close before stalled pairing cleanup uses its budget',
      () {
        fakeAsync((FakeAsync async) {
          final Completer<void> pairingCompleter = Completer<void>();
          final Completer<void> disconnectCompleter = Completer<void>();
          when(
            () => pairingMiddleware.shutdown(),
          ).thenAnswer((_) => pairingCompleter.future);
          bool shutdownCompleted = false;
          bool disconnectStarted = false;
          when(() => existingClient.closeIfCreated()).thenAnswer((_) {
            disconnectStarted = true;
            return disconnectCompleter.future;
          });
          service.shutdown().then((_) => shutdownCompleted = true);
          async.flushMicrotasks();

          expect(shutdownCompleted, isFalse);
          expect(disconnectStarted, isTrue);
          async.elapse(const Duration(seconds: 3));
          async.flushMicrotasks();

          expect(shutdownCompleted, isTrue);
          pairingCompleter.complete();
          disconnectCompleter.complete();
          async.flushMicrotasks();
          verify(() => existingClient.closeIfCreated()).called(1);
          verify(() => connectionMiddleware.shutdown()).called(1);
        });
      },
    );
  });
}
