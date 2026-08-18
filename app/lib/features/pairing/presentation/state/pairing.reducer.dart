import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Reduces pairing actions into [PairingState].
Reducer<PairingState> pairingReducer = combineReducers<PairingState>([
  TypedReducer<PairingState, PairingStartedAction>(pairingStartedReducer).call,

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

  TypedReducer<PairingState, PairingFailedAction>(pairingFailedReducer).call,

  TypedReducer<PairingState, PairingDisposedAction>(
    pairingDisposedReducer,
  ).call,

  TypedReducer<PairingState, PairingRenotifyRequestedAction>(
    pairingRenotifyRequestedReducer,
  ).call,

  TypedReducer<PairingState, PairingRenotifySucceededAction>(
    pairingRenotifySucceededReducer,
  ).call,

  TypedReducer<PairingState, PairingRenotifyCooldownAction>(
    pairingRenotifyCooldownReducer,
  ).call,

  TypedReducer<PairingState, PairingCancelSucceededAction>(
    pairingCancelSucceededReducer,
  ).call,

  TypedReducer<PairingState, PairingConfirmFailedWithAttemptsRemainingAction>(
    pairingConfirmFailedWithAttemptsRemainingReducer,
  ).call,
]);

/// Handles [PairingStartedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingStartedReducer(
  PairingState state,
  PairingStartedAction action,
) => state.copyWith(phase: PairingPhase.connecting, error: const None());

/// Handles [PairingAuthenticatedAction].
/// Updates [PairingState.phase], [PairingState.bridgeVersion], [PairingState.error]. Carries
/// [PairingAuthenticatedAction.credentialRejectedMessage] through as the error text when set, so
/// a session recovered from a rejected credential can still explain why to the user.
PairingState pairingAuthenticatedReducer(
  PairingState state,
  PairingAuthenticatedAction action,
) => state.copyWith(
  phase: action.trusted ? PairingPhase.trusted : PairingPhase.unpaired,
  bridgeVersion: Some(action.bridgeVersion),
  error: action.credentialRejectedMessage == null
      ? const None()
      : Some(action.credentialRejectedMessage!),
);

/// Handles [PairingCodeRequestedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingCodeRequestedReducer(
  PairingState state,
  PairingCodeRequestedAction action,
) => state.copyWith(phase: PairingPhase.requestingCode, error: const None());

/// Handles [PairingCodeAvailableAction].
/// Updates [PairingState.phase], [PairingState.error], [PairingState.codeExpiresAt].
PairingState pairingCodeAvailableReducer(
  PairingState state,
  PairingCodeAvailableAction action,
) => state.copyWith(
  phase: PairingPhase.awaitingCode,
  error: const None(),
  codeExpiresAt: action.expiresInSeconds == null
      ? const None()
      : Some(DateTime.now().add(Duration(seconds: action.expiresInSeconds!))),
);

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

/// Handles [PairingRenotifyRequestedAction].
/// Stays in [PairingPhase.awaitingCode], clears error.
PairingState pairingRenotifyRequestedReducer(
  PairingState state,
  PairingRenotifyRequestedAction action,
) => state.copyWith(error: const None());

/// Handles [PairingRenotifySucceededAction].
/// Stays in [PairingPhase.awaitingCode], clears error.
PairingState pairingRenotifySucceededReducer(
  PairingState state,
  PairingRenotifySucceededAction action,
) => state.copyWith(error: const None());

/// Handles [PairingRenotifyCooldownAction].
/// Sets [PairingState.renotifyAvailableAt] to the computed retry time.
/// Stays in [PairingPhase.awaitingCode].
PairingState pairingRenotifyCooldownReducer(
  PairingState state,
  PairingRenotifyCooldownAction action,
) => state.copyWith(
  renotifyAvailableAt: Some(
    DateTime.now().add(Duration(seconds: action.retryAfterSeconds)),
  ),
);

/// Handles [PairingCancelSucceededAction].
/// Transitions to [PairingPhase.failed] to exit the pairing flow.
PairingState pairingCancelSucceededReducer(
  PairingState state,
  PairingCancelSucceededAction action,
) => state.copyWith(
  phase: PairingPhase.failed,
  error: const Some('Pairing cancelled.'),
  codeExpiresAt: const None(),
  renotifyAvailableAt: const None(),
);

/// Handles [PairingConfirmFailedWithAttemptsRemainingAction].
/// Stays in [PairingPhase.awaitingCode], sets inline error message.
PairingState pairingConfirmFailedWithAttemptsRemainingReducer(
  PairingState state,
  PairingConfirmFailedWithAttemptsRemainingAction action,
) => state.copyWith(error: Some(action.message));
