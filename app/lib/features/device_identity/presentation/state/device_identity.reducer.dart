import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';

/// Reduces successful device-name persistence into [DeviceIdentityState].
/// @return A reducer that recognizes [DeviceNameSavedAction].
Reducer<DeviceIdentityState> deviceIdentityReducer =
    combineReducers<DeviceIdentityState>([
      TypedReducer<DeviceIdentityState, DeviceNameSavedAction>(
        deviceNameSavedReducer,
      ).call,
    ]);

/// Applies [DeviceNameSavedAction.displayName] and clears any previous load error.
/// @param state The current device-identity state.
/// @param action The successfully persisted display name.
/// @return The updated device-identity state.
DeviceIdentityState deviceNameSavedReducer(
  DeviceIdentityState state,
  DeviceNameSavedAction action,
) => DeviceIdentityState(displayName: action.displayName);
