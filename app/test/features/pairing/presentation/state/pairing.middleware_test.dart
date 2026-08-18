import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/cancel_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/confirm_pairing_code.params.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing_renotify.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

import '../../fixtures/pairing.fixture.dart';

/// Mocks for the use cases [PairingMiddleware] resolves through [sl].
class MockAuthenticateUseCase extends Mock implements AuthenticateUseCase {}

class MockRequestPairingUseCase extends Mock implements RequestPairingUseCase {}

class MockConfirmPairingCodeUseCase extends Mock
    implements ConfirmPairingCodeUseCase {}

class MockRequestPairingRenotifyUseCase extends Mock
    implements RequestPairingRenotifyUseCase {}

class MockCancelPairingUseCase extends Mock implements CancelPairingUseCase {}

class MockDisconnectUseCase extends Mock implements DisconnectUseCase {}

/// Mocktail double for [NavigatorService], matching this project's existing
/// mock-the-concrete-class convention for it (see `navigator_service_test.dart`'s `MockGoRouter`).
class MockNavigatorService extends Mock implements NavigatorService {}

/// Mocktail double for [Store], called directly rather than dispatched
/// through -- `dispatch` and the middleware's own `next` both append to one
/// action log, so no real reducer is involved and `store.state` is exactly
/// whatever a test stubs.
class MockStore extends Mock implements Store<AppState> {}

/// Builds an [AppState] with the given pairing [phase], the only field
/// [PairingMiddleware] itself ever reads from the Store.
AppState _stateWithPhase(PairingPhase phase) => AppState(
  connection: ConnectionState.initial(),
  pairing: PairingState(
    phase: phase,
    bridgeVersion: null,
    error: null,
    codeExpiresAt: null,
    renotifyAvailableAt: null,
  ),
);

/// Exercises [PairingMiddleware] in isolation: each test calls
/// `middleware.call(store, action, next)` directly with the action under
/// test, rather than dispatching through a real Store -- `next` and the
/// mocked `store.dispatch` both log into [actionLog], so any action the
/// middleware itself dispatches is observed there, never reduced.
void main() {
  late PairingMiddleware middleware;
  late MockAuthenticateUseCase mockAuthenticate;
  late MockRequestPairingUseCase mockRequestPairing;
  late MockConfirmPairingCodeUseCase mockConfirmPairingCode;
  late MockDisconnectUseCase mockDisconnect;
  late MockNavigatorService mockNavigatorService;
  late MockStore store;
  late List<Object?> actionLog;

  void next(dynamic action) => actionLog.add(action);

  setUpAll(() {
    registerFallbackValue(NoParams());
  });

  setUp(() async {
    await sl.reset();
    middleware = PairingMiddleware();
    mockAuthenticate = MockAuthenticateUseCase();
    mockRequestPairing = MockRequestPairingUseCase();
    mockConfirmPairingCode = MockConfirmPairingCodeUseCase();
    mockDisconnect = MockDisconnectUseCase();
    mockNavigatorService = MockNavigatorService();
    sl.registerLazySingleton<AuthenticateUseCase>(() => mockAuthenticate);
    sl.registerLazySingleton<RequestPairingUseCase>(() => mockRequestPairing);
    sl.registerLazySingleton<ConfirmPairingCodeUseCase>(
      () => mockConfirmPairingCode,
    );
    sl.registerLazySingleton<RequestPairingRenotifyUseCase>(
      () => MockRequestPairingRenotifyUseCase(),
    );
    sl.registerLazySingleton<CancelPairingUseCase>(
      () => MockCancelPairingUseCase(),
    );
    sl.registerLazySingleton<DisconnectUseCase>(() => mockDisconnect);
    sl.registerLazySingleton<NavigatorService>(() => mockNavigatorService);

    actionLog = [];
    store = MockStore();
    when(() => store.dispatch(any())).thenAnswer(
      (Invocation invocation) =>
          actionLog.add(invocation.positionalArguments[0]),
    );
    // Baseline default for _scheduleReconnect's disconnected-phase check; every test that
    // reaches it runs inside fakeAsync and elapses its own timer before returning, so this value
    // only matters to tests that don't override it with a more specific phase.
    when(() => store.state).thenReturn(_stateWithPhase(PairingPhase.none));
  });

  tearDown(() async {
    await sl.reset();
    reset(mockAuthenticate);
    reset(mockRequestPairing);
    reset(mockConfirmPairingCode);
    reset(mockDisconnect);
    reset(mockNavigatorService);
    reset(store);
  });

  group('PairingMiddleware — PairingStartedAction', () {
    test(
      'dispatches PairingAuthenticatedAction when authentication succeeds',
      () async {
        final PairingHandshakeEntity handshake = buildPairingHandshakeEntity(
          trusted: false,
        );
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) async => Right(handshake));

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog[0], isA<PairingStartedAction>());
        expect(actionLog[1], isA<PairingAuthenticatedAction>());
        expect(
          (actionLog[1] as PairingAuthenticatedAction).bridgeVersion,
          handshake.bridgeVersion,
        );
        expect(
          (actionLog[1] as PairingAuthenticatedAction).trusted,
          handshake.trusted,
        );
        expect(
          (actionLog[1] as PairingAuthenticatedAction)
              .credentialRejectedMessage,
          isNull,
        );
        verify(() => mockAuthenticate(any())).called(1);
      },
    );

    test(
      'dispatches PairingAuthenticatedAction carrying the credential-rejected message through',
      () async {
        final PairingHandshakeEntity handshake = buildPairingHandshakeEntity(
          trusted: false,
          credentialRejectedMessage: "This device's trust was revoked.",
        );
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) async => Right(handshake));

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(
          (actionLog[1] as PairingAuthenticatedAction)
              .credentialRejectedMessage,
          "This device's trust was revoked.",
        );
      },
    );

    test(
      'dispatches PairingDisconnectedAction when authentication fails with a NetworkFailure',
      () {
        fakeAsync((FakeAsync async) {
          const NetworkFailure failure = NetworkFailure('unreachable');
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) async => const Left(failure));

          middleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();

          expect(actionLog, [
            isA<PairingStartedAction>(),
            isA<PairingDisconnectedAction>(),
          ]);

          // A NetworkFailure also schedules a real Future.delayed reconnect (_scheduleReconnect);
          // drive it to completion inside this fakeAsync zone instead of leaving it pending as a
          // genuine 3-second timer that would fire during a later test, after tearDown resets
          // store's stubs. store.state's baseline stub (PairingPhase.none, set in setUp) means the
          // reconnect's disconnected-phase check is false, so this does not redispatch.
          async.elapse(middleware.reconnectDelay);
          async.flushMicrotasks();
        });
      },
    );

    test(
      'dispatches PairingFailedAction when authentication fails with a non-network failure',
      () async {
        const PairingFailure failure = PairingFailure('rejected');
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) async => const Left(failure));

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog[0], isA<PairingStartedAction>());
        expect(actionLog[1], const PairingFailedAction('rejected'));
      },
    );

    test(
      'schedules a retry that dispatches PairingStartedAction again after reconnectDelay when the Store still reports disconnected',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final PairingMiddleware retryMiddleware = PairingMiddleware(
            reconnectDelay: delay,
          );
          const NetworkFailure failure = NetworkFailure('unreachable');
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) async => const Left(failure));
          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.disconnected));

          retryMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();

          async.elapse(delay);
          async.flushMicrotasks();

          // The retry's redispatch is only ever logged here, never fed back
          // through the middleware -- it does not itself call authenticate
          // again, matching how a directly-invoked middleware call never
          // recurses through its own dispatched actions.
          expect(actionLog.whereType<PairingStartedAction>(), hasLength(2));
          verify(() => mockAuthenticate(any())).called(1);
        });
      },
    );

    test(
      'does not retry once the Store no longer reports disconnected (e.g. disposed) before reconnectDelay elapses',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final PairingMiddleware retryMiddleware = PairingMiddleware(
            reconnectDelay: delay,
          );
          const NetworkFailure failure = NetworkFailure('unreachable');
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) async => const Left(failure));
          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.disconnected));

          retryMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();

          // Disposes well before delay elapses -- PairingDisposedAction's
          // reducer would reset the phase away from disconnected; simulated
          // directly since no real reducer runs against a mocked Store.
          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.none));
          async.elapse(delay);
          async.flushMicrotasks();

          expect(actionLog.whereType<PairingStartedAction>(), hasLength(1));
          verify(() => mockAuthenticate(any())).called(1);
        });
      },
    );

    test(
      'does not retry once the Store reports a phase other than disconnected before reconnectDelay elapses',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final PairingMiddleware retryMiddleware = PairingMiddleware(
            reconnectDelay: delay,
          );
          const NetworkFailure failure = NetworkFailure('unreachable');
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) async => const Left(failure));
          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.disconnected));

          retryMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();

          // A real reconnect (e.g. a manual retry landing before the
          // scheduled one) would move the phase off disconnected via the
          // reducer; simulated directly since no real reducer runs against
          // a mocked Store.
          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.unpaired));
          async.elapse(delay);
          async.flushMicrotasks();

          expect(actionLog.whereType<PairingStartedAction>(), hasLength(1));
          verify(() => mockAuthenticate(any())).called(1);
        });
      },
    );
  });

  group('PairingMiddleware — PairingCodeRequestedAction', () {
    test(
      'dispatches PairingCodeAvailableAction when the request succeeds',
      () async {
        when(
          () => mockRequestPairing(any()),
        ).thenAnswer((_) async => const Right(null));

        middleware.call(store, const PairingCodeRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          isA<PairingCodeRequestedAction>(),
          isA<PairingCodeAvailableAction>(),
        ]);
      },
    );

    test(
      'dispatches PairingCodeAvailableAction with expiresInSeconds forwarded',
      () async {
        when(
          () => mockRequestPairing(any()),
        ).thenAnswer((_) async => const Right(30));

        middleware.call(store, const PairingCodeRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          isA<PairingCodeRequestedAction>(),
          const PairingCodeAvailableAction(expiresInSeconds: 30),
        ]);
      },
    );

    test('dispatches PairingFailedAction when the request fails', () async {
      const PairingFailure failure = PairingFailure('unavailable');
      when(
        () => mockRequestPairing(any()),
      ).thenAnswer((_) async => const Left(failure));

      middleware.call(store, const PairingCodeRequestedAction(), next);
      await Future<void>.delayed(Duration.zero);

      expect(actionLog[0], isA<PairingCodeRequestedAction>());
      expect(actionLog[1], const PairingFailedAction('unavailable'));
    });
  });

  group('PairingMiddleware — PairingCodeSubmittedAction', () {
    test(
      'dispatches PairingConfirmedAction and forwards code/displayName when confirmation succeeds',
      () async {
        when(
          () => mockConfirmPairingCode(
            const ConfirmPairingCodeParams(
              code: '123456',
              displayName: 'Desktop',
            ),
          ),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(
          store,
          const PairingCodeSubmittedAction(
            code: '123456',
            displayName: 'Desktop',
          ),
          next,
        );
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          const PairingCodeSubmittedAction(
            code: '123456',
            displayName: 'Desktop',
          ),
          const PairingConfirmedAction(),
        ]);
        verify(
          () => mockConfirmPairingCode(
            const ConfirmPairingCodeParams(
              code: '123456',
              displayName: 'Desktop',
            ),
          ),
        ).called(1);
      },
    );

    test('dispatches PairingFailedAction when confirmation fails', () async {
      const PairingFailure failure = PairingFailure('invalid');
      when(
        () => mockConfirmPairingCode(
          const ConfirmPairingCodeParams(code: '000000'),
        ),
      ).thenAnswer((_) async => const Left(failure));

      middleware.call(
        store,
        const PairingCodeSubmittedAction(code: '000000'),
        next,
      );
      await Future<void>.delayed(Duration.zero);

      expect(actionLog, [
        const PairingCodeSubmittedAction(code: '000000'),
        const PairingFailedAction('invalid'),
      ]);
    });
  });

  group('PairingMiddleware — PairingDisposedAction', () {
    test(
      'calls DisconnectUseCase as a best-effort cleanup when not yet trusted',
      () async {
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [const PairingDisposedAction(wasTrusted: false)]);
        verify(() => mockDisconnect(any())).called(1);
      },
    );

    test(
      'does not call DisconnectUseCase when pairing had already succeeded',
      () async {
        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: true),
          next,
        );
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [const PairingDisposedAction(wasTrusted: true)]);
        verifyNever(() => mockDisconnect(any()));
      },
    );
  });

  group('PairingMiddleware — PairingRenotifyRequestedAction', () {
    test(
      'dispatches PairingRenotifySucceededAction when renotify succeeds',
      () async {
        final mockRenotifyUseCase =
            sl<RequestPairingRenotifyUseCase>()
                as MockRequestPairingRenotifyUseCase;
        when(
          () => mockRenotifyUseCase(any()),
        ).thenAnswer((_) async => const Right(null));

        middleware.call(store, const PairingRenotifyRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          isA<PairingRenotifyRequestedAction>(),
          isA<PairingRenotifySucceededAction>(),
        ]);
        verify(() => mockRenotifyUseCase(any())).called(1);
      },
    );

    test(
      'dispatches PairingRenotifyCooldownAction with seconds when renotify is in cooldown',
      () async {
        final mockRenotifyUseCase =
            sl<RequestPairingRenotifyUseCase>()
                as MockRequestPairingRenotifyUseCase;
        when(
          () => mockRenotifyUseCase(any()),
        ).thenAnswer((_) async => const Right(5));

        middleware.call(store, const PairingRenotifyRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog.length, 2);
        expect(actionLog[0], isA<PairingRenotifyRequestedAction>());
        expect(actionLog[1], isA<PairingRenotifyCooldownAction>());
        expect(
          (actionLog[1] as PairingRenotifyCooldownAction).retryAfterSeconds,
          5,
        );
        verify(() => mockRenotifyUseCase(any())).called(1);
      },
    );

    test(
      'dispatches PairingRenotifyCooldownAction with zero when renotify cooldown is immediate',
      () async {
        final mockRenotifyUseCase =
            sl<RequestPairingRenotifyUseCase>()
                as MockRequestPairingRenotifyUseCase;
        when(
          () => mockRenotifyUseCase(any()),
        ).thenAnswer((_) async => const Right(0));

        middleware.call(store, const PairingRenotifyRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(
          (actionLog[1] as PairingRenotifyCooldownAction).retryAfterSeconds,
          0,
        );
      },
    );

    test('dispatches PairingFailedAction when renotify fails', () async {
      final mockRenotifyUseCase =
          sl<RequestPairingRenotifyUseCase>()
              as MockRequestPairingRenotifyUseCase;
      const PairingFailure failure = PairingFailure('no challenge active');
      when(
        () => mockRenotifyUseCase(any()),
      ).thenAnswer((_) async => const Left(failure));

      middleware.call(store, const PairingRenotifyRequestedAction(), next);
      await Future<void>.delayed(Duration.zero);

      expect(actionLog[0], isA<PairingRenotifyRequestedAction>());
      expect(actionLog[1], const PairingFailedAction('no challenge active'));
    });
  });

  group('PairingMiddleware — PairingCancelRequestedAction', () {
    test(
      'dispatches PairingCancelSucceededAction when cancel succeeds',
      () async {
        final mockCancelUseCase =
            sl<CancelPairingUseCase>() as MockCancelPairingUseCase;
        when(
          () => mockCancelUseCase(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(store, const PairingCancelRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          isA<PairingCancelRequestedAction>(),
          isA<PairingCancelSucceededAction>(),
        ]);
        verify(() => mockCancelUseCase(any())).called(1);
      },
    );

    test('dispatches PairingFailedAction when cancel fails', () async {
      final mockCancelUseCase =
          sl<CancelPairingUseCase>() as MockCancelPairingUseCase;
      const NetworkFailure failure = NetworkFailure('connection lost');
      when(
        () => mockCancelUseCase(any()),
      ).thenAnswer((_) async => const Left(failure));

      middleware.call(store, const PairingCancelRequestedAction(), next);
      await Future<void>.delayed(Duration.zero);

      expect(actionLog[0], isA<PairingCancelRequestedAction>());
      expect(actionLog[1], const PairingFailedAction('connection lost'));
    });
  });

  group('PairingMiddleware — PairingBackRequestedAction', () {
    test('navigates to the home route', () {
      middleware.call(store, const PairingBackRequestedAction(), next);

      expect(actionLog, [const PairingBackRequestedAction()]);
      verify(() => mockNavigatorService.go(AppRoutes.home)).called(1);
    });
  });
}
