import 'dart:async';

import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/shared/utils/app_shutdown_service.dart';
import 'package:dovahlink_client/shared/utils/existing_dovahlink_client.dart';

/// Mocks app-owned pairing recovery cleanup.
class MockPairingMiddleware extends Mock implements IPairingMiddleware {}

/// Mocks shutdown access to an existing SDK client.
class MockExistingDovahLinkClient extends Mock
    implements IExistingDovahLinkClient {}

/// Exercises shared cleanup ordering, deduplication, failures, and timeout behavior.
void main() {
  late MockPairingMiddleware pairingMiddleware;
  late MockExistingDovahLinkClient existingClient;
  late AppShutdownService service;

  setUp(() {
    pairingMiddleware = MockPairingMiddleware();
    existingClient = MockExistingDovahLinkClient();
    when(() => pairingMiddleware.shutdown()).thenAnswer((_) async {});
    when(() => existingClient.disconnectIfCreated()).thenAnswer((_) async {});
    service = AppShutdownService(
      pairingMiddleware: pairingMiddleware,
      existingClient: existingClient,
    );
  });

  group('Method shutdown behaves correctly', () {
    test(
      'Method shutdown stops pairing work before disconnecting an existing client',
      () async {
        final List<String> cleanupOrder = <String>[];
        when(() => pairingMiddleware.shutdown()).thenAnswer((_) async {
          cleanupOrder.add('pairing');
        });
        when(() => existingClient.disconnectIfCreated()).thenAnswer((_) async {
          cleanupOrder.add('client');
        });

        await service.shutdown();

        expect(cleanupOrder, <String>['pairing', 'client']);
      },
    );

    test(
      'Method shutdown shares one cleanup operation across simultaneous callers',
      () async {
        final Completer<void> disconnectCompleter = Completer<void>();
        when(
          () => existingClient.disconnectIfCreated(),
        ).thenAnswer((_) => disconnectCompleter.future);

        final Future<void> first = service.shutdown();
        final Future<void> second = service.shutdown();

        expect(identical(first, second), isTrue);
        disconnectCompleter.complete();
        await Future.wait(<Future<void>>[first, second]);
        verify(() => pairingMiddleware.shutdown()).called(1);
        verify(() => existingClient.disconnectIfCreated()).called(1);
      },
    );

    test('Method shutdown contains synchronous cleanup failures', () async {
      when(
        () => pairingMiddleware.shutdown(),
      ).thenThrow(StateError('pairing cleanup failed'));
      when(
        () => existingClient.disconnectIfCreated(),
      ).thenThrow(StateError('client disconnect failed'));

      await expectLater(service.shutdown(), completes);
      verify(() => pairingMiddleware.shutdown()).called(1);
      verify(() => existingClient.disconnectIfCreated()).called(1);
    });

    test('Method shutdown contains asynchronous cleanup failures', () async {
      when(() => pairingMiddleware.shutdown()).thenAnswer((_) async {
        throw StateError('pairing cleanup failed');
      });
      when(() => existingClient.disconnectIfCreated()).thenAnswer((_) async {
        throw StateError('client disconnect failed');
      });

      await expectLater(service.shutdown(), completes);
      verify(() => pairingMiddleware.shutdown()).called(1);
      verify(() => existingClient.disconnectIfCreated()).called(1);
    });

    test(
      'Method shutdown completes when client disconnect exceeds its budget',
      () {
        fakeAsync((FakeAsync async) {
          final Completer<void> disconnectCompleter = Completer<void>();
          when(
            () => existingClient.disconnectIfCreated(),
          ).thenAnswer((_) => disconnectCompleter.future);
          bool shutdownCompleted = false;
          service.shutdown().then((_) => shutdownCompleted = true);
          async.flushMicrotasks();

          expect(shutdownCompleted, isFalse);
          async.elapse(const Duration(seconds: 3));
          async.flushMicrotasks();

          expect(shutdownCompleted, isTrue);
          disconnectCompleter.complete();
        });
      },
    );

    test('Method shutdown completes when pairing cleanup stalls', () {
      fakeAsync((FakeAsync async) {
        final Completer<void> pairingCompleter = Completer<void>();
        when(
          () => pairingMiddleware.shutdown(),
        ).thenAnswer((_) => pairingCompleter.future);
        bool shutdownCompleted = false;
        service.shutdown().then((_) => shutdownCompleted = true);
        async.flushMicrotasks();

        expect(shutdownCompleted, isFalse);
        verify(() => existingClient.disconnectIfCreated()).called(1);
        async.elapse(const Duration(seconds: 3));
        async.flushMicrotasks();

        expect(shutdownCompleted, isTrue);
        pairingCompleter.complete();
      });
    });
  });
}
