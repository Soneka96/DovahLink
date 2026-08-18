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

  /// Returns remaining seconds until code expires, or null if no active code.
  /// Clamps to 0 if the expiry time is in the past (non-negative duration).
  static int? codeCountdownSecondsSelector(AppState state) {
    final expiresAt = state.pairing.codeExpiresAt;
    if (expiresAt == null) return null;
    final remaining = expiresAt.difference(DateTime.now()).inSeconds;
    return remaining < 0 ? 0 : remaining;
  }

  /// Returns remaining seconds until renotify is available, or null if not cooling down.
  /// Clamps to 0 if the availability time is in the past (non-negative duration).
  static int? renotifyCooldownSecondsSelector(AppState state) {
    final availableAt = state.pairing.renotifyAvailableAt;
    if (availableAt == null) return null;
    final remaining = availableAt.difference(DateTime.now()).inSeconds;
    return remaining < 0 ? 0 : remaining;
  }
}
