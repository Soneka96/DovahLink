import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/session_connector.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Mock session connector used to control connection state and count recovery attempts.
class MockSessionConnector extends Mock implements SessionConnector {}

/// Millisecond-scale delays used so the retry loop's tests run fast, mirroring this suite's
/// existing short-timeout convention for timer-based behavior.
const List<Duration> _shortDelays = <Duration>[
  Duration.zero,
  Duration(milliseconds: 5),
  Duration(milliseconds: 5),
  Duration(milliseconds: 5),
];

final Uri _uri = Uri.parse('ws://127.0.0.1:58231/');

/// Runs reconnect-coordinator behavior tests.
void main() {
  late MockSessionConnector sessionConnector;
  late int reauthenticateCallCount;
  late Future<HelloResult> Function() reauthenticate;

  setUpAll(() {
    registerFallbackValue(_uri);
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
  });

  setUp(() {
    sessionConnector = MockSessionConnector();
    reauthenticateCallCount = 0;
    when(
      () => sessionConnector.connectionState,
    ).thenReturn(DovahLinkConnectionState.reconnecting);
    when(() => sessionConnector.connect(any())).thenAnswer((_) async {});
    when(
      () => sessionConnector.disconnect(
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        reason: any(named: 'reason'),
      ),
    ).thenAnswer((_) async {});
  });

  /// Builds a coordinator over [sessionConnector] and [reauthenticate], with short test delays.
  ReconnectCoordinator buildCoordinator({
    List<Duration> attemptDelays = _shortDelays,
    Duration deadline = const Duration(seconds: 30),
    DateTime Function()? now,
  }) => ReconnectCoordinator(
    sessionConnector: sessionConnector,
    reauthenticate: reauthenticate,
    attemptDelays: attemptDelays,
    deadline: deadline,
    now: now ?? DateTime.now,
  );

  group('Method onOrdinaryTransportLoss behaves correctly', () {
    test(
      'Method onOrdinaryTransportLoss succeeds on the first attempt without disconnecting',
      () async {
        reauthenticate = () async {
          reauthenticateCallCount++;
          return const HelloResult(
            bridgeVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          );
        };
        final ReconnectCoordinator coordinator = buildCoordinator();

        coordinator.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();

        verify(() => sessionConnector.connect(_uri)).called(1);
        expect(reauthenticateCallCount, 1);
        verifyNever(
          () => sessionConnector.disconnect(
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
        when(() => sessionConnector.connect(any())).thenAnswer((_) async {
          connectCallCount++;
          if (connectCallCount < 3) {
            throw const DovahLinkConnectionException('unreachable');
          }
        });
        reauthenticate = () async {
          reauthenticateCallCount++;
          return const HelloResult(
            bridgeVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          );
        };
        final ReconnectCoordinator coordinator = buildCoordinator();

        coordinator.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        expect(connectCallCount, 3);
        expect(reauthenticateCallCount, 1);
        verifyNever(
          () => sessionConnector.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );

    test(
      'Method onOrdinaryTransportLoss stops immediately on a typed rejection from '
      'reauthenticate instead of consuming the remaining attempt budget',
      () async {
        reauthenticate = () async {
          reauthenticateCallCount++;
          throw const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'rejected',
            retryable: false,
          );
        };
        final ReconnectCoordinator coordinator = buildCoordinator();

        coordinator.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        expect(reauthenticateCallCount, 1);
        verify(() => sessionConnector.connect(_uri)).called(1);
        verify(
          () => sessionConnector.disconnect(
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
          () => sessionConnector.connect(any()),
        ).thenThrow(const DovahLinkConnectionException('unreachable'));
        reauthenticate = () async {
          reauthenticateCallCount++;
          return const HelloResult(
            bridgeVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          );
        };
        final ReconnectCoordinator coordinator = buildCoordinator();

        coordinator.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(
          () => sessionConnector.connect(_uri),
        ).called(_shortDelays.length);
        expect(reauthenticateCallCount, 0);
        final VerificationResult verification = verify(
          () => sessionConnector.disconnect(
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
        () => sessionConnector.connect(any()),
      ).thenThrow(const DovahLinkConnectionException('unreachable'));
      reauthenticate = () async {
        reauthenticateCallCount++;
        return const HelloResult(
          bridgeVersion: '1.0',
          trustState: DovahLinkTrustState.trusted,
        );
      };
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

      final ReconnectCoordinator coordinator = buildCoordinator(
        deadline: const Duration(seconds: 10),
        now: fakeNow,
      );

      coordinator.onOrdinaryTransportLoss(_uri);
      await pumpEventQueue();

      verify(() => sessionConnector.connect(_uri)).called(1);
      verify(
        () => sessionConnector.disconnect(
          orphanRetrySafeOperations: false,
          reason: any(named: 'reason'),
        ),
      ).called(1);
    });

    test(
      'Method onOrdinaryTransportLoss stops without touching the connection again once something '
      'else moves the session out of reconnecting',
      () async {
        // Simulates an explicit disconnect or administrative invalidation racing in before the
        // first attempt runs.
        when(
          () => sessionConnector.connectionState,
        ).thenReturn(DovahLinkConnectionState.disconnected);
        reauthenticate = () async {
          reauthenticateCallCount++;
          return const HelloResult(
            bridgeVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          );
        };
        final ReconnectCoordinator coordinator = buildCoordinator();

        coordinator.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();

        verifyNever(() => sessionConnector.connect(any()));
        expect(reauthenticateCallCount, 0);
        verifyNever(
          () => sessionConnector.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );
  });
}
