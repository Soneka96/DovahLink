import 'dart:async';

import 'package:flutter/services.dart';

import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/utils/app_shutdown_service.dart';

/// Mocks app-owned pairing recovery cleanup.
class MockPairingMiddleware extends Mock implements IPairingMiddleware {}

/// Mocks the existing SDK disconnect use case.
class MockDisconnectUseCase extends Mock implements DisconnectUseCase {}

/// Exercises shutdown ordering, deduplication, and the native close response boundary.
void main() {
  const MethodChannel windowChannel = MethodChannel('test/dovahlink_close');
  late MockPairingMiddleware pairingMiddleware;
  late MockDisconnectUseCase disconnectUseCase;
  late AppShutdownService service;

  setUpAll(() {
    registerFallbackValue(NoParams());
  });

  setUp(() {
    TestWidgetsFlutterBinding.ensureInitialized();
    pairingMiddleware = MockPairingMiddleware();
    disconnectUseCase = MockDisconnectUseCase();
    when(() => pairingMiddleware.shutdown()).thenAnswer((_) async {});
    when(
      () => disconnectUseCase(any()),
    ).thenAnswer((_) async => const Right(unit));
    service = AppShutdownService(
      pairingMiddleware: pairingMiddleware,
      disconnectUseCase: disconnectUseCase,
      windowChannel: windowChannel,
    );
  });

  tearDown(() {
    windowChannel.setMethodCallHandler(null);
  });

  group('Method shutdown behaves correctly', () {
    test(
      'shutdown cancels app recovery before disconnecting the SDK client',
      () async {
        final List<String> cleanupOrder = <String>[];
        when(() => pairingMiddleware.shutdown()).thenAnswer((_) async {
          cleanupOrder.add('app');
        });
        when(() => disconnectUseCase(any())).thenAnswer((_) async {
          cleanupOrder.add('sdk');
          return const Right(unit);
        });

        await service.shutdown();

        expect(cleanupOrder, ['app', 'sdk']);
      },
    );

    test('simultaneous shutdown calls share one cleanup sequence', () async {
      final Completer<void> middlewareCompleter = Completer<void>();
      when(
        () => pairingMiddleware.shutdown(),
      ).thenAnswer((_) => middlewareCompleter.future);

      final Future<void> first = service.shutdown();
      final Future<void> second = service.shutdown();
      expect(identical(first, second), isTrue);
      middlewareCompleter.complete();
      await Future.wait(<Future<void>>[first, second]);

      verify(() => pairingMiddleware.shutdown()).called(1);
      verify(() => disconnectUseCase(any())).called(1);
    });

    test(
      'SDK disconnect starts while app stream cancellation is pending',
      () async {
        final Completer<void> middlewareCompleter = Completer<void>();
        when(
          () => pairingMiddleware.shutdown(),
        ).thenAnswer((_) => middlewareCompleter.future);

        final Future<void> shutdown = service.shutdown();
        await pumpEventQueue();
        verify(() => disconnectUseCase(any())).called(1);
        middlewareCompleter.complete();
        await shutdown;
      },
    );

    test('cleanup failures still complete the native close request', () async {
      when(
        () => pairingMiddleware.shutdown(),
      ).thenThrow(StateError('subscription close failed'));
      when(
        () => disconnectUseCase(any()),
      ).thenThrow(StateError('disconnect failed'));
      service.registerWindowCloseHandler();
      final ByteData? response = await TestDefaultBinaryMessengerBinding
          .instance
          .defaultBinaryMessenger
          .handlePlatformMessage(
            windowChannel.name,
            windowChannel.codec.encodeMethodCall(
              const MethodCall('requestClose'),
            ),
            null,
          );

      expect(response, isNotNull);
      expect(windowChannel.codec.decodeEnvelope(response!), isNull);
      await service.shutdown();
      verify(() => pairingMiddleware.shutdown()).called(1);
      verify(() => disconnectUseCase(any())).called(1);
    });

    test('unsupported window methods do not start shutdown', () async {
      service.registerWindowCloseHandler();
      final ByteData? response = await TestDefaultBinaryMessengerBinding
          .instance
          .defaultBinaryMessenger
          .handlePlatformMessage(
            windowChannel.name,
            windowChannel.codec.encodeMethodCall(
              const MethodCall('unsupported'),
            ),
            null,
          );

      expect(response, isNull);
      verifyNever(() => pairingMiddleware.shutdown());
      verifyNever(() => disconnectUseCase(any()));
    });

    test('shutdown continues after the SDK disconnect timeout', () {
      fakeAsync((FakeAsync async) {
        final Completer<Either<Failure, Unit>> disconnectCompleter =
            Completer<Either<Failure, Unit>>();
        when(
          () => disconnectUseCase(any()),
        ).thenAnswer((_) => disconnectCompleter.future);
        bool shutdownCompleted = false;
        service.shutdown().then((_) => shutdownCompleted = true);
        async.flushMicrotasks();

        expect(shutdownCompleted, isFalse);
        async.elapse(const Duration(seconds: 3));
        async.flushMicrotasks();

        expect(shutdownCompleted, isTrue);
      });
    });

    test('shutdown completes if app-owned subscription cleanup stalls', () {
      fakeAsync((FakeAsync async) {
        final Completer<void> middlewareCompleter = Completer<void>();
        when(
          () => pairingMiddleware.shutdown(),
        ).thenAnswer((_) => middlewareCompleter.future);
        bool shutdownCompleted = false;
        service.shutdown().then((_) => shutdownCompleted = true);
        async.flushMicrotasks();
        expect(shutdownCompleted, isFalse);
        verify(() => disconnectUseCase(any())).called(1);

        async.elapse(const Duration(seconds: 3));
        async.flushMicrotasks();

        expect(shutdownCompleted, isTrue);
      });
    });

    test('native close reply waits for shared shutdown completion', () async {
      final Completer<Either<Failure, Unit>> disconnectCompleter =
          Completer<Either<Failure, Unit>>();
      when(
        () => disconnectUseCase(any()),
      ).thenAnswer((_) => disconnectCompleter.future);
      service.registerWindowCloseHandler();
      final TestDefaultBinaryMessenger messenger =
          TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
      final ByteData request = windowChannel.codec.encodeMethodCall(
        const MethodCall('requestClose'),
      );
      final Future<ByteData?> firstReply = messenger.handlePlatformMessage(
        windowChannel.name,
        request,
        null,
      );
      final Future<ByteData?> secondReply = messenger.handlePlatformMessage(
        windowChannel.name,
        request,
        null,
      );
      bool replied = false;
      unawaited(firstReply.then((_) => replied = true));
      await pumpEventQueue();

      expect(replied, isFalse);
      verify(() => pairingMiddleware.shutdown()).called(1);
      verify(() => disconnectUseCase(any())).called(1);
      disconnectCompleter.complete(const Right(unit));
      final List<ByteData?> replies = await Future.wait(<Future<ByteData?>>[
        firstReply,
        secondReply,
      ]);

      expect(replies, everyElement(isNotNull));
      for (final ByteData? reply in replies) {
        expect(windowChannel.codec.decodeEnvelope(reply!), isNull);
      }
    });
  });
}
