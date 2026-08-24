import 'package:dovahlink_client/features/pairing/presentation/state/pairing.reducer.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Applies a Redux action to [state] and returns the next root state.
///
/// [AppState.connection] never changes here -- no action reduces it, per
/// `connection.state.dart`'s current scope (a static Bridge list only; see
/// `ai/context/flutter/architecture.md`'s "Connection and recovery state" for the fuller model a
/// future Stage 4/10 delivery will need).
AppState appReducer(AppState state, Object? action) {
  final PairingState pairing = pairingReducer(state.pairing, action);
  if (identical(pairing, state.pairing)) {
    return state;
  }
  return AppState(connection: state.connection, pairing: pairing);
}
