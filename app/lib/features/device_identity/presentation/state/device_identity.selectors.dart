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
}
