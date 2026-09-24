import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../../fixtures/fixtures.dart';

/// Exercises [ConnectionsScreenViewModel.fromStore] projections.
void main() {
  group('ConnectionsScreenViewModel fromStore()', () {
    test('fromStore selects the card for the static default Host', () {
      final Store<AppState> store = const CreateStore()();

      final ConnectionsScreenViewModel viewModel =
          ConnectionsScreenViewModel.fromStore(store);

      expect(viewModel.hostCards, [Fixtures.buildHostCardViewData()]);
    });

    test(
      'onSelectHost dispatches ConnectionHostSelectedAction, recording the selected Host',
      () {
        final Store<AppState> store = const CreateStore()();
        final ConnectionsScreenViewModel viewModel =
            ConnectionsScreenViewModel.fromStore(store);

        viewModel.onSelectHost(viewModel.hostCards.single.host);

        expect(
          ConnectionSelectors.selectedHostSelector(store.state),
          Fixtures.buildHost(),
        );
      },
    );

    test('onSelectHost leaves no Host selected before it is called', () {
      final Store<AppState> store = const CreateStore()();
      ConnectionsScreenViewModel.fromStore(store);

      expect(ConnectionSelectors.selectedHostSelector(store.state), isNull);
    });

    test(
      'onSelectHost records the second of two Hosts sharing a display name by URI',
      () {
        final Host first = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://192.168.1.10:1000/'),
        );
        final Host second = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://192.168.1.11:2000/'),
        );
        final Store<AppState> store = const CreateStore()(
          initialState: AppState(
            connection: ConnectionState(hosts: [first, second]),
            pairing: PairingState.initial(),
          ),
        );

        ConnectionsScreenViewModel.fromStore(store).onSelectHost(second);

        expect(
          ConnectionSelectors.selectedHostSelector(store.state)?.uri,
          second.uri,
        );
      },
    );

    test(
      'two ViewModels with the same cards are equal, regardless of callback identity',
      () {
        final ConnectionsScreenViewModel first =
            ConnectionsScreenViewModel.fromStore(const CreateStore()());
        final ConnectionsScreenViewModel second =
            ConnectionsScreenViewModel.fromStore(const CreateStore()());

        expect(first, second);
      },
    );

    test('two ViewModels with different cards are not equal', () {
      final ConnectionsScreenViewModel first = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardViewData()],
        onSelectHost: (Host host) {},
      );
      final ConnectionsScreenViewModel second = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardViewData(title: 'Other')],
        onSelectHost: (Host host) {},
      );

      expect(first, isNot(second));
    });
  });
}
