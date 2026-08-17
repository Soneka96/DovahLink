import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Reduces pairing actions into [PairingState].
Reducer<PairingState> pairingReducer = combineReducers<PairingState>([
  TypedReducer<PairingState, PairingStartedAction>(
    pairingStartedReducer,
  ).call,

  TypedReducer<PairingState, PairingAuthenticatedAction>(
    pairingAuthenticatedReducer,
  ).call,

  TypedReducer<PairingState, PairingCodeRequestedAction>(
    pairingCodeRequestedReducer,
  ).call,

  TypedReducer<PairingState, PairingCodeAvailableAction>(
    pairingCodeAvailableReducer,
  ).call,

  TypedReducer<PairingState, PairingCodeSubmittedAction>(
    pairingCodeSubmittedReducer,
  ).call,

  TypedReducer<PairingState, PairingConfirmedAction>(
    pairingConfirmedReducer,
  ).call,

  TypedReducer<PairingState, PairingDisconnectedAction>(
    pairingDisconnectedReducer,
  ).call,

  TypedReducer<PairingState, PairingRevokedAction>(
    pairingRevokedReducer,
  ).call,

  TypedReducer<PairingState, PairingFailedAction>(
    pairingFailedReducer,
  ).call,

  TypedReducer<PairingState, PairingDisposedAction>(
    pairingDisposedReducer,
  ).call,
]);

/// Handles [PairingStartedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingStartedReducer(
  PairingState state,
  PairingStartedAction action,
) => state.copyWith(phase: PairingPhase.connecting, error: const None());

/// Handles [PairingAuthenticatedAction].
/// Updates [PairingState.phase], [PairingState.bridgeVersion], [PairingState.error].
PairingState pairingAuthenticatedReducer(
  PairingState state,
  PairingAuthenticatedAction action,
) => state.copyWith(
  phase: action.trusted ? PairingPhase.trusted : PairingPhase.unpaired,
  bridgeVersion: Some(action.bridgeVersion),
  error: const None(),
);

/// Handles [PairingCodeRequestedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingCodeRequestedReducer(
  PairingState state,
  PairingCodeRequestedAction action,
) => state.copyWith(phase: PairingPhase.requestingCode, error: const None());

/// Handles [PairingCodeAvailableAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingCodeAvailableReducer(
  PairingState state,
  PairingCodeAvailableAction action,
) => state.copyWith(phase: PairingPhase.awaitingCode, error: const None());

/// Handles [PairingCodeSubmittedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingCodeSubmittedReducer(
  PairingState state,
  PairingCodeSubmittedAction action,
) => state.copyWith(phase: PairingPhase.confirming, error: const None());

/// Handles [PairingConfirmedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingConfirmedReducer(
  PairingState state,
  PairingConfirmedAction action,
) => state.copyWith(phase: PairingPhase.trusted, error: const None());

/// Handles [PairingDisconnectedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingDisconnectedReducer(
  PairingState state,
  PairingDisconnectedAction action,
) => state.copyWith(phase: PairingPhase.disconnected, error: const None());

/// Handles [PairingRevokedAction].
/// Updates [PairingState.phase], [PairingState.error]. Returns to
/// [PairingPhase.unpaired] rather than [PairingPhase.failed] -- a revoked device's own next step
/// is to request a new pairing code, not retry the same dead credential -- reusing the unpaired
/// screen's existing request-code affordance rather than introducing a dedicated phase or widget.
PairingState pairingRevokedReducer(
  PairingState state,
  PairingRevokedAction action,
) => state.copyWith(phase: PairingPhase.unpaired, error: Some(action.message));

/// Handles [PairingFailedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingFailedReducer(
  PairingState state,
  PairingFailedAction action,
) => state.copyWith(phase: PairingPhase.failed, error: Some(action.message));

/// Handles [PairingDisposedAction].
/// Resets [PairingState] to its initial value.
PairingState pairingDisposedReducer(
  PairingState state,
  PairingDisposedAction action,
) => PairingState.initial();
