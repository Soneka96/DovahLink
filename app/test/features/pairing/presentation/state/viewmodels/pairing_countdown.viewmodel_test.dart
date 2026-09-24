import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_countdown.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mocks the Redux store used to create a pairing countdown ViewModel.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [PairingCountdownViewModel.fromStore] projections and equality.
void main() {
  late MockStore store;

  setUp(() {
    store = MockStore();
  });

  group('Method fromStore behaves correctly', () {
    test('Method fromStore returns null when no code is active', () {
      when(() => store.state).thenReturn(AppState.initial());

      final PairingCountdownViewModel viewModel =
          PairingCountdownViewModel.fromStore(store);

      expect(viewModel.remainingSeconds, isNull);
    });

    test('Method fromStore maps the active code expiry', () {
      when(() => store.state).thenReturn(
        AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            hostVersion: null,
            error: null,
            codeExpiresAt: DateTime.now().add(const Duration(minutes: 10)),
            renotifyAvailableAt: null,
          ),
        ),
      );

      final PairingCountdownViewModel viewModel =
          PairingCountdownViewModel.fromStore(store);

      expect(viewModel.remainingSeconds, isA<int>());
      expect(viewModel.remainingSeconds, greaterThan(0));
      expect(viewModel.remainingSeconds, lessThan(600));
    });

    test('Method fromStore clamps an expired countdown to zero', () {
      when(() => store.state).thenReturn(
        AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            hostVersion: null,
            error: null,
            codeExpiresAt: DateTime.now().subtract(const Duration(seconds: 10)),
            renotifyAvailableAt: null,
          ),
        ),
      );

      final PairingCountdownViewModel viewModel =
          PairingCountdownViewModel.fromStore(store);

      expect(viewModel.remainingSeconds, 0);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds when remaining seconds match', () {
      final int remainingSeconds = DateTime.now().minute;
      final PairingCountdownViewModel first = PairingCountdownViewModel(
        remainingSeconds: remainingSeconds,
      );
      final PairingCountdownViewModel second = PairingCountdownViewModel(
        remainingSeconds: remainingSeconds,
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality differs when remaining seconds change', () {
      const PairingCountdownViewModel first = PairingCountdownViewModel(
        remainingSeconds: 60,
      );
      const PairingCountdownViewModel second = PairingCountdownViewModel(
        remainingSeconds: 59,
      );

      expect(first, isNot(second));
    });

    test('Behavior equality distinguishes no active countdown from zero', () {
      const PairingCountdownViewModel inactive = PairingCountdownViewModel(
        remainingSeconds: null,
      );
      const PairingCountdownViewModel expired = PairingCountdownViewModel(
        remainingSeconds: 0,
      );

      expect(inactive, isNot(expired));
    });
  });
}
