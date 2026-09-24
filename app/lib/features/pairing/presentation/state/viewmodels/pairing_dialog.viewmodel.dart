import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_section.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_dialog.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [PairingDialog].
class PairingDialogViewModel extends Equatable {
  /// The dialog title for the current pairing state.
  final String title;

  /// Creates a pairing dialog ViewModel.
  const PairingDialogViewModel({required this.title});

  /// Builds a ViewModel from the Redux [store]. The title follows the pairing state: it names
  /// the Host while pairing, and announces when the Host is not running, when trust has to be
  /// confirmed again, and when the device is connected.
  factory PairingDialogViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    final String hostName =
        ConnectionSelectors.selectedHostNameSelector(state) ??
        PairingSectionViewModel.unknownHostName;
    return PairingDialogViewModel(
      title: switch (PairingSelectors.phaseSelector(state)) {
        PairingPhase.disconnected => 'Skyrim isn’t running',
        PairingPhase.trusted => 'Connected',
        PairingPhase.unpaired when PairingSelectors.isRepairSelector(state) =>
          'Pairing required',
        _ => 'Pair with $hostName',
      },
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [title];
}
