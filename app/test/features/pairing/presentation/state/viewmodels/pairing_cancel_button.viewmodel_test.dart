import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mocks the Redux store used to create a pairing cancellation ViewModel.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [PairingCancelButtonViewModel.fromStore] projections and callbacks.
void main() {
  late MockStore store;

  setUpAll(() {
    registerFallbackValue(const PairingCancelRequestedAction());
  });

  setUp(() {
    store = MockStore();
  });

  group('Method fromStore behaves correctly', () {
    test('Method fromStore enables cancellation only during code entry', () {
      for (final PairingPhase phase in PairingPhase.values) {
        when(() => store.state).thenReturn(
          AppState(
            connection: ConnectionState.initial(),
            pairing: PairingState(
              phase: phase,
              hostVersion: null,
              error: null,
              codeExpiresAt: null,
              renotifyAvailableAt: null,
            ),
          ),
        );

        final PairingCancelButtonViewModel viewModel =
            PairingCancelButtonViewModel.fromStore(store);
        final bool expectedEnabled = phase == PairingPhase.awaitingCode;

        expect(viewModel.isEnabled, expectedEnabled);
        expect(viewModel.onPressed != null, expectedEnabled);
      }

      verifyNever(() => store.dispatch(any()));
    });

    test(
      'Method fromStore creates a callback that dispatches cancellation',
      () {
        when(() => store.state).thenReturn(
          AppState(
            connection: ConnectionState.initial(),
            pairing: const PairingState(
              phase: PairingPhase.awaitingCode,
              hostVersion: null,
              error: null,
              codeExpiresAt: null,
              renotifyAvailableAt: null,
            ),
          ),
        );
        when(() => store.dispatch(any())).thenAnswer((_) {});

        final PairingCancelButtonViewModel viewModel =
            PairingCancelButtonViewModel.fromStore(store);
        viewModel.onPressed!();

        verify(
          () => store.dispatch(const PairingCancelRequestedAction()),
        ).called(1);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test(
      'Behavior equality ignores callback identity when enabled state matches',
      () {
        final PairingCancelButtonViewModel first = PairingCancelButtonViewModel(
          isEnabled: true,
          onPressed: () {},
        );
        final PairingCancelButtonViewModel second =
            PairingCancelButtonViewModel(isEnabled: true, onPressed: () {});

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      },
    );

    test('Behavior equality differs when enabled state differs', () {
      final PairingCancelButtonViewModel enabled = PairingCancelButtonViewModel(
        isEnabled: true,
        onPressed: () {},
      );
      const PairingCancelButtonViewModel disabled =
          PairingCancelButtonViewModel(isEnabled: false, onPressed: null);

      expect(enabled, isNot(disabled));
    });
  });
}
