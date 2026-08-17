import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/confirm_pairing_code.params.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

import '../../fixtures/pairing.fixture.dart';

/// Mocks for the use cases [PairingMiddleware] resolves through [sl].
class MockAuthenticateUseCase extends Mock implements AuthenticateUseCase {}

class MockRequestPairingUseCase extends Mock implements RequestPairingUseCase {}

class MockConfirmPairingCodeUseCase extends Mock
    implements ConfirmPairingCodeUseCase {}

class MockDisconnectUseCase extends Mock implements DisconnectUseCase {}

/// Exercises [PairingMiddleware]'s dispatched-action forwarding, using a real
/// store with the mocked use cases registered behind [sl].
void main() {
  late MockAuthenticateUseCase mockAuthenticate;
  late MockRequestPairingUseCase mockRequestPairing;
  late MockConfirmPairingCodeUseCase mockConfirmPairingCode;
  late MockDisconnectUseCase mockDisconnect;
  late Store<AppState> store;

  setUpAll(() {
    registerFallbackValue(NoParams());
  });

  setUp(() async {
    await sl.reset();
    mockAuthenticate = MockAuthenticateUseCase();
    mockRequestPairing = MockRequestPairingUseCase();
    mockConfirmPairingCode = MockConfirmPairingCodeUseCase();
    mockDisconnect = MockDisconnectUseCase();
    sl.registerLazySingleton<AuthenticateUseCase>(() => mockAuthenticate);
    sl.registerLazySingleton<RequestPairingUseCase>(() => mockRequestPairing);
    sl.registerLazySingleton<ConfirmPairingCodeUseCase>(
      () => mockConfirmPairingCode,
    );
    sl.registerLazySingleton<DisconnectUseCase>(() => mockDisconnect);
    store = const CreateStore()(middleware: [PairingMiddleware().call]);
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

        store.dispatch(const PairingStartedAction());
        await Future<void>.delayed(Duration.zero);

        expect(store.state.pairing.phase, PairingPhase.unpaired);
        expect(store.state.pairing.bridgeVersion, handshake.bridgeVersion);
        verify(() => mockAuthenticate(any())).called(1);
      },
    );

    test(
      'dispatches PairingDisconnectedAction when authentication fails with a NetworkFailure',
      () async {
        const NetworkFailure failure = NetworkFailure('unreachable');
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) async => const Left(failure));

        store.dispatch(const PairingStartedAction());
        await Future<void>.delayed(Duration.zero);

        expect(store.state.pairing.phase, PairingPhase.disconnected);
        expect(store.state.pairing.error, isNull);
      },
    );

    test(
      'dispatches PairingFailedAction when authentication fails with a non-network failure',
      () async {
        const PairingFailure failure = PairingFailure('rejected');
        when(
          () => mockAuthenticate(any()),
        ).thenAnswer((_) async => const Left(failure));

        store.dispatch(const PairingStartedAction());
        await Future<void>.delayed(Duration.zero);

        expect(store.state.pairing.phase, PairingPhase.failed);
        expect(store.state.pairing.error, 'rejected');
      },
    );

    test(
      'retries PairingStartedAction after reconnectDelay when still disconnected',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final Store<AppState> retryStore = const CreateStore()(
            middleware: [PairingMiddleware(reconnectDelay: delay).call],
          );
          const NetworkFailure failure = NetworkFailure('unreachable');
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) async => const Left(failure));

          retryStore.dispatch(const PairingStartedAction());
          async.flushMicrotasks();
          expect(retryStore.state.pairing.phase, PairingPhase.disconnected);

          async.elapse(delay);
          async.flushMicrotasks();

          expect(retryStore.state.pairing.phase, PairingPhase.disconnected);
          verify(() => mockAuthenticate(any())).called(2);
        });
      },
    );

    test(
      'does not retry once PairingDisposedAction fires before reconnectDelay elapses',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final Store<AppState> retryStore = const CreateStore()(
            middleware: [PairingMiddleware(reconnectDelay: delay).call],
          );
          const NetworkFailure failure = NetworkFailure('unreachable');
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) async => const Left(failure));
          when(
            () => mockDisconnect(any()),
          ).thenAnswer((_) async => const Right(unit));

          retryStore.dispatch(const PairingStartedAction());
          async.flushMicrotasks();
          expect(retryStore.state.pairing.phase, PairingPhase.disconnected);

          // Disposes well before delay elapses; the scheduled retry's own
          // phase guard must suppress it once virtual time catches up.
          retryStore.dispatch(const PairingDisposedAction());
          async.elapse(delay);
          async.flushMicrotasks();

          expect(retryStore.state.pairing.phase, PairingPhase.none);
          verify(() => mockAuthenticate(any())).called(1);
        });
      },
    );

    test(
      'does not retry once a real reconnect moves the phase off disconnected before reconnectDelay elapses',
      () {
        fakeAsync((FakeAsync async) {
          const Duration delay = Duration(seconds: 3);
          final Store<AppState> retryStore = const CreateStore()(
            middleware: [PairingMiddleware(reconnectDelay: delay).call],
          );
          const NetworkFailure failure = NetworkFailure('unreachable');
          when(
            () => mockAuthenticate(any()),
          ).thenAnswer((_) async => const Left(failure));

          retryStore.dispatch(const PairingStartedAction());
          async.flushMicrotasks();
          expect(retryStore.state.pairing.phase, PairingPhase.disconnected);

          // A real reconnect (e.g. a manual retry landing before the
          // scheduled one) moves the phase off disconnected well before
          // delay elapses; the scheduled retry must not stomp back over it.
          retryStore.dispatch(
            const PairingAuthenticatedAction(
              bridgeVersion: '1.2.3',
              trusted: false,
            ),
          );
          async.elapse(delay);
          async.flushMicrotasks();

          expect(retryStore.state.pairing.phase, PairingPhase.unpaired);
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
        ).thenAnswer((_) async => const Right(unit));

        store.dispatch(const PairingCodeRequestedAction());
        await Future<void>.delayed(Duration.zero);

        expect(store.state.pairing.phase, PairingPhase.awaitingCode);
      },
    );

    test('dispatches PairingFailedAction when the request fails', () async {
      const PairingFailure failure = PairingFailure('unavailable');
      when(
        () => mockRequestPairing(any()),
      ).thenAnswer((_) async => const Left(failure));

      store.dispatch(const PairingCodeRequestedAction());
      await Future<void>.delayed(Duration.zero);

      expect(store.state.pairing.phase, PairingPhase.failed);
      expect(store.state.pairing.error, 'unavailable');
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

        store.dispatch(
          const PairingCodeSubmittedAction(
            code: '123456',
            displayName: 'Desktop',
          ),
        );
        await Future<void>.delayed(Duration.zero);

        expect(store.state.pairing.phase, PairingPhase.trusted);
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

      store.dispatch(const PairingCodeSubmittedAction(code: '000000'));
      await Future<void>.delayed(Duration.zero);

      expect(store.state.pairing.phase, PairingPhase.failed);
      expect(store.state.pairing.error, 'invalid');
    });
  });

  group('PairingMiddleware — PairingDisposedAction', () {
    test('calls DisconnectUseCase as a best-effort cleanup', () async {
      when(
        () => mockDisconnect(any()),
      ).thenAnswer((_) async => const Right(unit));

      store.dispatch(const PairingDisposedAction());
      await Future<void>.delayed(Duration.zero);

      verify(() => mockDisconnect(any())).called(1);
      expect(store.state.pairing.phase, PairingPhase.none);
    });
  });
}
