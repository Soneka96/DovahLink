import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the real discovery state shown by [DiscoverDialog].
class DiscoverDialogViewModel extends Equatable {
  /// The current discovery operation state.
  final ConnectionDiscoveryStatus status;

  /// The current ephemeral candidates, mapped to the prototype's Local Host cards.
  final List<HostCardViewData> candidates;

  /// The semantic reason the latest discovery operation failed, or `null` when it did not fail.
  final ConnectionFailureReason? failure;

  /// Whether the user may request discovery in the current state.
  final bool canDiscover;

  /// The selected candidate retained while its real authentication is in progress.
  final HostCardViewData? selectedCandidate;

  /// The current SDK-backed pairing/authentication phase.
  final PairingPhase pairingPhase;

  /// Whether secure storage supports the selected pairing flow.
  final PairingSupport pairingSupport;

  /// Whether selecting a candidate can start a new pairing lifecycle right now.
  final bool canSelectCandidate;

  /// Called when the dialog opens or the user requests another search.
  final void Function() onDiscover;

  /// Selects [candidate] and starts its real SDK-backed authentication lifecycle.
  final void Function(HostCardViewData candidate) onSelectCandidate;

  /// Ends a candidate authentication flow when the dialog is dismissed before its outcome.
  final void Function() onDispose;

  /// Creates the discovery dialog ViewModel.
  const DiscoverDialogViewModel({
    required this.status,
    required this.candidates,
    required this.failure,
    required this.canDiscover,
    required this.selectedCandidate,
    required this.pairingPhase,
    required this.pairingSupport,
    required this.canSelectCandidate,
    required this.onDiscover,
    required this.onSelectCandidate,
    required this.onDispose,
  });

  /// Whether a real pairing/authentication outcome is ready for the existing pairing flow.
  bool get shouldContinueToPairing =>
      selectedCandidate != null &&
      switch (pairingPhase) {
        PairingPhase.connecting =>
          pairingSupport == PairingSupport.secureStorageUnavailable,
        PairingPhase.unpaired ||
        PairingPhase.requestingCode ||
        PairingPhase.awaitingCode ||
        PairingPhase.confirming ||
        PairingPhase.failed => true,
        PairingPhase.none ||
        PairingPhase.disconnected ||
        PairingPhase.trusted => false,
      };

  /// Whether candidate authentication produced an already trusted session.
  bool get hasTrustedCandidate =>
      selectedCandidate != null && pairingPhase == PairingPhase.trusted;

  /// Builds a ViewModel from the Redux [store].
  factory DiscoverDialogViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    final Host? selectedHost = ConnectionSelectors.selectedHostSelector(state);
    final bool selectedHostIsCandidate =
        ConnectionSelectors.selectedHostSourceSelector(state) ==
        ConnectionHostSelectionSource.candidate;
    return DiscoverDialogViewModel(
      status: ConnectionSelectors.discoveryStatusSelector(state),
      candidates: [
        for (final Host host in ConnectionSelectors.hostsSelector(state))
          _candidateCard(host),
      ],
      failure: ConnectionSelectors.discoveryFailureSelector(state),
      canDiscover: ConnectionSelectors.canDiscoverSelector(state),
      selectedCandidate: selectedHost != null && selectedHostIsCandidate
          ? _candidateCard(selectedHost)
          : null,
      pairingPhase: PairingSelectors.phaseSelector(state),
      pairingSupport: state.pairing.support,
      canSelectCandidate: PairingSelectors.canStartPairingSelector(state),
      onDiscover: () =>
          store.dispatch(const ConnectionDiscoveryRequestedAction()),
      onSelectCandidate: (HostCardViewData candidate) {
        if (!PairingSelectors.canStartPairingSelector(store.state)) {
          return;
        }
        store.dispatch(
          ConnectionHostSelectedAction(
            candidate.host,
            source: ConnectionHostSelectionSource.candidate,
          ),
        );
        store.dispatch(const PairingStartedAction());
      },
      onDispose: () {
        final Host? selectedHost = ConnectionSelectors.selectedHostSelector(
          store.state,
        );
        final PairingPhase pairingPhase = PairingSelectors.phaseSelector(
          store.state,
        );
        if (selectedHost != null &&
            ConnectionSelectors.selectedHostSourceSelector(store.state) ==
                ConnectionHostSelectionSource.candidate &&
            pairingPhase != PairingPhase.none) {
          store.dispatch(
            PairingDisposedAction(
              wasTrusted: pairingPhase == PairingPhase.trusted,
            ),
          );
        }
      },
    );
  }

  /// Maps [host] to the prototype's fixed Local Host candidate presentation.
  static HostCardViewData _candidateCard(Host host) => HostCardViewData(
    host: host,
    source: ConnectionHostSelectionSource.candidate,
    title: 'Local Host',
    subtitle: 'DovahLink · Ready to connect',
    detail: host.uri.authority.isEmpty
        ? host.uri.toString()
        : host.uri.authority,
    state: DovahConnectionCardState.unknown,
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    status,
    candidates,
    failure,
    canDiscover,
    selectedCandidate,
    pairingPhase,
    pairingSupport,
    canSelectCandidate,
  ];
}
