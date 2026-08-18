import 'package:dovahlink_client/features/connection/presentation/state/connection.reducer.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.reducer.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Applies a Redux action to [state] and returns the next root state.
AppState appReducer(AppState state, Object? action) {
  final ConnectionState connection = connectionReducer(
    state.connection,
    action,
  );
  final PairingState pairing = pairingReducer(state.pairing, action);
  if (identical(connection, state.connection) &&
      identical(pairing, state.pairing)) {
    return state;
  }
  return AppState(connection: connection, pairing: pairing);
}
