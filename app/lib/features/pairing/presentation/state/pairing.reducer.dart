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

  TypedReducer<PairingState, PairingRenotifyAlreadyIdleAction>(
    pairingRenotifyAlreadyIdleReducer,
  ).call,

  TypedReducer<PairingState, PairingCancelSucceededAction>(
    pairingCancelSucceededReducer,
  ).call,

  TypedReducer<PairingState, PairingConfirmFailedWithAttemptsRemainingAction>(
    pairingConfirmFailedWithAttemptsRemainingReducer,
  ).call,

  TypedReducer<PairingState, PairingConnectionRestoredAction>(
    pairingConnectionRestoredReducer,
  ).call,
]);

/// Handles [PairingStartedAction] by entering the explicit connection phase.
PairingState pairingStartedReducer(
  PairingState state,
  PairingStartedAction action,
) => state.copyWith(
  phase: PairingPhase.connecting,
  error: const None(),
  isRenotifyPending: false,
  credentialRejectionReason: const None(),
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
  renotifyOutcome: const None(),
);

/// Handles [PairingAuthenticatedAction].
/// Updates [PairingState.phase], [PairingState.hostVersion], [PairingState.error], and
/// [PairingState.credentialRejectionReason]. Carries a rejected credential's typed reason and
/// safe copy into state for presentation.
PairingState pairingAuthenticatedReducer(
  PairingState state,
  PairingAuthenticatedAction action,
) => state.copyWith(
  phase: action.trusted ? PairingPhase.trusted : PairingPhase.unpaired,
  hostVersion: Some(action.hostVersion),
  credentialRejectionReason:
      action.trusted || action.credentialRejectionReason == null
      ? const None()
      : Some(action.credentialRejectionReason!),
  error: action.credentialRejectedMessage == null
      ? const None()
      : Some(action.credentialRejectedMessage!),
);

/// Handles [PairingCodeRequestedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingCodeRequestedReducer(
  PairingState state,
  PairingCodeRequestedAction action,
) => state.copyWith(
  phase: PairingPhase.requestingCode,
  error: const None(),
  credentialRejectionReason: const None(),
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
);

/// Handles [PairingCodeAvailableAction].
/// Updates [PairingState.phase], [PairingState.error], and code timing. Clears redisplay state
/// unconditionally because a fresh or re-queried challenge invalidates the previous challenge's
/// cooldown and pending request.
PairingState pairingCodeAvailableReducer(
  PairingState state,
  PairingCodeAvailableAction action,
) => state.copyWith(
  phase: PairingPhase.awaitingCode,
  error: const None(),
  isRenotifyPending: false,
  codeExpiresAt: action.expiresInSeconds == null
      ? const None()
      : Some(DateTime.now().add(Duration(seconds: action.expiresInSeconds!))),
  renotifyAvailableAt: const None(),
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
  renotifyOutcome: const None(),
);

/// Handles [PairingCodeSubmittedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingCodeSubmittedReducer(
  PairingState state,
  PairingCodeSubmittedAction action,
) => state.copyWith(
  phase: PairingPhase.confirming,
  error: const None(),
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
  renotifyOutcome: const None(),
);

/// Handles [PairingConfirmedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingConfirmedReducer(
  PairingState state,
  PairingConfirmedAction action,
) => state.copyWith(
  phase: PairingPhase.trusted,
  error: const None(),
  credentialRejectionReason: const None(),
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
);

/// Handles [PairingDisconnectedAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingDisconnectedReducer(
  PairingState state,
  PairingDisconnectedAction action,
) => state.copyWith(phase: PairingPhase.disconnected, error: const None());

/// Handles [PairingFailedAction].
/// Updates [PairingState.phase], [PairingState.error], and clears challenge timing and redisplay
/// state when leaving the pairing flow.
PairingState pairingFailedReducer(
  PairingState state,
  PairingFailedAction action,
) => state.copyWith(
  phase: PairingPhase.failed,
  error: action.pairingOutcome == null ? Some(action.message) : const None(),
  codeExpiresAt: const None(),
  renotifyAvailableAt: const None(),
  isRenotifyPending: false,
  credentialRejectionReason: const None(),
  pairingOutcome: action.pairingOutcome == null
      ? const None()
      : Some(action.pairingOutcome!),
  attemptsRemaining: action.attemptsRemaining == null
      ? const None()
      : Some(action.attemptsRemaining!),
);

/// Handles [PairingDisposedAction].
/// Resets [PairingState] to its initial value.
PairingState pairingDisposedReducer(
  PairingState state,
  PairingDisposedAction action,
) => PairingState.initial(support: state.support);

/// Handles [PairingRenotifyRequestedAction].
/// Stays in [PairingPhase.awaitingCode], clears prior redisplay status, and marks the request pending.
PairingState pairingRenotifyRequestedReducer(
  PairingState state,
  PairingRenotifyRequestedAction action,
) => state.copyWith(
  error: const None(),
  isRenotifyPending: true,
  renotifyOutcome: const None(),
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
);

/// Handles [PairingRenotifySucceededAction].
/// Stays in [PairingPhase.awaitingCode] and stores successful redisplay plus its retry interval.
PairingState pairingRenotifySucceededReducer(
  PairingState state,
  PairingRenotifySucceededAction action,
) => state.copyWith(
  error: const None(),
  isRenotifyPending: false,
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
  renotifyOutcome: const Some(PairingRenotifyOutcome.renotified),
  renotifyAvailableAt: action.retryAfterSeconds == null
      ? const None()
      : Some(DateTime.now().add(Duration(seconds: action.retryAfterSeconds!))),
);

/// Handles [PairingRenotifyCooldownAction].
/// Stores the cooldown response and its retry time.
/// Stays in [PairingPhase.awaitingCode].
PairingState pairingRenotifyCooldownReducer(
  PairingState state,
  PairingRenotifyCooldownAction action,
) => state.copyWith(
  isRenotifyPending: false,
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
  renotifyOutcome: const Some(PairingRenotifyOutcome.cooldown),
  renotifyAvailableAt: action.retryAfterSeconds == null
      ? const None()
      : Some(DateTime.now().add(Duration(seconds: action.retryAfterSeconds!))),
);

/// Handles [PairingRenotifyAlreadyIdleAction].
/// Ends code entry because the Host no longer owns a challenge.
PairingState pairingRenotifyAlreadyIdleReducer(
  PairingState state,
  PairingRenotifyAlreadyIdleAction action,
) => state.copyWith(
  phase: PairingPhase.failed,
  error: const None(),
  codeExpiresAt: const None(),
  renotifyAvailableAt: const None(),
  isRenotifyPending: false,
  renotifyOutcome: const Some(PairingRenotifyOutcome.alreadyIdle),
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
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
  isRenotifyPending: false,
  credentialRejectionReason: const None(),
  pairingOutcome: const None(),
  attemptsRemaining: const None(),
  renotifyOutcome: const None(),
);

/// Handles [PairingConfirmFailedWithAttemptsRemainingAction].
/// Returns to [PairingPhase.awaitingCode] (the real predecessor is
/// [PairingPhase.confirming], set by [PairingCodeSubmittedAction] -- this reducer must set the
/// phase explicitly rather than relying on it already being [PairingPhase.awaitingCode]), sets
/// inline error message.
PairingState pairingConfirmFailedWithAttemptsRemainingReducer(
  PairingState state,
  PairingConfirmFailedWithAttemptsRemainingAction action,
) => state.copyWith(
  phase: PairingPhase.awaitingCode,
  error: const None(),
  pairingOutcome: Some(action.pairingOutcome),
  attemptsRemaining: action.attemptsRemaining == null
      ? const None()
      : Some(action.attemptsRemaining!),
);

/// Handles [PairingConnectionRestoredAction].
/// Updates [PairingState.phase], [PairingState.error].
PairingState pairingConnectionRestoredReducer(
  PairingState state,
  PairingConnectionRestoredAction action,
) => state.copyWith(
  phase: PairingPhase.trusted,
  error: const None(),
  credentialRejectionReason: const None(),
);
