import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
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
      expect(viewModel.discoveryStatus, ConnectionDiscoveryStatus.idle);
      expect(viewModel.canDiscover, isTrue);
      expect(viewModel.discoveryFailure, isNull);
    });

    test(
      'fromStore exposes selector capability and discovery status separately',
      () {
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
          expect(viewModel.discoveryStatus, status);
        }
      },
    );

    test('fromStore projects the current discovery failure reason', () {
      final Store<AppState> store = const CreateStore()(
        initialState: AppState(
          connection: ConnectionState(
            hosts: [Fixtures.buildHost()],
            discoveryStatus: ConnectionDiscoveryStatus.failed,
            discoveryFailure: ConnectionFailureReason.invalidResponse,
          ),
          pairing: PairingState.initial(),
        ),
      );

      final ConnectionsScreenViewModel viewModel =
          ConnectionsScreenViewModel.fromStore(store);

      expect(viewModel.discoveryStatus, ConnectionDiscoveryStatus.failed);
      expect(
        viewModel.discoveryFailure,
        ConnectionFailureReason.invalidResponse,
      );
      expect(viewModel.hostCards, hasLength(1));
    });

    test(
      'onSelectHost dispatches ConnectionHostSelectedAction, recording the selected Host',
      () {
        final Host host = Fixtures.buildHost();
        final Store<AppState> store = const CreateStore()(
          initialState: AppState(
            connection: ConnectionState(hosts: [host]),
            pairing: PairingState.initial(),
          ),
        );
        final ConnectionsScreenViewModel viewModel =
            ConnectionsScreenViewModel.fromStore(store);

        viewModel.onSelectHost(host);

        expect(ConnectionSelectors.selectedHostSelector(store.state), host);
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
        discoveryStatus: ConnectionDiscoveryStatus.idle,
        canDiscover: true,
        discoveryFailure: null,
        onDiscover: () {},
        onSelectHost: (Host host) {},
      );
      final ConnectionsScreenViewModel second = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardViewData(title: 'Other')],
        discoveryStatus: ConnectionDiscoveryStatus.idle,
        canDiscover: true,
        discoveryFailure: null,
        onDiscover: () {},
        onSelectHost: (Host host) {},
      );

      expect(first, isNot(second));
    });

    test('canDiscover participates in ViewModel equality', () {
      final ConnectionsScreenViewModel first = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardViewData()],
        discoveryStatus: ConnectionDiscoveryStatus.idle,
        canDiscover: true,
        discoveryFailure: null,
        onDiscover: () {},
        onSelectHost: (Host host) {},
      );
      final ConnectionsScreenViewModel second = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardViewData()],
        discoveryStatus: ConnectionDiscoveryStatus.idle,
        canDiscover: false,
        discoveryFailure: null,
        onDiscover: () {},
        onSelectHost: (Host host) {},
      );

      expect(first, isNot(second));
    });
  });

  group('ConnectionsScreenViewModel onDiscover behaves correctly', () {
    test('onDiscover dispatches ConnectionDiscoveryRequestedAction', () {
      final List<Object?> actions = [];
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
      final ConnectionsScreenViewModel viewModel =
          ConnectionsScreenViewModel.fromStore(store);

      viewModel.onDiscover();

      expect(actions, [const ConnectionDiscoveryRequestedAction()]);
    });
  });
}
