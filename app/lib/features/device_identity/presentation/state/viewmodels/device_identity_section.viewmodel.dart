import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel for the device-identity section of Settings.
class DeviceIdentitySectionViewModel extends Equatable {
  /// The resolved name in the input field, or `null` when it could not load.
  final String? displayName;

  /// The failure to read the saved override, if any.
  final String? loadFailure;

  /// Whether a local save and active-Host response are pending.
  final bool isSaving;

  /// The local validation or persistence error, if any.
  final String? saveFailure;

  /// The last active-Host rename outcome, or `null` before a completed save.
  final DeviceNameRenameStatus? remoteRenameStatus;

  /// The Host associated with [remoteRenameStatus], if a rename was attempted.
  final String? remoteHostId;

  /// The unique Host with a currently admitted SDK session, if one exists.
  final String? admittedHostId;

  /// Dispatches a Settings device-name save request.
  final void Function(String displayName) onSave;

  /// Creates the device-identity section ViewModel.
  /// @param displayName The resolved current name.
  /// @param loadFailure The local preference read error, if any.
  /// @param isSaving Whether a name save is pending.
  /// @param saveFailure The local save error, if any.
  /// @param remoteRenameStatus The last typed Host outcome.
  /// @param remoteHostId The Host associated with that result.
  /// @param admittedHostId The current SDK-admitted Host ID.
  /// @param onSave Dispatches the proposed text.
  const DeviceIdentitySectionViewModel({
    required this.displayName,
    required this.loadFailure,
    required this.isSaving,
    required this.saveFailure,
    required this.remoteRenameStatus,
    required this.remoteHostId,
    required this.admittedHostId,
    required this.onSave,
  });

  /// Builds the section's Redux projection and save callback from [store].
  /// @param store The application store read by the Settings section.
  /// @return The section's current presentation values and action.
  factory DeviceIdentitySectionViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return DeviceIdentitySectionViewModel(
      displayName: DeviceIdentitySelectors.displayNameSelector(state),
      loadFailure: DeviceIdentitySelectors.loadFailureSelector(state),
      isSaving: DeviceIdentitySelectors.isSavingSelector(state),
      saveFailure: DeviceIdentitySelectors.saveFailureSelector(state),
      remoteRenameStatus: DeviceIdentitySelectors.remoteRenameStatusSelector(
        state,
      ),
      remoteHostId: DeviceIdentitySelectors.remoteHostIdSelector(state),
      admittedHostId: ConnectionSelectors.admittedHostIdSelector(state),
      onSave: (String displayName) => store.dispatch(
        DeviceNameSaveRequestedAction(displayName: displayName),
      ),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    displayName,
    loadFailure,
    isSaving,
    saveFailure,
    remoteRenameStatus,
    remoteHostId,
    admittedHostId,
  ];
}
