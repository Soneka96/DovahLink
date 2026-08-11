import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for connection presentation state.
abstract final class ConnectionSelectors {
  /// Returns the current connection lifecycle phase.
  static ConnectionPhase phaseSelector(AppState state) =>
      state.connection.phase;

  /// Returns the user-visible label for the current connection phase.
  static String statusLabelSelector(AppState state) =>
      phaseSelector(state).label;

  /// Returns the active session identifier, or `null` when unavailable.
  static String? sessionIdSelector(AppState state) =>
      state.connection.session?.sessionId;

  /// Returns the user-safe connection error, or `null` when absent.
  static String? errorSelector(AppState state) => state.connection.error;
}
