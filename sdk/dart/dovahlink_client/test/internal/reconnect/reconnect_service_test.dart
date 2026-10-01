import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';

/// Mocks session lifecycle to control connection state and count recovery attempts.
class MockSessionService extends Mock implements ISessionService {}

/// Mocks authentication so these tests can verify [ReconnectService]'s response to
/// [IAuthenticationService.hello] outcomes.
class MockAuthenticationService extends Mock
    implements IAuthenticationService {}

/// Mocks the single owner of runtime Known Host availability.
class MockHostAvailabilityService extends Mock
    implements IHostAvailabilityService {}

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
  late MockHostAvailabilityService hostAvailabilityService;

  setUpAll(() {
    registerFallbackValue(_uri);
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
    registerFallbackValue(
      DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
    );
    registerFallbackValue(DovahLinkHostAvailability.unknown);
  });

  setUp(() {
    sessionService = MockSessionService();
    authenticationService = MockAuthenticationService();
    hostAvailabilityService = MockHostAvailabilityService();
    when(
      () => sessionService.connectionState,
    ).thenReturn(DovahLinkConnectionState.reconnecting);
    when(() => sessionService.isTerminallyClosed).thenReturn(false);
    when(() => sessionService.connect(any())).thenAnswer((_) async {});
    when(
      () => sessionService.disconnect(
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        reason: any(named: 'reason'),
      ),
    ).thenAnswer((_) async {});
    when(
      () => authenticationService.forgetLastKnownCredential(),
    ).thenAnswer((_) async {});
    when(
      () => hostAvailabilityService.setAvailability(any(), any()),
    ).thenReturn(null);
  });

  /// Builds a service over [sessionService] and [authenticationService], with short test delays.
  ReconnectService buildService({
    List<Duration> attemptDelays = _shortDelays,
    Duration deadline = const Duration(seconds: 30),
    Duration initialConnectionRetryDelay = const Duration(seconds: 3),
    Stream<void>? initialConnectionRetryTicks,
    DateTime Function()? now,
  }) => ReconnectService(
    sessionService: sessionService,
    authenticationService: authenticationService,
    hostAvailabilityService: hostAvailabilityService,
    attemptDelays: attemptDelays,
    deadline: deadline,
    now: now ?? DateTime.now,
    initialConnectionRetryDelay: initialConnectionRetryDelay,
    initialConnectionRetryTicks: initialConnectionRetryTicks,
  );

  group('Method connectWithInitialRetry behaves correctly', () {
    test(
      'Method connectWithInitialRetry rejects a terminally closed client without attempting authentication',
      () async {
        when(() => sessionService.isTerminallyClosed).thenReturn(true);
        int attemptCount = 0;
        final ReconnectService service = buildService();

        await expectLater(
          service.connectWithInitialRetry(() async {
            attemptCount++;
            return Fixtures.buildHelloResult();
          }),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(attemptCount, 0);
      },
    );

    test(
      'Method connectWithInitialRetry retries transient failures until authentication succeeds',
      () async {
        int attemptCount = 0;
        final List<DovahLinkInitialConnectionRetryStatus> statuses =
            <DovahLinkInitialConnectionRetryStatus>[];
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );
        final StreamSubscription<DovahLinkInitialConnectionRetryStatus>
        subscription = service.initialConnectionRetryChanges.listen(
          statuses.add,
        );
        addTearDown(subscription.cancel);

        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () async {
            attemptCount++;
            if (attemptCount == 1) {
              throw const DovahLinkConnectionException('unreachable');
            }
            if (attemptCount < 5) {
              throw const DovahLinkProtocolException(
                code: ProtocolErrorCode.rateLimited,
                message: 'temporarily unavailable',
                retryable: true,
              );
            }
            return Fixtures.buildHelloResult(
              trustState: DovahLinkTrustState.unpaired,
            );
          },
        );
        final HelloResult result = await connection;
        await pumpEventQueue();

        expect(attemptCount, 5);
        expect(result.trustState, DovahLinkTrustState.unpaired);
        expect(statuses, <DovahLinkInitialConnectionRetryStatus>[
          DovahLinkInitialConnectionRetryStatus.inactive,
          DovahLinkInitialConnectionRetryStatus.retrying,
          DovahLinkInitialConnectionRetryStatus.inactive,
        ]);
      },
    );

    test(
      'Method connectWithInitialRetry stops after administrative invalidation',
      () async {
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );
        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () async {
            attemptCount++;
            if (attemptCount == 1) {
              throw const DovahLinkConnectionException('unreachable');
            }
            when(
              () => sessionService.connectionState,
            ).thenReturn(DovahLinkConnectionState.administrativelyInvalidated);
            throw const DovahLinkConnectionException('invalidated');
          },
        );

        await expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(attemptCount, 2);
      },
    );

    test(
      'Method stopInitialConnectionRetry cancels its delay without another attempt',
      () async {
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: const Duration(seconds: 30),
        );
        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () async {
            attemptCount++;
            throw const DovahLinkConnectionException('unreachable');
          },
        );
        await pumpEventQueue();

        service.stopInitialConnectionRetry();
        await expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(attemptCount, 1);
        expect(
          await service.initialConnectionRetryChanges.first,
          DovahLinkInitialConnectionRetryStatus.inactive,
        );
      },
    );

    test(
      'Method stopRecovery does not cancel an initial connection retry',
      () async {
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );

        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () async {
            attemptCount++;
            if (attemptCount == 1) {
              throw const DovahLinkConnectionException('unreachable');
            }
            return Fixtures.buildHelloResult();
          },
        );
        service.stopRecovery();

        await connection;

        expect(attemptCount, 2);
      },
    );
  });

  group('Method connectWithInitialRetry behaves correctly', () {
    test(
      'Method connectWithInitialRetry retries transient failures until success',
      () async {
        int attemptCount = 0;
        final List<DovahLinkInitialConnectionRetryStatus> statuses =
            <DovahLinkInitialConnectionRetryStatus>[];
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );
        final StreamSubscription<DovahLinkInitialConnectionRetryStatus>
        subscription = service.initialConnectionRetryChanges.listen(
          statuses.add,
        );
        addTearDown(subscription.cancel);

        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () async {
            attemptCount++;
            if (attemptCount < 5) {
              throw const DovahLinkConnectionException('unreachable');
            }
            return Fixtures.buildHelloResult(
              trustState: DovahLinkTrustState.unpaired,
            );
          },
        );
        final HelloResult result = await connection;
        await pumpEventQueue();

        expect(attemptCount, 5);
        expect(result.trustState, DovahLinkTrustState.unpaired);
        expect(statuses, <DovahLinkInitialConnectionRetryStatus>[
          DovahLinkInitialConnectionRetryStatus.inactive,
          DovahLinkInitialConnectionRetryStatus.retrying,
          DovahLinkInitialConnectionRetryStatus.inactive,
        ]);
      },
    );

    test(
      'Method connectWithInitialRetry fails after one attempt for a non-retryable protocol error',
      () async {
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );

        await expectLater(
          service.connectWithInitialRetry(() async {
            attemptCount++;
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.rateLimited,
              message: 'not retryable',
              retryable: false,
            );
          }),
          throwsA(isA<DovahLinkProtocolException>()),
        );

        expect(attemptCount, 1);
      },
    );

    test(
      'Method connectWithInitialRetry fails after one attempt for malformed_message',
      () async {
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );

        await expectLater(
          service.connectWithInitialRetry(() async {
            attemptCount++;
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'malformed reply',
              retryable: true,
            );
          }),
          throwsA(isA<DovahLinkProtocolException>()),
        );

        expect(attemptCount, 1);
      },
    );

    test(
      'Method connectWithInitialRetry stops after a terminal protocol error on a retry',
      () async {
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );

        await expectLater(
          service.connectWithInitialRetry(() async {
            attemptCount++;
            if (attemptCount == 1) {
              throw const DovahLinkConnectionException('unreachable');
            }
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'malformed retry reply',
              retryable: true,
            );
          }),
          throwsA(isA<DovahLinkProtocolException>()),
        );

        expect(attemptCount, 2);
      },
    );

    test(
      'Method connectWithInitialRetry does not retry after administrative invalidation',
      () async {
        DovahLinkConnectionState connectionState =
            DovahLinkConnectionState.disconnected;
        when(
          () => sessionService.connectionState,
        ).thenAnswer((_) => connectionState);
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );

        await expectLater(
          service.connectWithInitialRetry(() async {
            attemptCount++;
            connectionState =
                DovahLinkConnectionState.administrativelyInvalidated;
            throw const DovahLinkConnectionException(
              'Host invalidated session',
            );
          }),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(attemptCount, 1);
      },
    );

    test(
      'Method connectWithInitialRetry does not retry a protocol failure after administrative invalidation',
      () async {
        DovahLinkConnectionState connectionState =
            DovahLinkConnectionState.disconnected;
        when(
          () => sessionService.connectionState,
        ).thenAnswer((_) => connectionState);
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );

        await expectLater(
          service.connectWithInitialRetry(() async {
            attemptCount++;
            connectionState =
                DovahLinkConnectionState.administrativelyInvalidated;
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.rateLimited,
              message: 'authentication rejected',
              retryable: true,
            );
          }),
          throwsA(isA<DovahLinkProtocolException>()),
        );

        expect(attemptCount, 1);
      },
    );

    test(
      'Method connectWithInitialRetry does not retry when invalidated during its delay',
      () async {
        DovahLinkConnectionState connectionState =
            DovahLinkConnectionState.disconnected;
        when(
          () => sessionService.connectionState,
        ).thenAnswer((_) => connectionState);
        final StreamController<void> ticks = StreamController<void>.broadcast();
        addTearDown(ticks.close);
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryTicks: ticks.stream,
        );
        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () async {
            attemptCount++;
            throw const DovahLinkConnectionException('unreachable');
          },
        );
        await pumpEventQueue();

        connectionState = DovahLinkConnectionState.administrativelyInvalidated;
        ticks.add(null);
        await expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(attemptCount, 1);
      },
    );

    test(
      'Method stopInitialConnectionRetry cancels a pending delay and suppresses its late result',
      () async {
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: const Duration(seconds: 30),
        );
        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () async {
            attemptCount++;
            throw const DovahLinkConnectionException('unreachable');
          },
        );
        await pumpEventQueue();

        service.stopInitialConnectionRetry();
        await expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(attemptCount, 1);
        expect(
          await service.initialConnectionRetryChanges.first,
          DovahLinkInitialConnectionRetryStatus.inactive,
        );
      },
    );

    test(
      'Method stopInitialConnectionRetry ignores a successful in-flight result',
      () async {
        final Completer<HelloResult> attempt = Completer<HelloResult>();
        final ReconnectService service = buildService();
        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () => attempt.future,
        );
        service.stopInitialConnectionRetry();
        attempt.complete(Fixtures.buildHelloResult());

        await expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );

    test(
      'Method connectWithInitialRetry invalidates an older Host attempt when a new one starts',
      () async {
        final Completer<HelloResult> olderAttempt = Completer<HelloResult>();
        final HelloResult currentHost = Fixtures.buildHelloResult(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
        );
        final ReconnectService service = buildService();
        final Future<HelloResult> olderConnection = service
            .connectWithInitialRetry(() => olderAttempt.future);

        expect(
          await service.connectWithInitialRetry(() async => currentHost),
          same(currentHost),
        );
        olderAttempt.complete(
          Fixtures.buildHelloResult(
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          ),
        );
        await expectLater(
          olderConnection,
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );

    test(
      'Method stopRecovery does not cancel an initial connection retry',
      () async {
        int attemptCount = 0;
        final ReconnectService service = buildService(
          initialConnectionRetryDelay: Duration.zero,
        );

        final Future<HelloResult> connection = service.connectWithInitialRetry(
          () async {
            attemptCount++;
            if (attemptCount == 1) {
              throw const DovahLinkConnectionException('unreachable');
            }
            return Fixtures.buildHelloResult();
          },
        );
        service.stopRecovery();

        await connection;

        expect(attemptCount, 2);
      },
    );
  });

  group('Method onOrdinaryTransportLoss behaves correctly', () {
    test(
      'Method onOrdinaryTransportLoss succeeds on the first attempt without disconnecting',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenAnswer(
          (_) async => Fixtures.buildHelloResult(
            hostVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();

        verify(() => sessionService.connect(_uri)).called(1);
        verify(() => authenticationService.helloLastKnownHost()).called(1);
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method onOrdinaryTransportLoss reports online for the Known Host this cycle owns',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        when(() => authenticationService.helloLastKnownHost()).thenAnswer((
          _,
        ) async {
          when(
            () => sessionService.connectionState,
          ).thenReturn(DovahLinkConnectionState.connected);
          return Fixtures.buildHelloResult(
            hostVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          );
        });
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri, hostId);
        await pumpEventQueue();

        verify(
          () => hostAvailabilityService.setAvailability(
            hostId,
            DovahLinkHostAvailability.online,
          ),
        ).called(1);
        verifyNever(
          () => hostAvailabilityService.setAvailability(
            any(),
            DovahLinkHostAvailability.offline,
          ),
        );
      },
    );

    test(
      'Method stopRecovery cancels a pending retry delay without starting another attempt',
      () async {
        int connectCallCount = 0;
        when(() => sessionService.connect(any())).thenAnswer((_) async {
          connectCallCount++;
          throw const DovahLinkConnectionException('unreachable');
        });
        final ReconnectService service = buildService(
          attemptDelays: const <Duration>[Duration.zero, Duration(seconds: 1)],
        );

        service.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();
        service.stopRecovery();
        await pumpEventQueue();

        expect(connectCallCount, 1);
        verifyNever(() => authenticationService.helloLastKnownHost());
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );

    test(
      'Method stopRecovery prevents authentication after an in-flight connect completes',
      () async {
        final Completer<void> connectCompleter = Completer<void>();
        when(
          () => sessionService.connect(any()),
        ).thenAnswer((_) => connectCompleter.future);
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        service.stopRecovery();
        connectCompleter.complete();
        await pumpEventQueue();

        verifyNever(() => authenticationService.helloLastKnownHost());
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );

    test(
      'Method stopRecovery ignores a late terminal rejection from an in-flight hello',
      () async {
        final Completer<HelloResult> helloCompleter = Completer<HelloResult>();
        when(
          () => authenticationService.helloLastKnownHost(),
        ).thenAnswer((_) => helloCompleter.future);
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();
        service.stopRecovery();
        helloCompleter.completeError(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'rejected',
            retryable: false,
          ),
        );
        await pumpEventQueue();

        verify(() => sessionService.connect(_uri)).called(1);
        verify(() => authenticationService.helloLastKnownHost()).called(1);
        verifyNever(() => authenticationService.forgetLastKnownCredential());
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );

    test(
      'Method onOrdinaryTransportLoss starts a later recovery after stopRecovery',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenAnswer(
          (_) async => Fixtures.buildHelloResult(
            hostVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final ReconnectService service = buildService();

        service.stopRecovery();
        service.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();

        verify(() => sessionService.connect(_uri)).called(1);
        verify(() => authenticationService.helloLastKnownHost()).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss succeeds on a later attempt after earlier connect() '
      'failures, waiting between attempts',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        int connectCallCount = 0;
        when(() => sessionService.connect(any())).thenAnswer((_) async {
          connectCallCount++;
          if (connectCallCount < 3) {
            throw const DovahLinkConnectionException('unreachable');
          }
        });
        when(() => authenticationService.helloLastKnownHost()).thenAnswer((
          _,
        ) async {
          when(
            () => sessionService.connectionState,
          ).thenReturn(DovahLinkConnectionState.connected);
          return Fixtures.buildHelloResult(
            hostVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          );
        });
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri, hostId);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        expect(connectCallCount, 3);
        verify(() => authenticationService.helloLastKnownHost()).called(1);
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
        verify(
          () => hostAvailabilityService.setAvailability(
            hostId,
            DovahLinkHostAvailability.online,
          ),
        ).called(1);
        verifyNever(
          () => hostAvailabilityService.setAvailability(
            hostId,
            DovahLinkHostAvailability.offline,
          ),
        );
      },
    );

    test(
      'Method onOrdinaryTransportLoss stops immediately on a typed rejection from hello() '
      'instead of consuming the remaining attempt budget',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'rejected',
            retryable: false,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri, hostId);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(() => authenticationService.helloLastKnownHost()).called(1);
        verify(() => sessionService.connect(_uri)).called(1);
        verify(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: false,
            reason: any(named: 'reason'),
          ),
        ).called(1);
        verify(
          () => hostAvailabilityService.setAvailability(
            hostId,
            DovahLinkHostAvailability.unknown,
          ),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss stops after an incompatible Host instead of retrying it',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkCompatibilityException(
            hostVersion: '0.6.0',
            supportedHostVersionRange: '0.5.x',
            failure: HostVersionCompatibilityFailure.hostTooNew,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri, hostId);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(() => authenticationService.helloLastKnownHost()).called(1);
        verify(() => sessionService.connect(_uri)).called(1);
        final Exception reason =
            verify(
                  () => sessionService.disconnect(
                    orphanRetrySafeOperations: false,
                    reason: captureAny(named: 'reason'),
                  ),
                ).captured.single
                as Exception;
        expect(reason, isA<DovahLinkCompatibilityException>());
        verify(
          () => hostAvailabilityService.setAvailability(
            hostId,
            DovahLinkHostAvailability.unknown,
          ),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss stops after a Known Host identity mismatch and preserves its reason',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        const DovahLinkHostIdentityMismatchException mismatch =
            DovahLinkHostIdentityMismatchException(
              knownHostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
              reportedHostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            );
        when(
          () => authenticationService.helloLastKnownHost(),
        ).thenThrow(mismatch);
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri, hostId);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(() => sessionService.connect(_uri)).called(1);
        verify(() => authenticationService.helloLastKnownHost()).called(1);
        verifyNever(() => authenticationService.forgetLastKnownCredential());
        final Exception reason =
            verify(
                  () => sessionService.disconnect(
                    orphanRetrySafeOperations: false,
                    reason: captureAny(named: 'reason'),
                  ),
                ).captured.single
                as Exception;
        expect(identical(reason, mismatch), isTrue);
        verify(
          () => hostAvailabilityService.setAvailability(
            hostId,
            DovahLinkHostAvailability.unknown,
          ),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss stops after an older Host instead of retrying it',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkCompatibilityException(
            hostVersion: '0.3.9',
            supportedHostVersionRange: '0.5.x',
            failure: HostVersionCompatibilityFailure.hostTooOld,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(() => authenticationService.helloLastKnownHost()).called(1);
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
      'Method onOrdinaryTransportLoss stops on a terminal protocol rejection from hello() on a '
      'later attempt, not just the first, without consuming further budget',
      () async {
        int connectCallCount = 0;
        when(() => sessionService.connect(any())).thenAnswer((_) async {
          connectCallCount++;
        });
        int helloCallCount = 0;
        when(() => authenticationService.helloLastKnownHost()).thenAnswer((
          _,
        ) async {
          helloCallCount++;
          if (helloCallCount == 1) {
            throw const DovahLinkConnectionException('unreachable');
          }
          throw const DovahLinkProtocolException(
            code: ProtocolErrorCode.blocked,
            message: 'blocked',
            retryable: false,
          );
        });
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        expect(helloCallCount, 2);
        expect(connectCallCount, 2);
        verify(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: false,
            reason: any(named: 'reason'),
          ),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss forgets the credential on a terminal revoked rejection '
      'from hello(), so a later automatic attempt never presents it again',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'rejected',
            retryable: false,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(
          () => authenticationService.forgetLastKnownCredential(),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss forgets the credential on a terminal blocked rejection '
      'from hello() the same way',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.blocked,
            message: 'blocked',
            retryable: false,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(
          () => authenticationService.forgetLastKnownCredential(),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss forgets the credential on a terminal unauthenticated '
      'rejection from hello() the same way',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.unauthenticated,
            message: 'invalid token',
            retryable: false,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(
          () => authenticationService.forgetLastKnownCredential(),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss does not forget the credential on a terminal rejection '
      'that is not about this credential specifically',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.unauthorized,
            message: 'not authorized',
            retryable: false,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verifyNever(() => authenticationService.forgetLastKnownCredential());
      },
    );

    test(
      'Method onOrdinaryTransportLoss forgets the credential before the final disconnect on a '
      'terminal credential rejection, not concurrently with or after it',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'rejected',
            retryable: false,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verifyInOrder([
          () => authenticationService.forgetLastKnownCredential(),
          () => sessionService.disconnect(
            orphanRetrySafeOperations: false,
            reason: any(named: 'reason'),
          ),
        ]);
      },
    );

    test(
      'Method onOrdinaryTransportLoss still reaches the final disconnect when credential cleanup fails',
      () async {
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'rejected',
            retryable: false,
          ),
        );
        when(
          () => authenticationService.forgetLastKnownCredential(),
        ).thenThrow(const DovahLinkStorageException('storage unavailable'));
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verifyInOrder([
          () => authenticationService.forgetLastKnownCredential(),
          () => sessionService.disconnect(
            orphanRetrySafeOperations: false,
            reason: any(named: 'reason'),
          ),
        ]);
      },
    );

    test(
      'Method onOrdinaryTransportLoss consumes an attempt and continues after a retryable '
      'protocol rejection from hello(), succeeding on a later attempt',
      () async {
        int helloCallCount = 0;
        when(() => authenticationService.helloLastKnownHost()).thenAnswer((
          _,
        ) async {
          helloCallCount++;
          if (helloCallCount == 1) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.rateLimited,
              message: 'slow down',
              retryable: true,
            );
          }
          return Fixtures.buildHelloResult(
            hostVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          );
        });
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        expect(helloCallCount, 2);
        verify(() => sessionService.connect(_uri)).called(2);
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );

    test(
      'Method onOrdinaryTransportLoss keeps availability unknown when retryable protocol responses exhaust the budget',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        when(() => authenticationService.helloLastKnownHost()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.rateLimited,
            message: 'slow down',
            retryable: true,
          ),
        );
        final ReconnectService service = buildService(
          attemptDelays: const <Duration>[Duration.zero, Duration.zero],
        );

        service.onOrdinaryTransportLoss(_uri, hostId);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(() => sessionService.connect(_uri)).called(2);
        verify(
          () => hostAvailabilityService.setAvailability(
            hostId,
            DovahLinkHostAvailability.unknown,
          ),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss disconnects once the attempt budget is exhausted, all '
      'attempts having failed',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        when(
          () => sessionService.connect(any()),
        ).thenThrow(const DovahLinkConnectionException('unreachable'));
        when(() => authenticationService.helloLastKnownHost()).thenAnswer(
          (_) async => Fixtures.buildHelloResult(
            hostVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri, hostId);
        await Future<void>.delayed(const Duration(milliseconds: 50));

        verify(() => sessionService.connect(_uri)).called(_shortDelays.length);
        verifyNever(() => authenticationService.helloLastKnownHost());
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
        verify(
          () => hostAvailabilityService.setAvailability(
            hostId,
            DovahLinkHostAvailability.offline,
          ),
        ).called(1);
      },
    );

    test(
      'Method onOrdinaryTransportLoss preserves availability when administrative invalidation ends recovery',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        when(() => authenticationService.helloLastKnownHost()).thenAnswer((
          _,
        ) async {
          when(
            () => sessionService.connectionState,
          ).thenReturn(DovahLinkConnectionState.administrativelyInvalidated);
          throw DovahLinkHostIdentityMismatchException(
            knownHostId: hostId.value,
            reportedHostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          );
        });
        final ReconnectService service = buildService(
          attemptDelays: const <Duration>[Duration.zero],
        );

        service.onOrdinaryTransportLoss(_uri, hostId);
        await pumpEventQueue();

        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
            reason: any(named: 'reason'),
          ),
        );
      },
    );

    test('Method onOrdinaryTransportLoss stops once the hard deadline elapses even with attempts '
        'remaining', () async {
      when(
        () => sessionService.connect(any()),
      ).thenThrow(const DovahLinkConnectionException('unreachable'));
      when(() => authenticationService.helloLastKnownHost()).thenAnswer(
        (_) async => Fixtures.buildHelloResult(
          hostVersion: '1.0',
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

      final ReconnectService service = buildService(
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
      when(() => authenticationService.helloLastKnownHost()).thenAnswer((
        _,
      ) async {
        helloCallCount++;
        if (helloCallCount == 1) {
          throw const DovahLinkConnectionException('unreachable');
        }
        return Fixtures.buildHelloResult(
          hostVersion: '1.0',
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

      final ReconnectService service = buildService(
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
        when(() => authenticationService.helloLastKnownHost()).thenAnswer(
          (_) async => Fixtures.buildHelloResult(
            hostVersion: '1.0',
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final ReconnectService service = buildService();

        service.onOrdinaryTransportLoss(_uri);
        await pumpEventQueue();

        verifyNever(() => sessionService.connect(any()));
        verifyNever(() => authenticationService.helloLastKnownHost());
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
