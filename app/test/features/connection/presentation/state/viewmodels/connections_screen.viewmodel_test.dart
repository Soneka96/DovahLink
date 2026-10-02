import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../../fixtures/fixtures.dart';

/// Exercises [ConnectionsScreenViewModel.fromStore] projections.
void main() {
  group('ConnectionsScreenViewModel fromStore()', () {
    test('fromStore starts with no cards before discovery', () {
      final Store<AppState> store = const CreateStore()();

      final ConnectionsScreenViewModel viewModel =
          ConnectionsScreenViewModel.fromStore(store);

      expect(viewModel.hostCards, isEmpty);
      expect(viewModel.canDiscover, isTrue);
    });

    test('fromStore exposes the discovery capability', () {
      for (final ConnectionDiscoveryStatus status
          in ConnectionDiscoveryStatus.values) {
        final Store<AppState> store = const CreateStore()(
          initialState: AppState(
            connection: ConnectionState(discoveryStatus: status),
            pairing: PairingState.initial(),
          ),
        );

        final ConnectionsScreenViewModel viewModel =
            ConnectionsScreenViewModel.fromStore(store);

        expect(
          viewModel.canDiscover,
          ConnectionSelectors.canDiscoverSelector(store.state),
        );
        expect(
          viewModel.canDiscover,
          status != ConnectionDiscoveryStatus.discovering,
        );
      }
    });

    test('fromStore keeps discovery candidates out of Known Host cards', () {
      final KnownHost knownHost = Fixtures.buildKnownHost();
      final Store<AppState> store = const CreateStore()(
        initialState: AppState(
          connection: ConnectionState(
            hosts: [Fixtures.buildHost()],
            knownHosts: [knownHost],
            discoveryStatus: ConnectionDiscoveryStatus.failed,
            discoveryFailure: ConnectionFailureReason.invalidResponse,
          ),
          pairing: PairingState.initial(),
        ),
      );

      final ConnectionsScreenViewModel viewModel =
          ConnectionsScreenViewModel.fromStore(store);

      expect(viewModel.hostCards, hasLength(1));
      expect(viewModel.hostCards.single.host, knownHost.host);
      expect(
        viewModel.hostCards.single.source,
        ConnectionHostSelectionSource.knownHost,
      );
    });

    test('onSelectHost dispatches candidate selection with its source', () {
      final HostCardViewData card = Fixtures.buildHostCardViewData(
        host: Fixtures.buildHost(),
      );
      final Store<AppState> store = const CreateStore()(
        initialState: AppState(
          connection: ConnectionState(hosts: [card.host]),
          pairing: PairingState.initial(),
        ),
      );
      final ConnectionsScreenViewModel viewModel =
          ConnectionsScreenViewModel.fromStore(store);

      viewModel.onSelectHost(card);

      expect(ConnectionSelectors.selectedHostSelector(store.state), card.host);
      expect(
        ConnectionSelectors.selectedHostSourceSelector(store.state),
        ConnectionHostSelectionSource.candidate,
      );
    });

    test('onSelectHost dispatches Known Host selection with its source', () {
      final HostCardViewData card = Fixtures.buildHostCardViewData(
        source: ConnectionHostSelectionSource.knownHost,
      );
      final Store<AppState> store = const CreateStore()(
        initialState: AppState(
          connection: ConnectionState(
            knownHosts: [
              Fixtures.buildKnownHost(
                host: card.host,
                availability: HostAvailability.unknown,
              ),
            ],
          ),
          pairing: PairingState.initial(),
        ),
      );
      final ConnectionsScreenViewModel viewModel =
          ConnectionsScreenViewModel.fromStore(store);

      viewModel.onSelectHost(viewModel.hostCards.single);

      expect(ConnectionSelectors.selectedHostSelector(store.state), card.host);
      expect(
        ConnectionSelectors.selectedHostSourceSelector(store.state),
        ConnectionHostSelectionSource.knownHost,
      );
    });

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
            connection: ConnectionState(
              knownHosts: [
                Fixtures.buildKnownHost(host: first),
                Fixtures.buildKnownHost(host: second),
              ],
            ),
            pairing: PairingState.initial(),
          ),
        );

        ConnectionsScreenViewModel.fromStore(
          store,
        ).onSelectHost(ConnectionSelectors.hostCardsSelector(store.state).last);

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
        canDiscover: true,
        onSelectHost: (HostCardViewData card) {},
      );
      final ConnectionsScreenViewModel second = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardViewData(title: 'Other')],
        canDiscover: true,
        onSelectHost: (HostCardViewData card) {},
      );

      expect(first, isNot(second));
    });

    test('canDiscover participates in ViewModel equality', () {
      final ConnectionsScreenViewModel first = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardViewData()],
        canDiscover: true,
        onSelectHost: (HostCardViewData card) {},
      );
      final ConnectionsScreenViewModel second = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardViewData()],
        canDiscover: false,
        onSelectHost: (HostCardViewData card) {},
      );

      expect(first, isNot(second));
    });
  });
}
