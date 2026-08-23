import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect_service_impl.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Mock session service used to control connection state and count recovery attempts, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionService extends Mock implements SessionService {}

/// Mock authentication service -- its own `hello`/recovery logic is
/// `authentication_service_impl_test.dart`'s responsibility; this file only proves
/// [ReconnectServiceImpl] reacts correctly to each classification `hello()` can produce.
class MockAuthenticationService extends Mock implements AuthenticationService {}

/// Millisecond-scale delays used so the retry loop's tests run fast, mirroring this suite's
/// existing short-timeout convention for timer-based behavior.
const List<Duration> _shortDelays = <Duration>[
  Duration.zero,
  Duration(milliseconds: 5),
  Duration(milliseconds: 5),
  Duration(milliseconds: 5),
];

final Uri _uri = Uri.parse('ws://127.0.0.1:58231/');

/// Runs reconnect-service behavior tests.
void main() {
  late MockSessionService sessionService;
  late MockAuthenticationService authenticationService;

  setUpAll(() {
    registerFallbackValue(_uri);
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
  });

  setUp(() {
    sessionService = MockSessionService();
    authenticationService = MockAuthenticationService();
    when(
      () => sessionService.connectionState,
    ).thenReturn(DovahLinkConnectionState.reconnecting);
    when(() => sessionService.connect(any())).thenAnswer((_) async {});
    when(
      () => sessionService.disconnect(
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        reason: any(named: 'reason'),
      ),
    ).thenAnswer((_) async {});
  });

  /// Builds a service over [sessionService] and [authenticationService], with short test delays.
  ReconnectServiceImpl buildService({
    List<Duration> attemptDelays = _shortDelays,
    Duration deadline = const Duration(seconds: 30),
    DateTime Function()? now,
  }) => ReconnectServiceImpl(
    sessionService: sessionService,
    authenticationService: authenticationService,
    attemptDelays: attemptDelays,
    deadline: deadline,
    now: now ?? DateTime.now,
  );

  group('Method onOrdinaryTransportLoss behaves correctly', () {
    test(
      'Method onOrdinaryTransportLoss succeeds on the first attempt without disconnecting',
      () async {
        when(() => authenticationService.hello()).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final ReconnectServiceImpl service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();

        verify(() => sessionService.connect(_uri)).called(1);
        verify(() => authenticationService.hello()).called(1);
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );

    test(
      'Method onOrdinaryTransportLoss succeeds on a later attempt after earlier connect() '
      'failures, waiting between attempts',
      () async {
        int connectCallCount = 0;
        when(() => sessionService.connect(any())).thenAnswer((_) async {
          connectCallCount++;
          if (connectCallCount < 3) {
            throw const DovahLinkConnectionException('unreachable');
          }
        });
        when(() => authenticationService.hello()).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final ReconnectServiceImpl service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        expect(connectCallCount, 3);
        verify(() => authenticationService.hello()).called(1);
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );

    test(
      'Method onOrdinaryTransportLoss stops immediately on a typed rejection from hello() '
      'instead of consuming the remaining attempt budget',
      () async {
        when(() => authenticationService.hello()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'rejected',
            retryable: false,
          ),
        );
        final ReconnectServiceImpl service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(() => authenticationService.hello()).called(1);
        verify(() => sessionService.connect(_uri)).called(1);
        verify(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: false,
            reason: any(named: 'reason'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss disconnects once the attempt budget is exhausted, all '
      'attempts having failed',
      () async {
        when(
          () => sessionService.connect(any()),
        ).thenThrow(const DovahLinkConnectionException('unreachable'));
        when(() => authenticationService.hello()).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final ReconnectServiceImpl service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(() => sessionService.connect(_uri)).called(_shortDelays.length);
        verifyNever(() => authenticationService.hello());
        final VerificationResult verification = verify(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: false,
            reason: captureAny(named: 'reason'),
          ),
        );
        verification.called(1);
        expect(
          (verification.captured.single as DovahLinkConnectionException)
              .message,
          isNotEmpty,
        );
      },
    );

    test('Method onOrdinaryTransportLoss stops once the hard deadline elapses even with attempts '
        'remaining', () async {
      when(
        () => sessionService.connect(any()),
      ).thenThrow(const DovahLinkConnectionException('unreachable'));
      when(() => authenticationService.hello()).thenAnswer(
        (_) async => const HelloResult(
          bridgeVersion: '1.0',
          trustState: DovahLinkTrustState.trusted,
        ),
      );
      // A fake clock, not real elapsed time, decides when the deadline has passed: the first
      // two calls (the deadline calculation and attempt 0's own deadline check) see the start
      // time; every call after that sees a time far past the deadline, so attempt 1's
      // untilDeadline check breaks the loop deterministically, regardless of how fast this
      // test actually runs.
      int nowCallCount = 0;
      final DateTime start = DateTime(2024);
      DateTime fakeNow() {
        nowCallCount++;
        return nowCallCount <= 2
            ? start
            : start.add(const Duration(seconds: 100));
      }

      final ReconnectServiceImpl service = buildService(
        deadline: const Duration(seconds: 10),
        now: fakeNow,
      );

      service.onOrdinaryTransportLoss(_uri);
      await pumpEventQueue();

      verify(() => sessionService.connect(_uri)).called(1);
      verify(
        () => sessionService.disconnect(
          orphanRetrySafeOperations: false,
          reason: any(named: 'reason'),
        ),
      ).called(1);
    });

    test('Method onOrdinaryTransportLoss caps a later attempt\'s delay to the time remaining before '
        'the deadline instead of waiting its full nominal delay', () async {
      int connectCallCount = 0;
      when(() => sessionService.connect(any())).thenAnswer((_) async {
        connectCallCount++;
      });
      int helloCallCount = 0;
      when(() => authenticationService.hello()).thenAnswer((_) async {
        helloCallCount++;
        if (helloCallCount == 1) {
          throw const DovahLinkConnectionException('unreachable');
        }
        return const HelloResult(
          bridgeVersion: '1.0',
          trustState: DovahLinkTrustState.trusted,
        );
      });
      // A fake clock makes attempt 1's nominal 100-second delay irrelevant: by the time it is
      // computed, only 5 milliseconds remain before the deadline, so the capped wait is short
      // enough for this test to observe the second attempt run well within a short real wait --
      // proving the loop capped the delay and continued, rather than waiting the nominal delay
      // or breaking outright.
      final DateTime start = DateTime(2024);
      int nowCallCount = 0;
      DateTime fakeNow() {
        nowCallCount++;
        return switch (nowCallCount) {
          <= 2 => start,
          _ => start.add(const Duration(seconds: 10, milliseconds: -5)),
        };
      }

      final ReconnectServiceImpl service = buildService(
        attemptDelays: const <Duration>[Duration.zero, Duration(seconds: 100)],
        deadline: const Duration(seconds: 10),
        now: fakeNow,
      );

      service.onOrdinaryTransportLoss(_uri);
      await Future<void>.delayed(const Duration(milliseconds: 200));

      expect(connectCallCount, 2);
      expect(helloCallCount, 2);
      verifyNever(
        () => sessionService.disconnect(
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          reason: any(named: 'reason'),
        ),
      );
    });

    test(
      'Method onOrdinaryTransportLoss stops without touching the connection again once something '
      'else moves the session out of reconnecting',
      () async {
        // Simulates an explicit disconnect or administrative invalidation racing in before the
        // first attempt runs.
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.disconnected);
        when(() => authenticationService.hello()).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final ReconnectServiceImpl service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();

        verifyNever(() => sessionService.connect(any()));
        verifyNever(() => authenticationService.hello());
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );
  });
}
