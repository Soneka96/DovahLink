import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/discover_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
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
      expect(viewModel.selectedCandidate, isNull);
      expect(viewModel.shouldContinueToPairing, isFalse);
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

  group('DiscoverDialogViewModel selects candidates', () {
    test(
      'onSelectCandidate dispatches selection before PairingStartedAction',
      () {
        final Host candidate = Fixtures.buildHost();
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
          initialState: AppState(
            connection: ConnectionState(hosts: [candidate]),
            pairing: PairingState.initial(),
          ),
          middleware: [recordActions],
        );
        final DiscoverDialogViewModel viewModel =
            DiscoverDialogViewModel.fromStore(store);

        viewModel.onSelectCandidate(viewModel.candidates.single);

        expect(actions, [
          ConnectionHostSelectedAction(
            candidate,
            source: ConnectionHostSelectionSource.candidate,
          ),
          const PairingStartedAction(),
        ]);
        expect(
          ConnectionSelectors.selectedHostSelector(store.state),
          candidate,
        );
        expect(store.state.pairing.phase, PairingPhase.connecting);
        expect(
          DiscoverDialogViewModel.fromStore(store).shouldContinueToPairing,
          isFalse,
        );
      },
    );

    test('onSelectCandidate does not restart an active pairing lifecycle', () {
      final Host candidate = Fixtures.buildHost();
      final List<Object?> actions = [];

      /// Records any selection or pairing action before the reducer receives it.
      void recordActions(
        Store<AppState> store,
        dynamic action,
        NextDispatcher next,
      ) {
        actions.add(action);
        next(action);
      }

      final Store<AppState> store = const CreateStore()(
        initialState: AppState(
          connection: ConnectionState(hosts: [candidate]),
          pairing: PairingState.initial().copyWith(
            phase: PairingPhase.connecting,
          ),
        ),
        middleware: [recordActions],
      );

      DiscoverDialogViewModel.fromStore(store).onSelectCandidate(
        DiscoverDialogViewModel.fromStore(store).candidates.single,
      );

      expect(actions, isEmpty);
      expect(ConnectionSelectors.selectedHostSelector(store.state), isNull);
    });

    test('shouldContinueToPairing waits through connection and retries', () {
      final Host candidate = Fixtures.buildHost();
      for (final PairingPhase phase in [
        PairingPhase.none,
        PairingPhase.connecting,
        PairingPhase.disconnected,
      ]) {
        final Store<AppState> store = const CreateStore()(
          initialState: AppState(
            connection: ConnectionState(
              hosts: [candidate],
              selectedHost: candidate,
              selectedHostSource: ConnectionHostSelectionSource.candidate,
            ),
            pairing: PairingState.initial().copyWith(phase: phase),
          ),
        );

        expect(
          DiscoverDialogViewModel.fromStore(store).shouldContinueToPairing,
          isFalse,
          reason: '$phase',
        );
      }
    });

    test(
      'shouldContinueToPairing returns after the real lifecycle outcome',
      () {
        final Host candidate = Fixtures.buildHost();
        for (final PairingPhase phase in [
          PairingPhase.unpaired,
          PairingPhase.requestingCode,
          PairingPhase.awaitingCode,
          PairingPhase.trusted,
          PairingPhase.failed,
        ]) {
          final Store<AppState> store = const CreateStore()(
            initialState: AppState(
              connection: ConnectionState(
                hosts: [candidate],
                selectedHost: candidate,
                selectedHostSource: ConnectionHostSelectionSource.candidate,
              ),
              pairing: PairingState.initial().copyWith(phase: phase),
            ),
          );

          expect(
            DiscoverDialogViewModel.fromStore(store).shouldContinueToPairing,
            isTrue,
            reason: '$phase',
          );
        }
      },
    );

    test(
      'shouldContinueToPairing returns when secure storage is unavailable',
      () {
        final Host candidate = Fixtures.buildHost();
        final Store<AppState> store = const CreateStore()(
          initialState: AppState(
            connection: ConnectionState(
              selectedHost: candidate,
              selectedHostSource: ConnectionHostSelectionSource.candidate,
            ),
            pairing: PairingState.initial(
              support: PairingSupport.secureStorageUnavailable,
            ).copyWith(phase: PairingPhase.connecting),
          ),
        );

        expect(
          DiscoverDialogViewModel.fromStore(store).shouldContinueToPairing,
          isTrue,
        );
      },
    );

    test('onDispose cancels a pending candidate authentication', () {
      final Host candidate = Fixtures.buildHost();
      final Store<AppState> store = const CreateStore()(
        initialState: AppState(
          connection: ConnectionState(
            hosts: [candidate],
            selectedHost: candidate,
            selectedHostSource: ConnectionHostSelectionSource.candidate,
          ),
          pairing: PairingState.initial().copyWith(
            phase: PairingPhase.connecting,
          ),
        ),
      );

      DiscoverDialogViewModel.fromStore(store).onDispose();

      expect(store.state.pairing.phase, PairingPhase.none);
      expect(ConnectionSelectors.selectedHostSelector(store.state), candidate);
    });

    test(
      'onDispose ignores a stale candidate with no active pairing phase',
      () {
        final Host candidate = Fixtures.buildHost();
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
          initialState: AppState(
            connection: ConnectionState(
              selectedHost: candidate,
              selectedHostSource: ConnectionHostSelectionSource.candidate,
            ),
            pairing: PairingState.initial(),
          ),
          middleware: [recordActions],
        );

        DiscoverDialogViewModel.fromStore(store).onDispose();

        expect(actions, isEmpty);
      },
    );
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
