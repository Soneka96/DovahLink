import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/discover_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../../fixtures/fixtures.dart';

/// Exercises [DiscoverDialogViewModel.fromStore] and its discovery request callback.
void main() {
  group('DiscoverDialogViewModel fromStore()', () {
    test('fromStore maps each ephemeral candidate to the Local Host card', () {
      final candidate = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final Store<AppState> store = const CreateStore()(
        initialState: AppState(
          connection: ConnectionState(hosts: [candidate]),
          pairing: PairingState.initial(),
        ),
      );

      final DiscoverDialogViewModel viewModel =
          DiscoverDialogViewModel.fromStore(store);

      expect(viewModel.candidates, hasLength(1));
      expect(viewModel.candidates.single.host, candidate);
      expect(
        viewModel.candidates.single.source,
        ConnectionHostSelectionSource.candidate,
      );
      expect(viewModel.candidates.single.title, 'Local Host');
      expect(
        viewModel.candidates.single.subtitle,
        'DovahLink · Ready to connect',
      );
      expect(viewModel.candidates.single.detail, '127.0.0.1:58231');
      expect(
        viewModel.candidates.single.state,
        DovahConnectionCardState.unknown,
      );
    });

    test('fromStore projects every real discovery status and capability', () {
      for (final ConnectionDiscoveryStatus status
          in ConnectionDiscoveryStatus.values) {
        final Store<AppState> store = const CreateStore()(
          initialState: AppState(
            connection: ConnectionState(discoveryStatus: status),
            pairing: PairingState.initial(),
          ),
        );

        final DiscoverDialogViewModel viewModel =
            DiscoverDialogViewModel.fromStore(store);

        expect(viewModel.status, status);
        expect(
          viewModel.canDiscover,
          status != ConnectionDiscoveryStatus.discovering,
        );
      }
    });

    test('fromStore maps the app-owned discovery failure reason', () {
      final Store<AppState> store = const CreateStore()(
        initialState: AppState(
          connection: const ConnectionState(
            discoveryStatus: ConnectionDiscoveryStatus.failed,
            discoveryFailure: ConnectionFailureReason.invalidResponse,
          ),
          pairing: PairingState.initial(),
        ),
      );

      final DiscoverDialogViewModel viewModel =
          DiscoverDialogViewModel.fromStore(store);

      expect(viewModel.failure, ConnectionFailureReason.invalidResponse);
    });
  });

  group('DiscoverDialogViewModel onDiscover behaves correctly', () {
    test('onDiscover dispatches ConnectionDiscoveryRequestedAction', () {
      final List<Object?> actions = [];

      /// Records each action before passing it to the reducer.
      void recordActions(
        Store<AppState> store,
        dynamic action,
        NextDispatcher next,
      ) {
        actions.add(action);
        next(action);
      }

      final Store<AppState> store = const CreateStore()(
        middleware: [recordActions],
      );

      DiscoverDialogViewModel.fromStore(store).onDiscover();

      expect(actions, [const ConnectionDiscoveryRequestedAction()]);
    });
  });
}
