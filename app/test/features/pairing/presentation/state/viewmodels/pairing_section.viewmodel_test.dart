import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_section.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../../../../../fixtures/fixtures.dart';

/// Mocks the Redux store the ViewModel reads and dispatches through.
class MockStore extends Mock implements Store<AppState> {}

/// Builds an [AppState] with the given pairing [phase], [error] and selected [host].
AppState buildState({
  PairingPhase phase = PairingPhase.none,
  String? error,
  Host? host,
}) => AppState(
  connection: ConnectionState(selectedHost: host),
  pairing: PairingState(
    phase: phase,
    hostVersion: null,
    error: error,
    codeExpiresAt: null,
    renotifyAvailableAt: null,
  ),
);

/// Exercises [PairingSectionViewModel.fromStore] projections and callbacks.
void main() {
  late MockStore store;

  setUpAll(() {
    registerFallbackValue(const PairingStartedAction());
  });

  setUp(() {
    store = MockStore();
    when(() => store.dispatch(any())).thenAnswer((_) {});
  });

  group('Method fromStore behaves correctly', () {
    test('Method fromStore projects the phase and error from state', () {
      when(() => store.state).thenReturn(
        buildState(phase: PairingPhase.failed, error: 'Code expired.'),
      );

      final PairingSectionViewModel viewModel =
          PairingSectionViewModel.fromStore(store);

      expect(viewModel.phase, PairingPhase.failed);
      expect(viewModel.error, 'Code expired.');
    });

    test('Method fromStore projects the selected Host name', () {
      when(() => store.state).thenReturn(
        buildState(host: Fixtures.buildHost(displayName: 'Bedroom PC')),
      );

      final PairingSectionViewModel viewModel =
          PairingSectionViewModel.fromStore(store);

      expect(viewModel.hostName, 'Bedroom PC');
    });

    test(
      'Method fromStore falls back to a generic name with no Host selected',
      () {
        when(() => store.state).thenReturn(buildState());

        final PairingSectionViewModel viewModel =
            PairingSectionViewModel.fromStore(store);

        expect(viewModel.hostName, PairingSectionViewModel.unknownHostName);
        expect(viewModel.hostName, 'this PC');
      },
    );

    test(
      'Method fromStore lets the section be dismissed except while confirming',
      () {
        for (final PairingPhase phase in PairingPhase.values) {
          when(() => store.state).thenReturn(buildState(phase: phase));

          final PairingSectionViewModel viewModel =
              PairingSectionViewModel.fromStore(store);

          expect(viewModel.canDismiss, phase != PairingPhase.confirming);
        }
      },
    );

    test('Method fromStore dispatches nothing until a callback runs', () {
      when(() => store.state).thenReturn(buildState());

      PairingSectionViewModel.fromStore(store);

      verifyNever(() => store.dispatch(any()));
    });
  });

  group('Property onStart behaves correctly', () {
    test('Property onStart dispatches PairingStartedAction', () {
      when(() => store.state).thenReturn(buildState());

      PairingSectionViewModel.fromStore(store).onStart();

      verify(() => store.dispatch(const PairingStartedAction())).called(1);
    });
  });

  group('Property onRequestCode behaves correctly', () {
    test('Property onRequestCode dispatches PairingCodeRequestedAction', () {
      when(() => store.state).thenReturn(buildState());

      PairingSectionViewModel.fromStore(store).onRequestCode();

      verify(
        () => store.dispatch(const PairingCodeRequestedAction()),
      ).called(1);
    });
  });

  group('Property onSubmitCode behaves correctly', () {
    test(
      'Property onSubmitCode dispatches PairingCodeSubmittedAction with the code and display name',
      () {
        when(() => store.state).thenReturn(buildState());

        PairingSectionViewModel.fromStore(
          store,
        ).onSubmitCode('123456', 'Desktop');

        verify(
          () => store.dispatch(
            const PairingCodeSubmittedAction(
              code: '123456',
              displayName: 'Desktop',
            ),
          ),
        ).called(1);
      },
    );
  });

  group('Property onDispose behaves correctly', () {
    test(
      'Property onDispose dispatches an untrusted disposal outside trusted',
      () {
        when(() => store.state).thenReturn(buildState());
        final PairingSectionViewModel viewModel =
            PairingSectionViewModel.fromStore(store);

        viewModel.onDispose();

        verify(
          () => store.dispatch(const PairingDisposedAction(wasTrusted: false)),
        ).called(1);
      },
    );

    test(
      'Property onDispose reads trust from the store when the callback runs',
      () {
        when(() => store.state).thenReturn(buildState());
        final PairingSectionViewModel viewModel =
            PairingSectionViewModel.fromStore(store);
        when(
          () => store.state,
        ).thenReturn(buildState(phase: PairingPhase.trusted));

        viewModel.onDispose();

        verify(
          () => store.dispatch(const PairingDisposedAction(wasTrusted: true)),
        ).called(1);
        verifyNever(
          () => store.dispatch(const PairingDisposedAction(wasTrusted: false)),
        );
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('PairingSectionViewModel differs when a displayed value differs', () {
      when(() => store.state).thenReturn(buildState());
      final PairingSectionViewModel first = PairingSectionViewModel.fromStore(
        store,
      );
      when(
        () => store.state,
      ).thenReturn(buildState(phase: PairingPhase.confirming));
      final PairingSectionViewModel second = PairingSectionViewModel.fromStore(
        store,
      );

      expect(first, isNot(second));
    });

    test(
      'PairingSectionViewModel equals another with the same displayed values',
      () {
        when(() => store.state).thenReturn(buildState());

        final PairingSectionViewModel first = PairingSectionViewModel.fromStore(
          store,
        );
        final PairingSectionViewModel second =
            PairingSectionViewModel.fromStore(store);

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      },
    );
  });
}
