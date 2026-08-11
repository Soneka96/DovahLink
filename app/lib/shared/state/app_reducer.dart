import 'package:dovahlink_client/features/connection/presentation/state/connection.reducer.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Applies a Redux action to [state] and returns the next root state.
AppState appReducer(AppState state, Object? action) {
  final ConnectionState connection = connectionReducer(
    state.connection,
    action,
  );
  if (identical(connection, state.connection)) {
    return state;
  }
  return AppState(connection: connection);
}
