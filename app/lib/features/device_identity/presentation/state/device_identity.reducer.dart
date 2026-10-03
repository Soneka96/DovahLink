import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Reduces successful device-name persistence into [DeviceIdentityState].
/// @return A reducer that recognizes [DeviceNameSavedAction].
Reducer<DeviceIdentityState> deviceIdentityReducer =
    combineReducers<DeviceIdentityState>([
      TypedReducer<DeviceIdentityState, DeviceNameSavedAction>(
        deviceNameSavedReducer,
      ).call,
      TypedReducer<DeviceIdentityState, DeviceNameSaveRequestedAction>(
        deviceNameSaveRequestedReducer,
      ).call,
      TypedReducer<DeviceIdentityState, DeviceNameSaveFailedAction>(
        deviceNameSaveFailedReducer,
      ).call,
      TypedReducer<DeviceIdentityState, DeviceNameSaveFinishedAction>(
        deviceNameSaveFinishedReducer,
      ).call,
    ]);

/// Marks the local save and possible Host rename as pending.
/// @param state The current device-identity state.
/// @param action The proposed local name.
/// @return State with prior save feedback cleared.
DeviceIdentityState deviceNameSaveRequestedReducer(
  DeviceIdentityState state,
  DeviceNameSaveRequestedAction action,
) => DeviceIdentityState(
  displayName: state.displayName,
  loadFailure: state.loadFailure,
  isSaving: true,
  remoteRenameStatus: DeviceNameRenameStatus.notAttempted,
);

/// Applies [DeviceNameSavedAction.displayName] and clears any previous load error.
/// @param state The current device-identity state.
/// @param action The successfully persisted display name.
/// @return The updated device-identity state.
DeviceIdentityState deviceNameSavedReducer(
  DeviceIdentityState state,
  DeviceNameSavedAction action,
) => DeviceIdentityState(
  displayName: action.displayName,
  isSaving: true,
  saveFailure: null,
);

/// Ends a local save attempt that failed before persistence succeeded.
/// @param state The current device-identity state.
/// @param action The local failure to display.
/// @return State retaining the previous name and exposing the failure.
DeviceIdentityState deviceNameSaveFailedReducer(
  DeviceIdentityState state,
  DeviceNameSaveFailedAction action,
) => DeviceIdentityState(
  displayName: state.displayName,
  loadFailure: state.loadFailure,
  saveFailure: action.message,
  remoteRenameStatus: DeviceNameRenameStatus.notAttempted,
);

/// Finishes local-save feedback with the separately reported Host outcome.
/// @param state The current device-identity state.
/// @param action The exact Host rename result.
/// @return State with the save complete and remote status associated with its Host.
DeviceIdentityState deviceNameSaveFinishedReducer(
  DeviceIdentityState state,
  DeviceNameSaveFinishedAction action,
) => DeviceIdentityState(
  displayName: state.displayName,
  loadFailure: state.loadFailure,
  saveFailure: state.saveFailure,
  remoteRenameStatus: action.remoteRenameStatus,
  remoteHostId: action.remoteHostId,
);
