import 'package:dovahlink_client/features/appearance/presentation/state/appearance.reducer.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.reducer.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Applies a Redux action to [state] and returns the next root state.
///
/// [AppState.connection] is preserved because no current action reduces it.
AppState appReducer(AppState state, Object? action) {
  final PairingState pairing = pairingReducer(state.pairing, action);
  final AppearanceState appearance = appearanceReducer(
    state.appearance,
    action,
  );
  if (identical(pairing, state.pairing) &&
      identical(appearance, state.appearance)) {
    return state;
  }
  return AppState(
    connection: state.connection,
    pairing: pairing,
    appearance: appearance,
  );
}
