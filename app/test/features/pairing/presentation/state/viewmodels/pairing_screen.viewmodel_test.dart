import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_screen.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_reducer.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Exercises [PairingScreenViewModel.fromStore] projections.
void main() {
  group('PairingScreenViewModel fromStore()', () {
    test('fromStore constructs an initial ViewModel correctly', () {
      final PairingScreenViewModel viewModel =
          PairingScreenViewModel.fromStore(const CreateStore()());

      expect(viewModel.phase, PairingPhase.none);
      expect(viewModel.statusLabel, 'Unknown');
      expect(viewModel.bridgeVersion, isNull);
      expect(viewModel.error, isNull);
    });

    test('fromStore constructs an authenticated ViewModel correctly', () {
      final Store<AppState> store = const CreateStore()();
      store.dispatch(
        const PairingAuthenticatedAction(
          bridgeVersion: '1.2.3',
          trusted: false,
        ),
      );

      final PairingScreenViewModel viewModel =
          PairingScreenViewModel.fromStore(store);

      expect(viewModel.phase, PairingPhase.unpaired);
      expect(viewModel.statusLabel, 'Not paired');
      expect(viewModel.bridgeVersion, '1.2.3');
      expect(viewModel.error, isNull);
    });

    test('fromStore constructs a failed ViewModel correctly', () {
      final Store<AppState> store = const CreateStore()();
      store.dispatch(const PairingFailedAction('That code isn\'t correct.'));

      final PairingScreenViewModel viewModel =
          PairingScreenViewModel.fromStore(store);

      expect(viewModel.phase, PairingPhase.failed);
      expect(viewModel.error, "That code isn't correct.");
    });

    test('fromStore labels every lifecycle phase correctly', () {
      final List<MapEntry<PairingPhase, String>> cases = [
        const MapEntry(PairingPhase.none, 'Unknown'),
        const MapEntry(PairingPhase.connecting, 'Connecting'),
        const MapEntry(PairingPhase.unpaired, 'Not paired'),
        const MapEntry(PairingPhase.requestingCode, 'Requesting code'),
        const MapEntry(PairingPhase.awaitingCode, 'Awaiting code'),
        const MapEntry(PairingPhase.confirming, 'Confirming'),
        const MapEntry(PairingPhase.trusted, 'Paired'),
        const MapEntry(PairingPhase.failed, 'Failed'),
      ];

      for (final MapEntry<PairingPhase, String> entry in cases) {
        final Store<AppState> store = Store<AppState>(
          appReducer,
          initialState: AppState(
            connection: ConnectionState.initial(),
            pairing: PairingState(
              phase: entry.key,
              bridgeVersion: null,
              error: null,
              codeExpiresAt: null,
              renotifyAvailableAt: null,
            ),
          ),
          distinct: true,
        );

        final PairingScreenViewModel viewModel =
            PairingScreenViewModel.fromStore(store);

        expect(viewModel.statusLabel, entry.value);
      }
    });

    test('onStart dispatches PairingStartedAction', () {
      final Store<AppState> store = const CreateStore()();
      final PairingScreenViewModel viewModel =
          PairingScreenViewModel.fromStore(store);

      viewModel.onStart();

      expect(store.state.pairing.phase, PairingPhase.connecting);
    });

    test('onRequestCode dispatches PairingCodeRequestedAction', () {
      final Store<AppState> store = const CreateStore()();
      final PairingScreenViewModel viewModel =
          PairingScreenViewModel.fromStore(store);

      viewModel.onRequestCode();

      expect(store.state.pairing.phase, PairingPhase.requestingCode);
    });

    test(
      'onSubmitCode dispatches PairingCodeSubmittedAction with the code and displayName',
      () {
        final Store<AppState> store = const CreateStore()();
        final PairingScreenViewModel viewModel =
            PairingScreenViewModel.fromStore(store);

        viewModel.onSubmitCode('123456', 'Desktop');

        expect(store.state.pairing.phase, PairingPhase.confirming);
      },
    );

    test('onBack dispatches PairingBackRequestedAction', () {
      // PairingBackRequestedAction has no reducer effect (pure navigation),
      // so dispatch is observed directly rather than through a phase change.
      final List<Object?> dispatchedActions = [];
      final Store<AppState> store = const CreateStore()(
        middleware: [
          (Store<AppState> store, dynamic action, NextDispatcher next) {
            dispatchedActions.add(action);
            next(action);
          },
        ],
      );
      final PairingScreenViewModel viewModel =
          PairingScreenViewModel.fromStore(store);

      viewModel.onBack();

      expect(dispatchedActions, contains(const PairingBackRequestedAction()));
    });
  });
}
