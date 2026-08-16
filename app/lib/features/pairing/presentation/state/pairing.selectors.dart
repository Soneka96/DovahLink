import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for pairing presentation state.
abstract final class PairingSelectors {
  /// Returns the current pairing lifecycle phase.
  static PairingPhase phaseSelector(AppState state) => state.pairing.phase;

  /// Returns the user-visible label for the current pairing phase.
  static String statusLabelSelector(AppState state) =>
      phaseSelector(state).label;

  /// Returns the reported bridge version, or `null` when unknown.
  static String? bridgeVersionSelector(AppState state) =>
      state.pairing.bridgeVersion;

  /// Returns the user-safe pairing error, or `null` when absent.
  static String? errorSelector(AppState state) => state.pairing.error;
}
