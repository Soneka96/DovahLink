import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';

/// The root immutable state held by the DovahLink Redux store.
class AppState {
  /// Creates the root state from feature states.
  const AppState({required this.connection});

  /// Current connection state.
  final ConnectionState connection;

  /// Returns the initial state for a new client session.
  factory AppState.initial() => AppState(connection: ConnectionState.initial());
}
