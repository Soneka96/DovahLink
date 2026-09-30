import 'dart:async';

import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/cancel_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/observe_connection_status.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/confirm_pairing_code.params.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing_renotify.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import '../../../../fixtures/fixtures.dart';

/// Mocks for the use cases [PairingMiddleware] resolves through [sl].
class MockAuthenticateUseCase extends Mock implements AuthenticateUseCase {}

class MockRequestPairingUseCase extends Mock implements RequestPairingUseCase {}

class MockConfirmPairingCodeUseCase extends Mock
    implements ConfirmPairingCodeUseCase {}

class MockRequestPairingRenotifyUseCase extends Mock
    implements RequestPairingRenotifyUseCase {}

class MockCancelPairingUseCase extends Mock implements CancelPairingUseCase {}

class MockDisconnectUseCase extends Mock implements DisconnectUseCase {}

class MockObserveConnectionStatusUseCase extends Mock
    implements ObserveConnectionStatusUseCase {}

/// Mocktail double for [Store], called directly rather than dispatched
/// through -- `dispatch` and the middleware's own `next` both append to one
/// action log, so no real reducer is involved and `store.state` is exactly
/// whatever a test stubs.
class MockStore extends Mock implements Store<AppState> {}

/// Builds an [AppState] with the given pairing [phase] and the selected [host] (the representative
/// Host when omitted) -- the two things [PairingMiddleware] itself reads from the Store.
AppState _stateWithPhase(
  PairingPhase phase, {
  Host? host,
  String? pendingPairingHostId,
  ConnectionHostSelectionSource source =
      ConnectionHostSelectionSource.candidate,
  PairingSupport support = PairingSupport.available,
}) => AppState(
  connection: ConnectionState(
    selectedHost: host ?? Fixtures.buildHost(),
    selectedHostSource: source,
    pendingPairingHostId: pendingPairingHostId,
  ),
  pairing: PairingState(
    phase: phase,
    support: support,
    hostVersion: null,
    error: null,
    codeExpiresAt: null,
    renotifyAvailableAt: null,
  ),
);

/// Builds an [AppState] with no Host selected, in the initial pairing phase.
AppState _stateWithoutSelectedHost() => AppState(
  connection: ConnectionState.initial(),
  pairing: PairingState.initial(),
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
  late MockObserveConnectionStatusUseCase mockObserveConnectionStatus;
  late MockStore store;
  late List<Object?> actionLog;

  void next(dynamic action) => actionLog.add(action);

  setUpAll(() {
    registerFallbackValue(NoParams());
    registerFallbackValue(Fixtures.buildAuthenticateParams());
    registerFallbackValue(const ConfirmPairingCodeParams(code: '000000'));
  });

  setUp(() async {
    await sl.reset();
    middleware = PairingMiddleware();
    mockAuthenticate = MockAuthenticateUseCase();
    mockRequestPairing = MockRequestPairingUseCase();
    mockConfirmPairingCode = MockConfirmPairingCodeUseCase();
    mockDisconnect = MockDisconnectUseCase();
    mockObserveConnectionStatus = MockObserveConnectionStatusUseCase();
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
    sl.registerLazySingleton<ObserveConnectionStatusUseCase>(
      () => mockObserveConnectionStatus,
    );

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

  test('does not authenticate when secure storage is unavailable', () {
    when(() => store.state).thenReturn(
      _stateWithPhase(
        PairingPhase.none,
        support: PairingSupport.secureStorageUnavailable,
      ),
    );

    middleware.call(store, const PairingStartedAction(), next);

    expect(actionLog, [isA<PairingStartedAction>()]);
    verifyNever(() => mockAuthenticate(any()));
  });

  tearDown(() async {
    await sl.reset();
    reset(mockAuthenticate);
    reset(mockRequestPairing);
    reset(mockConfirmPairingCode);
    reset(mockDisconnect);
    reset(mockObserveConnectionStatus);
    reset(store);
  });

  group('PairingMiddleware processes PairingStartedAction correctly', () {
    test(
      'PairingStartedAction dispatches PairingAuthenticatedAction then PairingCodeRequestedAction when an unpaired session authenticates',
      () async {
        final PairingHandshake handshake = Fixtures.buildPairingHandshake(
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
          (actionLog[1] as PairingAuthenticatedAction).hostVersion,
          handshake.hostVersion,
        );
        expect(
          (actionLog[1] as PairingAuthenticatedAction).trusted,
          handshake.trusted,
        );
        expect(
          (actionLog[1] as PairingAuthenticatedAction)
              .credentialRejectionReason,
          isNull,
        );
        expect(
          (actionLog[1] as PairingAuthenticatedAction)
              .credentialRejectedMessage,
          isNull,
        );
        // An untrusted (unpaired) session must not start observing invalidation -- there is no
        // trusted session yet to invalidate -- and asks for its code through the dispatched
        // action rather than calling the use case itself.
        expect(actionLog, hasLength(3));
        expect(actionLog[2], const PairingCodeRequestedAction());
        verify(() => mockAuthenticate(any())).called(1);
        verifyNever(() => mockRequestPairing(any()));
      },
    );

    test(
      'PairingStartedAction dispatches PairingCodeRequestedAction exactly once per unpaired authentication',
      () async {
        when(() => mockAuthenticate(any())).thenAnswer(
          (_) async => Right(Fixtures.buildPairingHandshake(trusted: false)),
        );

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog.whereType<PairingCodeRequestedAction>(), hasLength(1));

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog.whereType<PairingCodeRequestedAction>(), hasLength(2));
        expect(actionLog.whereType<PairingAuthenticatedAction>(), hasLength(2));
      },
    );

    test(
      'PairingStartedAction does not dispatch PairingCodeRequestedAction when the session is already trusted',
      () async {
        when(() => mockAuthenticate(any())).thenAnswer(
          (_) async => Right(Fixtures.buildPairingHandshake(trusted: true)),
        );
        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => const Stream<PairingConnectionStatus>.empty());

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog.whereType<PairingCodeRequestedAction>(), isEmpty);
      },
    );

    test(
      'PairingStartedAction does not dispatch PairingCodeRequestedAction when a rejected credential needs confirmation',
      () async {
        when(() => mockAuthenticate(any())).thenAnswer(
          (_) async => Right(
            Fixtures.buildPairingHandshake(
              trusted: false,
              credentialRejectionReason:
                  PairingCredentialRejectionReason.revoked,
              credentialRejectedMessage: "This device's trust was revoked.",
            ),
          ),
        );

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          isA<PairingStartedAction>(),
          isA<PairingAuthenticatedAction>(),
        ]);
        verifyNever(() => mockRequestPairing(any()));
      },
    );

    test(
      'PairingStartedAction does not dispatch PairingCodeRequestedAction for a blocked credential',
      () async {
        when(() => mockAuthenticate(any())).thenAnswer(
          (_) async => Right(
            Fixtures.buildPairingHandshake(
              trusted: false,
              credentialRejectionReason:
                  PairingCredentialRejectionReason.blocked,
              credentialRejectedMessage: 'This device is blocked by the Host.',
            ),
          ),
        );

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog.whereType<PairingCodeRequestedAction>(), isEmpty);
        expect(
          actionLog
              .whereType<PairingAuthenticatedAction>()
              .single
              .credentialRejectionReason,
          PairingCredentialRejectionReason.blocked,
        );
        verifyNever(() => mockRequestPairing(any()));
      },
    );

    test(
      'PairingStartedAction does not dispatch PairingCodeRequestedAction when authentication fails',
      () async {
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) async => const Left(PairingFailure('rejected')));

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog.whereType<PairingCodeRequestedAction>(), isEmpty);
        expect(actionLog.whereType<PairingFailedAction>(), hasLength(1));
      },
    );

    test(
      'PairingStartedAction authenticates with exactly the selected Host URI',
      () async {
        final Host host = Fixtures.buildHost(
          displayName: 'Second Host',
          uri: Uri.parse('ws://192.168.1.11:2000/'),
        );
        when(
          () => store.state,
        ).thenReturn(_stateWithPhase(PairingPhase.none, host: host));
        when(() => mockAuthenticate(any())).thenAnswer(
          (_) async => Right(Fixtures.buildPairingHandshake(trusted: false)),
        );

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        verify(
          () => mockAuthenticate(
            AuthenticateParams(hostUri: Uri.parse('ws://192.168.1.11:2000/')),
          ),
        ).called(1);
      },
    );

    test('PairingStartedAction authenticates a Known Host by ID', () async {
      final Host host = Fixtures.buildHost(
        displayName: 'Known Host',
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      when(() => store.state).thenReturn(
        _stateWithPhase(
          PairingPhase.none,
          host: host,
          source: ConnectionHostSelectionSource.knownHost,
        ),
      );
      when(() => mockAuthenticate(any())).thenAnswer(
        (_) async => Right(Fixtures.buildPairingHandshake(trusted: false)),
      );

      middleware.call(store, const PairingStartedAction(), next);
      await Future<void>.delayed(Duration.zero);

      verify(
        () =>
            mockAuthenticate(AuthenticateParams.knownHost(hostId: host.hostId)),
      ).called(1);
    });

    test(
      'PairingStartedAction authenticates by URI when two Hosts share a display name',
      () async {
        final Host first = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://192.168.1.10:1000/'),
        );
        final Host second = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://192.168.1.11:2000/'),
        );
        when(() => mockAuthenticate(any())).thenAnswer(
          (_) async => Right(Fixtures.buildPairingHandshake(trusted: false)),
        );

        when(
          () => store.state,
        ).thenReturn(_stateWithPhase(PairingPhase.none, host: first));
        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);
        when(
          () => store.state,
        ).thenReturn(_stateWithPhase(PairingPhase.none, host: second));
        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        verifyInOrder([
          () => mockAuthenticate(AuthenticateParams(hostUri: first.uri)),
          () => mockAuthenticate(AuthenticateParams(hostUri: second.uri)),
        ]);
      },
    );

    test(
      'PairingStartedAction dispatches PairingFailedAction and never authenticates when no Host is selected',
      () async {
        when(() => store.state).thenReturn(_stateWithoutSelectedHost());

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          isA<PairingStartedAction>(),
          const PairingFailedAction('Select a Host to pair with.'),
        ]);
        verifyNever(() => mockAuthenticate(any()));
      },
    );

    test(
      'PairingStartedAction also dispatches PairingSessionTrustedAction when authentication '
      'presents an already-trusted credential',
      () async {
        final PairingHandshake handshake = Fixtures.buildPairingHandshake(
          trusted: true,
        );
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) async => Right(handshake));
        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => const Stream<PairingConnectionStatus>.empty());

        middleware.call(store, const PairingStartedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          isA<PairingStartedAction>(),
          isA<PairingAuthenticatedAction>(),
          const PairingSessionTrustedAction(),
        ]);
      },
    );

    test(
      'PairingStartedAction maps a successful automatic retry to the SDK authenticated state without scheduling another retry',
      () {
        fakeAsync((FakeAsync async) {
          final PairingMiddleware retryMiddleware = PairingMiddleware(
            reconnectDelay: const Duration(seconds: 3),
          );
          final PairingHandshake handshake = Fixtures.buildPairingHandshake(
            trusted: true,
          );
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) async => Right(handshake));

          retryMiddleware.call(
            store,
            const PairingStartedAction(isAutomaticRetry: true),
            next,
          );
          async.flushMicrotasks();

          expect(actionLog, [
            const PairingStartedAction(isAutomaticRetry: true),
            isA<PairingAuthenticatedAction>(),
            const PairingSessionTrustedAction(),
          ]);
          async.elapse(const Duration(seconds: 3));
          async.flushMicrotasks();
          expect(actionLog.whereType<PairingStartedAction>(), hasLength(1));
          verify(() => mockAuthenticate(any())).called(1);
        });
      },
    );

    test(
      'PairingStartedAction dispatches PairingAuthenticatedAction carrying the credential-rejected message through',
      () async {
        final PairingHandshake handshake = Fixtures.buildPairingHandshake(
          trusted: false,
          credentialRejectionReason: PairingCredentialRejectionReason.revoked,
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
        expect(
          (actionLog[1] as PairingAuthenticatedAction)
              .credentialRejectionReason,
          PairingCredentialRejectionReason.revoked,
        );
      },
    );

    test(
      'PairingStartedAction dispatches PairingDisconnectedAction when authentication fails with a NetworkFailure',
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
      'PairingStartedAction dispatches PairingFailedAction when authentication fails with a non-network failure',
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

    test('PairingStartedAction dispatches PairingFailedAction, and never '
        'schedules a reconnect, when authentication fails with a '
        'SessionInvalidatedFailure', () {
      fakeAsync((FakeAsync async) {
        const SessionInvalidatedFailure failure = SessionInvalidatedFailure(
          'disconnected by the host',
        );
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) async => const Left(failure));
        when(
          () => store.state,
        ).thenReturn(_stateWithPhase(PairingPhase.disconnected));

        middleware.call(store, const PairingStartedAction(), next);
        async.flushMicrotasks();

        expect(actionLog, [
          isA<PairingStartedAction>(),
          const PairingFailedAction('disconnected by the host'),
        ]);

        // Administrative invalidation must never auto-retry, unlike NetworkFailure above --
        // elapsing the same reconnectDelay proves no PairingStartedAction was scheduled, even
        // with store.state stubbed to the disconnected phase _scheduleReconnect checks for.
        async.elapse(middleware.reconnectDelay);
        async.flushMicrotasks();

        expect(actionLog.whereType<PairingStartedAction>(), hasLength(1));
      });
    });

    test(
      'PairingStartedAction dispatches marked automatic retries after each failed attempt',
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

          final PairingStartedAction firstRetry = actionLog
              .whereType<PairingStartedAction>()
              .last;
          expect(firstRetry.isAutomaticRetry, isTrue);

          actionLog.clear();
          retryMiddleware.call(store, firstRetry, next);
          async.flushMicrotasks();
          async.elapse(delay);
          async.flushMicrotasks();

          expect(
            actionLog.whereType<PairingStartedAction>().every(
              (PairingStartedAction action) => action.isAutomaticRetry,
            ),
            isTrue,
          );
          expect(actionLog.whereType<PairingStartedAction>(), hasLength(2));
          verify(() => mockAuthenticate(any())).called(2);
        });
      },
    );

    test(
      'PairingStartedAction lets an explicit retry replace a scheduled automatic retry',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final PairingMiddleware retryMiddleware = PairingMiddleware(
            reconnectDelay: delay,
          );
          int authenticationCount = 0;
          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.disconnected));
          when(() => mockAuthenticate(any())).thenAnswer((_) async {
            authenticationCount++;
            if (authenticationCount == 1) {
              return const Left(NetworkFailure('unreachable'));
            }
            return Right(Fixtures.buildPairingHandshake(trusted: true));
          });
          when(
            () => mockObserveConnectionStatus(any()),
          ).thenAnswer((_) => const Stream<PairingConnectionStatus>.empty());

          retryMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();
          retryMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();
          async.elapse(delay);
          async.flushMicrotasks();

          expect(actionLog.whereType<PairingStartedAction>(), hasLength(2));
          expect(actionLog.last, const PairingSessionTrustedAction());
          verify(() => mockAuthenticate(any())).called(2);
        });
      },
    );

    test(
      'PairingDisposedAction cancels a pending automatic retry while waiting offline',
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
          when(
            () => mockDisconnect(any()),
          ).thenAnswer((_) async => const Right(unit));

          retryMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();

          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.none));
          retryMiddleware.call(
            store,
            const PairingDisposedAction(wasTrusted: false),
            next,
          );
          async.flushMicrotasks();
          async.elapse(delay);
          async.flushMicrotasks();

          expect(actionLog.whereType<PairingStartedAction>(), hasLength(1));
          verify(() => mockAuthenticate(any())).called(1);
          verify(() => mockDisconnect(any())).called(1);
        });
      },
    );

    test(
      'PairingDisposedAction suppresses an authentication failure that completes late',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final PairingMiddleware pendingMiddleware = PairingMiddleware(
            reconnectDelay: delay,
          );
          final Completer<Either<Failure, PairingHandshake>> authentication =
              Completer<Either<Failure, PairingHandshake>>();
          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.disconnected));
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) => authentication.future);
          when(
            () => mockDisconnect(any()),
          ).thenAnswer((_) async => const Right(unit));

          pendingMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();
          pendingMiddleware.call(
            store,
            const PairingDisposedAction(wasTrusted: false),
            next,
          );
          async.flushMicrotasks();
          authentication.complete(const Left(NetworkFailure('unreachable')));
          async.flushMicrotasks();
          async.elapse(delay);
          async.flushMicrotasks();

          expect(actionLog, [
            isA<PairingStartedAction>(),
            const PairingDisposedAction(wasTrusted: false),
          ]);
          verify(() => mockAuthenticate(any())).called(1);
          verify(() => mockDisconnect(any())).called(1);
        });
      },
    );

    test(
      'PairingDisposedAction suppresses a successful authentication that completes late',
      () {
        fakeAsync((FakeAsync async) {
          final PairingMiddleware pendingMiddleware = PairingMiddleware();
          final Completer<Either<Failure, PairingHandshake>> authentication =
              Completer<Either<Failure, PairingHandshake>>();
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) => authentication.future);
          when(
            () => mockDisconnect(any()),
          ).thenAnswer((_) async => const Right(unit));

          pendingMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();
          pendingMiddleware.call(
            store,
            const PairingDisposedAction(wasTrusted: false),
            next,
          );
          async.flushMicrotasks();
          authentication.complete(
            Right(Fixtures.buildPairingHandshake(trusted: false)),
          );
          async.flushMicrotasks();

          expect(actionLog, [
            isA<PairingStartedAction>(),
            const PairingDisposedAction(wasTrusted: false),
          ]);
          verify(() => mockAuthenticate(any())).called(1);
          verify(() => mockDisconnect(any())).called(1);
        });
      },
    );

    test(
      'PairingStartedAction lets a new flow succeed and ignores the older result',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final PairingMiddleware flowMiddleware = PairingMiddleware(
            reconnectDelay: delay,
          );
          final Completer<Either<Failure, PairingHandshake>>
          olderAuthentication = Completer<Either<Failure, PairingHandshake>>();
          final Completer<Either<Failure, PairingHandshake>>
          currentAuthentication =
              Completer<Either<Failure, PairingHandshake>>();
          int authenticationCount = 0;
          when(
            () => store.state,
          ).thenReturn(_stateWithPhase(PairingPhase.disconnected));
          when(() => mockAuthenticate(any())).thenAnswer((_) {
            authenticationCount++;
            return authenticationCount == 1
                ? olderAuthentication.future
                : currentAuthentication.future;
          });
          when(
            () => mockDisconnect(any()),
          ).thenAnswer((_) async => const Right(unit));
          when(
            () => mockObserveConnectionStatus(any()),
          ).thenAnswer((_) => const Stream<PairingConnectionStatus>.empty());

          flowMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();
          flowMiddleware.call(
            store,
            const PairingDisposedAction(wasTrusted: false),
            next,
          );
          async.flushMicrotasks();
          flowMiddleware.call(store, const PairingStartedAction(), next);
          async.flushMicrotasks();
          currentAuthentication.complete(
            Right(Fixtures.buildPairingHandshake(trusted: true)),
          );
          async.flushMicrotasks();
          final int actionsAfterCurrentFlow = actionLog.length;

          olderAuthentication.complete(
            Right(Fixtures.buildPairingHandshake(trusted: false)),
          );
          async.flushMicrotasks();
          async.elapse(delay);
          async.flushMicrotasks();

          expect(actionLog.length, actionsAfterCurrentFlow);
          expect(actionLog.whereType<PairingStartedAction>(), hasLength(2));
          expect(actionLog.last, const PairingSessionTrustedAction());
          verify(() => mockAuthenticate(any())).called(2);
          verify(() => mockDisconnect(any())).called(1);
        });
      },
    );

    test(
      'PairingStartedAction does not retry once the Store reports a phase other than disconnected before reconnectDelay elapses',
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

    test('shutdown cancels a pending pairing retry', () {
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
        bool shutdownCompleted = false;
        retryMiddleware.shutdown().then((_) => shutdownCompleted = true);
        async.flushMicrotasks();
        async.elapse(delay);
        async.flushMicrotasks();

        expect(shutdownCompleted, isTrue);
        expect(actionLog.whereType<PairingStartedAction>(), hasLength(1));
        verify(() => mockAuthenticate(any())).called(1);
      });
    });
  });

  group('PairingMiddleware shutdown behaves correctly', () {
    test(
      'shutdown suppresses follow-up pairing work when authentication completes late',
      () async {
        final Completer<Either<Failure, PairingHandshake>> authentication =
            Completer<Either<Failure, PairingHandshake>>();
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) => authentication.future);

        middleware.call(store, const PairingStartedAction(), next);
        await middleware.shutdown();
        authentication.complete(
          Right(Fixtures.buildPairingHandshake(trusted: false)),
        );
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [const PairingStartedAction()]);
        verifyNever(() => mockRequestPairing(any()));
      },
    );

    test(
      'shutdown suppresses code availability when a code request completes late',
      () async {
        final Completer<Either<Failure, int?>> request =
            Completer<Either<Failure, int?>>();
        when(() => mockRequestPairing(any())).thenAnswer((_) => request.future);

        middleware.call(store, const PairingCodeRequestedAction(), next);
        await middleware.shutdown();
        request.complete(const Right(60));
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [const PairingCodeRequestedAction()]);
      },
    );

    test(
      'shutdown suppresses trust actions when code confirmation completes late',
      () async {
        final Completer<Either<Failure, Unit>> confirmation =
            Completer<Either<Failure, Unit>>();
        when(
          () => mockConfirmPairingCode(any()),
        ).thenAnswer((_) => confirmation.future);

        middleware.call(
          store,
          const PairingCodeSubmittedAction(code: '123456'),
          next,
        );
        await middleware.shutdown();
        confirmation.complete(const Right(unit));
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          const PairingCodeSubmittedAction(code: '123456'),
          ConnectionCandidatePairingStartedAction(Fixtures.buildHost().hostId),
        ]);
        verifyNever(() => mockObserveConnectionStatus(any()));
      },
    );

    test(
      'shutdown prevents later actions from starting pairing work',
      () async {
        await middleware.shutdown();

        middleware.call(store, const PairingStartedAction(), next);

        expect(actionLog, [const PairingStartedAction()]);
        verifyNever(() => mockAuthenticate(any()));
      },
    );

    test('shutdown cancels the trusted-session observation', () async {
      final StreamController<PairingConnectionStatus> controller =
          StreamController<PairingConnectionStatus>.broadcast(sync: true);
      addTearDown(controller.close);
      when(
        () => mockObserveConnectionStatus(any()),
      ).thenAnswer((_) => controller.stream);
      middleware.call(store, const PairingSessionTrustedAction(), next);
      await pumpEventQueue();
      expect(controller.hasListener, isTrue);

      await middleware.shutdown();
      controller.add(PairingConnectionStatus.lost);
      await pumpEventQueue();

      expect(controller.hasListener, isFalse);
      expect(actionLog, [const PairingSessionTrustedAction()]);
      middleware.call(store, const PairingSessionTrustedAction(), next);
      verify(() => mockObserveConnectionStatus(any())).called(1);
    });
  });

  group('PairingMiddleware processes PairingCodeRequestedAction correctly', () {
    test(
      'PairingCodeRequestedAction dispatches PairingCodeAvailableAction when the request succeeds',
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
      'PairingCodeRequestedAction dispatches PairingCodeAvailableAction with expiresInSeconds forwarded',
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

    test(
      'PairingCodeRequestedAction dispatches PairingFailedAction when the request fails',
      () async {
        const PairingFailure failure = PairingFailure('unavailable');
        when(
          () => mockRequestPairing(any()),
        ).thenAnswer((_) async => const Left(failure));

        middleware.call(store, const PairingCodeRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog[0], isA<PairingCodeRequestedAction>());
        expect(actionLog[1], const PairingFailedAction('unavailable'));
      },
    );
  });

  group('PairingMiddleware processes PairingCodeRequestedAction correctly', () {
    test(
      'PairingCodeRequestedAction suppresses a late successful request after disposal',
      () async {
        final Completer<Either<Failure, int?>> request =
            Completer<Either<Failure, int?>>();
        when(() => mockRequestPairing(any())).thenAnswer((_) => request.future);
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(store, const PairingCodeRequestedAction(), next);
        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await pumpEventQueue();
        request.complete(const Right(60));
        await pumpEventQueue();

        expect(actionLog, [
          const PairingCodeRequestedAction(),
          const PairingDisposedAction(wasTrusted: false),
        ]);
      },
    );

    test(
      'PairingCodeRequestedAction suppresses a late failure after disposal',
      () async {
        final Completer<Either<Failure, int?>> request =
            Completer<Either<Failure, int?>>();
        when(() => mockRequestPairing(any())).thenAnswer((_) => request.future);
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(store, const PairingCodeRequestedAction(), next);
        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await pumpEventQueue();
        request.complete(const Left(PairingFailure('expired')));
        await pumpEventQueue();

        expect(actionLog, [
          const PairingCodeRequestedAction(),
          const PairingDisposedAction(wasTrusted: false),
        ]);
      },
    );
  });

  group('PairingMiddleware processes PairingCodeSubmittedAction correctly', () {
    test(
      'PairingCodeSubmittedAction suppresses a late success after disposal',
      () async {
        final Completer<Either<Failure, Unit>> confirmation =
            Completer<Either<Failure, Unit>>();
        when(
          () => mockConfirmPairingCode(any()),
        ).thenAnswer((_) => confirmation.future);
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(
          store,
          const PairingCodeSubmittedAction(code: '123456'),
          next,
        );
        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await pumpEventQueue();
        confirmation.complete(const Right(unit));
        await pumpEventQueue();

        expect(actionLog, [
          const PairingCodeSubmittedAction(code: '123456'),
          ConnectionCandidatePairingStartedAction(Fixtures.buildHost().hostId),
          const PairingDisposedAction(wasTrusted: false),
        ]);
        expect(actionLog.whereType<PairingConfirmedAction>(), isEmpty);
        expect(actionLog.whereType<PairingSessionTrustedAction>(), isEmpty);
        expect(
          actionLog.whereType<ConnectionCandidatePairingEndedAction>(),
          isEmpty,
        );
      },
    );

    test(
      'PairingCodeSubmittedAction suppresses a late failure after disposal',
      () async {
        final Completer<Either<Failure, Unit>> confirmation =
            Completer<Either<Failure, Unit>>();
        when(
          () => mockConfirmPairingCode(any()),
        ).thenAnswer((_) => confirmation.future);
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(
          store,
          const PairingCodeSubmittedAction(code: '123456'),
          next,
        );
        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await pumpEventQueue();
        confirmation.complete(const Left(PairingFailure('expired')));
        await pumpEventQueue();

        expect(actionLog, [
          const PairingCodeSubmittedAction(code: '123456'),
          ConnectionCandidatePairingStartedAction(Fixtures.buildHost().hostId),
          const PairingDisposedAction(wasTrusted: false),
        ]);
        expect(actionLog.whereType<PairingFailedAction>(), isEmpty);
        expect(
          actionLog.whereType<ConnectionCandidatePairingEndedAction>(),
          isEmpty,
        );
      },
    );

    test(
      'PairingCodeSubmittedAction ignores an older flow after a new flow starts',
      () async {
        final Completer<Either<Failure, Unit>> oldConfirmation =
            Completer<Either<Failure, Unit>>();
        when(
          () => mockConfirmPairingCode(any()),
        ).thenAnswer((_) => oldConfirmation.future);
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));
        when(() => mockAuthenticate(any())).thenAnswer(
          (_) async => Right(Fixtures.buildPairingHandshake(trusted: true)),
        );

        middleware.call(
          store,
          const PairingCodeSubmittedAction(code: '123456'),
          next,
        );
        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await pumpEventQueue();
        middleware.call(store, const PairingStartedAction(), next);
        oldConfirmation.complete(const Right(unit));
        await pumpEventQueue();

        expect(actionLog.whereType<PairingConfirmedAction>(), isEmpty);
        expect(actionLog.whereType<PairingFailedAction>(), isEmpty);
        expect(
          actionLog.whereType<ConnectionCandidatePairingEndedAction>(),
          isEmpty,
        );
        expect(actionLog.whereType<PairingAuthenticatedAction>(), hasLength(1));
        verify(() => mockAuthenticate(any())).called(1);
      },
    );

    test(
      'PairingCodeSubmittedAction cannot end a newer flow pending selection',
      () async {
        final Host oldCandidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final Host newCandidate = Fixtures.buildHost(
          hostId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        );
        final Completer<Either<Failure, Unit>> oldConfirmation =
            Completer<Either<Failure, Unit>>();
        final Completer<Either<Failure, Unit>> newConfirmation =
            Completer<Either<Failure, Unit>>();
        int confirmationCallCount = 0;
        AppState currentState = _stateWithPhase(
          PairingPhase.awaitingCode,
          host: oldCandidate,
        );
        when(() => store.state).thenAnswer((_) => currentState);
        when(() => mockConfirmPairingCode(any())).thenAnswer(
          (_) => confirmationCallCount++ == 0
              ? oldConfirmation.future
              : newConfirmation.future,
        );
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));
        when(() => mockAuthenticate(any())).thenAnswer(
          (_) async => Right(Fixtures.buildPairingHandshake(trusted: true)),
        );

        middleware.call(
          store,
          const PairingCodeSubmittedAction(code: '111111'),
          next,
        );
        currentState = _stateWithPhase(
          PairingPhase.awaitingCode,
          host: oldCandidate,
          pendingPairingHostId: oldCandidate.hostId,
        );
        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await pumpEventQueue();

        currentState = _stateWithPhase(
          PairingPhase.awaitingCode,
          host: newCandidate,
        );
        middleware.call(store, const PairingStartedAction(), next);
        await pumpEventQueue();
        middleware.call(
          store,
          const PairingCodeSubmittedAction(code: '222222'),
          next,
        );
        currentState = _stateWithPhase(
          PairingPhase.confirming,
          host: newCandidate,
          pendingPairingHostId: newCandidate.hostId,
        );

        oldConfirmation.complete(const Left(PairingFailure('old failure')));
        await pumpEventQueue();
        newConfirmation.complete(const Right(unit));
        await pumpEventQueue();

        expect(actionLog.whereType<ConnectionCandidatePairingEndedAction>(), [
          ConnectionCandidatePairingEndedAction(oldCandidate.hostId),
        ]);
        expect(
          ConnectionSelectors.pendingPairingHostIdSelector(currentState),
          newCandidate.hostId,
        );
        expect(actionLog.whereType<PairingFailedAction>(), isEmpty);
        expect(actionLog.whereType<PairingConfirmedAction>(), hasLength(1));
      },
    );
  });

  group(
    'PairingMiddleware processes PairingRenotifyRequestedAction correctly',
    () {
      test(
        'PairingRenotifyRequestedAction suppresses late results after disposal',
        () async {
          final Completer<Either<Failure, int?>> successfulRenotify =
              Completer<Either<Failure, int?>>();
          final Completer<Either<Failure, int?>> cooledDownRenotify =
              Completer<Either<Failure, int?>>();
          final Completer<Either<Failure, int?>> failedRenotify =
              Completer<Either<Failure, int?>>();
          final MockRequestPairingRenotifyUseCase mockRenotify =
              sl<RequestPairingRenotifyUseCase>()
                  as MockRequestPairingRenotifyUseCase;
          int renotifyCallCount = 0;
          when(() => mockRenotify(any())).thenAnswer(
            (_) => switch (renotifyCallCount++) {
              0 => successfulRenotify.future,
              1 => cooledDownRenotify.future,
              _ => failedRenotify.future,
            },
          );
          when(
            () => mockDisconnect(any()),
          ).thenAnswer((_) async => const Right(unit));

          middleware.call(store, const PairingRenotifyRequestedAction(), next);
          middleware.call(store, const PairingRenotifyRequestedAction(), next);
          middleware.call(store, const PairingRenotifyRequestedAction(), next);
          middleware.call(
            store,
            const PairingDisposedAction(wasTrusted: false),
            next,
          );
          await pumpEventQueue();
          successfulRenotify.complete(const Right(null));
          cooledDownRenotify.complete(const Right(5));
          failedRenotify.complete(const Left(PairingFailure('no challenge')));
          await pumpEventQueue();

          expect(actionLog, [
            const PairingRenotifyRequestedAction(),
            const PairingRenotifyRequestedAction(),
            const PairingRenotifyRequestedAction(),
            const PairingDisposedAction(wasTrusted: false),
          ]);
          expect(actionLog.whereType<PairingRenotifyCooldownAction>(), isEmpty);
          expect(
            actionLog.whereType<PairingRenotifySucceededAction>(),
            isEmpty,
          );
          expect(actionLog.whereType<PairingFailedAction>(), isEmpty);
        },
      );
    },
  );

  group('PairingMiddleware processes PairingCancelRequestedAction correctly', () {
    test(
      'PairingCancelRequestedAction suppresses late success and failure after disposal',
      () async {
        final Completer<Either<Failure, Unit>> successfulCancellation =
            Completer<Either<Failure, Unit>>();
        final Completer<Either<Failure, Unit>> failedCancellation =
            Completer<Either<Failure, Unit>>();
        final MockCancelPairingUseCase mockCancellation =
            sl<CancelPairingUseCase>() as MockCancelPairingUseCase;
        int cancellationCallCount = 0;
        when(() => mockCancellation(any())).thenAnswer(
          (_) => cancellationCallCount++ == 0
              ? successfulCancellation.future
              : failedCancellation.future,
        );
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(store, const PairingCancelRequestedAction(), next);
        middleware.call(store, const PairingCancelRequestedAction(), next);
        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await pumpEventQueue();
        successfulCancellation.complete(const Right(unit));
        failedCancellation.complete(
          const Left(PairingFailure('cancel failed')),
        );
        await pumpEventQueue();

        expect(actionLog, [
          const PairingCancelRequestedAction(),
          const PairingCancelRequestedAction(),
          const PairingDisposedAction(wasTrusted: false),
        ]);
        expect(actionLog.whereType<PairingCancelSucceededAction>(), isEmpty);
        expect(actionLog.whereType<PairingFailedAction>(), isEmpty);
      },
    );
  });

  group('PairingMiddleware processes PairingCodeSubmittedAction correctly', () {
    test(
      'PairingCodeSubmittedAction dispatches PairingConfirmedAction and forwards code/displayName when confirmation succeeds',
      () async {
        when(
          () => mockConfirmPairingCode(
            const ConfirmPairingCodeParams(
              code: '123456',
              displayName: 'Desktop',
            ),
          ),
        ).thenAnswer((_) async => const Right(unit));
        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => const Stream<PairingConnectionStatus>.empty());

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
          ConnectionCandidatePairingStartedAction(Fixtures.buildHost().hostId),
          const PairingConfirmedAction(),
          const PairingSessionTrustedAction(),
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

    test(
      'PairingCodeSubmittedAction preserves an existing Known Host selection on success',
      () async {
        final Host knownHost = Fixtures.buildHost();
        when(() => store.state).thenReturn(
          _stateWithPhase(
            PairingPhase.confirming,
            host: knownHost,
            source: ConnectionHostSelectionSource.knownHost,
          ),
        );
        when(
          () => mockConfirmPairingCode(
            const ConfirmPairingCodeParams(code: '123456'),
          ),
        ).thenAnswer((_) async => const Right(unit));
        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => const Stream<PairingConnectionStatus>.empty());

        middleware.call(
          store,
          const PairingCodeSubmittedAction(code: '123456'),
          next,
        );
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          const PairingCodeSubmittedAction(code: '123456'),
          const PairingConfirmedAction(),
          const PairingSessionTrustedAction(),
        ]);
        verifyNever(
          () => store.dispatch(any(that: isA<ConnectionHostSelectedAction>())),
        );
      },
    );

    test(
      'PairingCodeSubmittedAction dispatches PairingFailedAction when confirmation fails',
      () async {
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
          ConnectionCandidatePairingStartedAction(Fixtures.buildHost().hostId),
          ConnectionCandidatePairingEndedAction(Fixtures.buildHost().hostId),
          const PairingFailedAction('invalid'),
        ]);
      },
    );

    test(
      'PairingCodeSubmittedAction dispatches PairingConfirmFailedWithAttemptsRemainingAction, '
      'not PairingFailedAction, when the failure is retriable',
      () async {
        const PairingRetriableFailure failure = PairingRetriableFailure(
          "That code isn't correct. Check Skyrim and try again.",
        );
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
          ConnectionCandidatePairingStartedAction(Fixtures.buildHost().hostId),
          const PairingConfirmFailedWithAttemptsRemainingAction(
            message: "That code isn't correct. Check Skyrim and try again.",
          ),
        ]);
      },
    );
  });

  group('PairingMiddleware processes PairingDisposedAction correctly', () {
    test(
      'PairingDisposedAction calls DisconnectUseCase as a best-effort cleanup when not yet trusted',
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
      'PairingDisposedAction does not call DisconnectUseCase when pairing had already succeeded',
      () async {
        final Host knownHost = Fixtures.buildHost();
        when(() => store.state).thenReturn(
          _stateWithPhase(
            PairingPhase.trusted,
            host: knownHost,
            source: ConnectionHostSelectionSource.knownHost,
          ),
        );

        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: true),
          next,
        );
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [const PairingDisposedAction(wasTrusted: true)]);
        expect(
          actionLog.whereType<ConnectionCandidatePairingEndedAction>(),
          isEmpty,
        );
        verifyNever(() => mockDisconnect(any()));
      },
    );

    test(
      'PairingDisposedAction releases an untrusted pending candidate before disconnecting',
      () async {
        final Host candidate = Fixtures.buildHost();
        when(() => store.state).thenReturn(
          _stateWithPhase(
            PairingPhase.awaitingCode,
            host: candidate,
            pendingPairingHostId: candidate.hostId,
          ),
        );
        when(
          () => mockDisconnect(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: false),
          next,
        );
        await pumpEventQueue();

        expect(actionLog, [
          const PairingDisposedAction(wasTrusted: false),
          ConnectionCandidatePairingEndedAction(candidate.hostId),
        ]);
        verify(() => mockDisconnect(any())).called(1);
      },
    );

    test(
      'PairingDisposedAction does not cancel a running connection-status observation',
      () async {
        final StreamController<PairingConnectionStatus> controller =
            StreamController<PairingConnectionStatus>.broadcast();
        addTearDown(controller.close);
        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => controller.stream);
        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();

        middleware.call(
          store,
          const PairingDisposedAction(wasTrusted: true),
          next,
        );
        await pumpEventQueue();

        controller.add(PairingConnectionStatus.invalidated);
        await pumpEventQueue();

        expect(actionLog, [
          const PairingSessionTrustedAction(),
          const PairingDisposedAction(wasTrusted: true),
          PairingFailedAction(SessionInvalidatedFailure.administrative.message),
        ]);
      },
    );
  });

  group('PairingMiddleware processes PairingRenotifyRequestedAction correctly', () {
    test(
      'PairingRenotifyRequestedAction dispatches the Host retry cooldown after successful redisplay',
      () async {
        final mockRenotifyUseCase =
            sl<RequestPairingRenotifyUseCase>()
                as MockRequestPairingRenotifyUseCase;
        when(
          () => mockRenotifyUseCase(any()),
        ).thenAnswer((_) async => const Right(5));

        middleware.call(store, const PairingRenotifyRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog, [
          isA<PairingRenotifyRequestedAction>(),
          isA<PairingRenotifyCooldownAction>(),
        ]);
        expect(
          (actionLog[1] as PairingRenotifyCooldownAction).retryAfterSeconds,
          5,
        );
        verify(() => mockRenotifyUseCase(any())).called(1);
      },
    );

    test(
      'PairingRenotifyRequestedAction dispatches PairingRenotifyCooldownAction with seconds when renotify is in cooldown',
      () async {
        final mockRenotifyUseCase =
            sl<RequestPairingRenotifyUseCase>()
                as MockRequestPairingRenotifyUseCase;
        when(
          () => mockRenotifyUseCase(any()),
        ).thenAnswer((_) async => const Right(3));

        middleware.call(store, const PairingRenotifyRequestedAction(), next);
        await Future<void>.delayed(Duration.zero);

        expect(actionLog.length, 2);
        expect(actionLog[0], isA<PairingRenotifyRequestedAction>());
        expect(actionLog[1], isA<PairingRenotifyCooldownAction>());
        expect(
          (actionLog[1] as PairingRenotifyCooldownAction).retryAfterSeconds,
          3,
        );
        verify(() => mockRenotifyUseCase(any())).called(1);
      },
    );

    test(
      'PairingRenotifyRequestedAction dispatches PairingRenotifyCooldownAction with zero when renotify cooldown is immediate',
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

    test(
      'PairingRenotifyRequestedAction dispatches PairingFailedAction when renotify fails',
      () async {
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
      },
    );
  });

  group('PairingMiddleware processes PairingCancelRequestedAction correctly', () {
    test(
      'PairingCancelRequestedAction dispatches PairingCancelSucceededAction when cancel succeeds',
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

    test(
      'PairingCancelRequestedAction dispatches PairingFailedAction when cancel fails',
      () async {
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
        expect(
          actionLog.whereType<ConnectionCandidatePairingEndedAction>(),
          isEmpty,
        );
      },
    );

    test(
      'PairingCancelRequestedAction releases the pending candidate when cancellation succeeds',
      () async {
        final Host candidate = Fixtures.buildHost();
        when(() => store.state).thenReturn(
          _stateWithPhase(
            PairingPhase.awaitingCode,
            host: candidate,
            pendingPairingHostId: candidate.hostId,
          ),
        );
        final mockCancelUseCase =
            sl<CancelPairingUseCase>() as MockCancelPairingUseCase;
        when(
          () => mockCancelUseCase(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(store, const PairingCancelRequestedAction(), next);
        await pumpEventQueue();

        expect(actionLog, [
          const PairingCancelRequestedAction(),
          ConnectionCandidatePairingEndedAction(candidate.hostId),
          const PairingCancelSucceededAction(),
        ]);
      },
    );
  });

  group('PairingMiddleware processes PairingSessionTrustedAction correctly', () {
    test(
      'PairingSessionTrustedAction dispatches PairingDisconnectedAction when the observation '
      'stream emits lost',
      () async {
        when(() => mockObserveConnectionStatus(any())).thenAnswer(
          (_) => Stream<PairingConnectionStatus>.value(
            PairingConnectionStatus.lost,
          ),
        );

        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();

        expect(actionLog, [
          const PairingSessionTrustedAction(),
          const PairingDisconnectedAction(),
        ]);
      },
    );

    test(
      'PairingSessionTrustedAction dispatches PairingConnectionRestoredAction when the '
      'observation stream emits restored',
      () async {
        when(() => mockObserveConnectionStatus(any())).thenAnswer(
          (_) => Stream<PairingConnectionStatus>.value(
            PairingConnectionStatus.restored,
          ),
        );

        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();

        expect(actionLog, [
          const PairingSessionTrustedAction(),
          const PairingConnectionRestoredAction(),
        ]);
      },
    );

    test(
      'PairingSessionTrustedAction dispatches PairingFailedAction with the canonical '
      'administrative message when the observation stream emits invalidated',
      () async {
        when(() => mockObserveConnectionStatus(any())).thenAnswer(
          (_) => Stream<PairingConnectionStatus>.value(
            PairingConnectionStatus.invalidated,
          ),
        );

        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();

        expect(actionLog, [
          const PairingSessionTrustedAction(),
          PairingFailedAction(SessionInvalidatedFailure.administrative.message),
        ]);
      },
    );

    test(
      'PairingSessionTrustedAction dispatches one action for each event the observation stream '
      'emits, in order',
      () async {
        when(() => mockObserveConnectionStatus(any())).thenAnswer(
          (_) => Stream<PairingConnectionStatus>.fromIterable([
            PairingConnectionStatus.lost,
            PairingConnectionStatus.restored,
          ]),
        );

        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();

        expect(actionLog, [
          const PairingSessionTrustedAction(),
          const PairingDisconnectedAction(),
          const PairingConnectionRestoredAction(),
        ]);
      },
    );

    test(
      'PairingSessionTrustedAction does not start a second observation when dispatched again',
      () async {
        final StreamController<PairingConnectionStatus> controller =
            StreamController<PairingConnectionStatus>.broadcast();
        addTearDown(controller.close);
        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => controller.stream);

        middleware.call(store, const PairingSessionTrustedAction(), next);
        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();

        verify(() => mockObserveConnectionStatus(any())).called(1);

        // Proves the guard actually prevents a second listener attaching to the stream, not just
        // a second use-case constructor call: exactly one PairingFailedAction reaches actionLog
        // per event, never two.
        controller.add(PairingConnectionStatus.invalidated);
        await pumpEventQueue();

        expect(actionLog.whereType<PairingFailedAction>(), [
          PairingFailedAction(SessionInvalidatedFailure.administrative.message),
        ]);
      },
    );

    test(
      'PairingSessionTrustedAction does not dispatch for an event on the observation stream '
      'after invalidated was already received',
      () async {
        final StreamController<PairingConnectionStatus> controller =
            StreamController<PairingConnectionStatus>.broadcast();
        addTearDown(controller.close);
        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => controller.stream);

        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();

        controller.add(PairingConnectionStatus.invalidated);
        await pumpEventQueue();
        controller.add(PairingConnectionStatus.restored);
        await pumpEventQueue();

        expect(actionLog, [
          const PairingSessionTrustedAction(),
          PairingFailedAction(SessionInvalidatedFailure.administrative.message),
        ]);
      },
    );

    test(
      'PairingSessionTrustedAction starts a new observation when dispatched again after the '
      'previous one was invalidated',
      () async {
        final StreamController<PairingConnectionStatus> firstController =
            StreamController<PairingConnectionStatus>.broadcast();
        addTearDown(firstController.close);
        final StreamController<PairingConnectionStatus> secondController =
            StreamController<PairingConnectionStatus>.broadcast();
        addTearDown(secondController.close);
        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => firstController.stream);

        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();
        firstController.add(PairingConnectionStatus.invalidated);
        await pumpEventQueue();

        when(
          () => mockObserveConnectionStatus(any()),
        ).thenAnswer((_) => secondController.stream);
        middleware.call(store, const PairingSessionTrustedAction(), next);
        await pumpEventQueue();
        secondController.add(PairingConnectionStatus.restored);
        await pumpEventQueue();

        verify(() => mockObserveConnectionStatus(any())).called(2);
        expect(actionLog, [
          const PairingSessionTrustedAction(),
          PairingFailedAction(SessionInvalidatedFailure.administrative.message),
          const PairingSessionTrustedAction(),
          const PairingConnectionRestoredAction(),
        ]);
      },
    );
  });
}
