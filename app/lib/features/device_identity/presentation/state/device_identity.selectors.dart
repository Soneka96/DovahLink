import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for device-identity presentation.
abstract final class DeviceIdentitySelectors {
  /// Returns the resolved device name, or `null` when local identity failed to load.
  /// @param state The application state to inspect.
  /// @return The resolved name, or `null` when it could not be loaded.
  static String? displayNameSelector(AppState state) =>
      state.deviceIdentity.displayName;

  /// Returns the user-safe device-name load failure, or `null` when loading succeeded.
  /// @param state The application state to inspect.
  /// @return The load failure message, or `null` when loading succeeded.
  static String? loadFailureSelector(AppState state) =>
      state.deviceIdentity.loadFailure;

  /// Returns whether a device-name save and remote acknowledgement are pending.
  /// @param state The application state to inspect.
  /// @return `true` while a save request is active.
  static bool isSavingSelector(AppState state) => state.deviceIdentity.isSaving;

  /// Returns the local validation or persistence failure, or `null` when absent.
  /// @param state The application state to inspect.
  /// @return The user-safe save failure, or `null`.
  static String? saveFailureSelector(AppState state) =>
      state.deviceIdentity.saveFailure;

  /// Returns the Host rename outcome from the most recent save.
  /// @param state The application state to inspect.
  /// @return The typed remote rename status.
  static DeviceNameRenameStatus remoteRenameStatusSelector(AppState state) =>
      state.deviceIdentity.remoteRenameStatus;

  /// Returns the exact Host associated with the most recent rename response.
  /// @param state The application state to inspect.
  /// @return The Host ID, or `null` if no request was sent.
  static String? remoteHostIdSelector(AppState state) =>
      state.deviceIdentity.remoteHostId;
}
