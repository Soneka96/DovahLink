import 'dart:async';

import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/cancel_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/observe_connection_status.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/confirm_pairing_code.params.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing_renotify.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Handles pairing actions, resolving its use cases through the shared [sl]
/// container.
class PairingMiddleware extends MiddlewareClass<AppState> {
  /// Creates pairing middleware. [reconnectDelay] is the wait before
  /// silently retrying after the bridge is found unreachable; injectable so
  /// tests don't wait in real time.
  PairingMiddleware({this.reconnectDelay = const Duration(seconds: 3)});

  /// Delay before automatically retrying after [PairingDisconnectedAction].
  final Duration reconnectDelay;

  /// The active subscription started by [_pairingSessionTrusted], or `null` before the first
  /// trusted session this middleware instance has observed, or after the session it was watching
  /// was administratively invalidated. Survives navigation away from the pairing screen (see
  /// [_pairingDisposed]'s `wasTrusted` handling) for as long as the session it watches stays
  /// trusted; cancelled and reset to `null` once that session is invalidated, so a later
  /// [PairingSessionTrustedAction] for a new session starts a fresh subscription instead of
  /// reusing one still delivering events for the session that is now gone.
  StreamSubscription<PairingConnectionStatus>? _connectionStatusSubscription;

  /// See [MiddlewareClass.call].
  @override
  void call(Store<AppState> store, dynamic action, NextDispatcher next) {
    next(action);

    switch (action) {
      case PairingStartedAction _:
        _pairingStarted(store, action);
      case PairingCodeRequestedAction _:
        _pairingCodeRequested(store, action);
      case PairingCodeSubmittedAction _:
        _pairingCodeSubmitted(store, action);
      case PairingRenotifyRequestedAction _:
        _pairingRenotifyRequested(store, action);
      case PairingCancelRequestedAction _:
        _pairingCancelRequested(store, action);
      case PairingDisposedAction _:
        _pairingDisposed(store, action);
      case PairingBackRequestedAction _:
        _pairingBackRequested(store, action);
      case PairingSessionTrustedAction _:
        _pairingSessionTrusted(store, action);
    }
  }

  /// Handles [PairingStartedAction] by authenticating through
  /// [AuthenticateUseCase].
  Future<void> _pairingStarted(
    Store<AppState> store,
    PairingStartedAction action,
  ) async {
    (await sl<AuthenticateUseCase>()(NoParams())).fold(
      (Failure failure) {
        if (failure is NetworkFailure) {
          store.dispatch(const PairingDisconnectedAction());
          _scheduleReconnect(store);
        } else {
          store.dispatch(PairingFailedAction(failure.message));
        }
      },
      (PairingHandshakeEntity handshake) {
        store.dispatch(
          PairingAuthenticatedAction(
            bridgeVersion: handshake.bridgeVersion,
            trusted: handshake.trusted,
            credentialRejectedMessage: handshake.credentialRejectedMessage,
          ),
        );
        if (handshake.trusted) {
          store.dispatch(const PairingSessionTrustedAction());
        }
      },
    );
  }

  /// Handles [PairingRenotifyRequestedAction] by requesting redisplay through
  /// [RequestPairingRenotifyUseCase]. Dispatches success, cooldown with
  /// remaining seconds, or failure accordingly.
  Future<void> _pairingRenotifyRequested(
    Store<AppState> store,
    PairingRenotifyRequestedAction action,
  ) async {
    (await sl<RequestPairingRenotifyUseCase>()(NoParams())).fold(
      (Failure failure) {
        store.dispatch(PairingFailedAction(failure.message));
      },
      (int? cooldownSeconds) {
        if (cooldownSeconds == null) {
          store.dispatch(const PairingRenotifySucceededAction());
        } else {
          store.dispatch(
            PairingRenotifyCooldownAction(retryAfterSeconds: cooldownSeconds),
          );
        }
      },
    );
  }

  /// Handles [PairingCancelRequestedAction] by cancelling the active challenge
  /// through [CancelPairingUseCase]. Dispatches success or failure.
  Future<void> _pairingCancelRequested(
    Store<AppState> store,
    PairingCancelRequestedAction action,
  ) async {
    (await sl<CancelPairingUseCase>()(NoParams())).fold(
      (Failure failure) {
        store.dispatch(PairingFailedAction(failure.message));
      },
      (_) {
        store.dispatch(const PairingCancelSucceededAction());
      },
    );
  }

  /// Silently retries [PairingStartedAction] after [reconnectDelay] if the
  /// pairing state is still [PairingPhase.disconnected] -- that check is the
  /// active-generation guard: a dispose or a real reconnect in the meantime
  /// changes the phase, so a stale retry naturally no-ops instead of needing
  /// a separate cancellation token.
  void _scheduleReconnect(Store<AppState> store) {
    Future<void>.delayed(reconnectDelay, () {
      if (PairingSelectors.phaseSelector(store.state) ==
          PairingPhase.disconnected) {
        store.dispatch(const PairingStartedAction());
      }
    });
  }

  /// Handles [PairingCodeRequestedAction] by requesting a pairing challenge
  /// through [RequestPairingUseCase]. Deliberately does not distinguish
  /// [NetworkFailure] here or in [_pairingCodeSubmitted] the way
  /// [_pairingStarted] does: silently discarding a code the user is
  /// mid-entering to retry the initial connect would lose their progress, so
  /// a network hiccup mid-flow surfaces as an ordinary [PairingFailedAction]
  /// instead.
  Future<void> _pairingCodeRequested(
    Store<AppState> store,
    PairingCodeRequestedAction action,
  ) async {
    (await sl<RequestPairingUseCase>()(NoParams())).fold(
      (Failure failure) {
        store.dispatch(PairingFailedAction(failure.message));
      },
      (int? expiresInSeconds) {
        store.dispatch(
          PairingCodeAvailableAction(expiresInSeconds: expiresInSeconds),
        );
      },
    );
  }

  /// Handles [PairingCodeSubmittedAction] by confirming the code through
  /// [ConfirmPairingCodeUseCase].
  Future<void> _pairingCodeSubmitted(
    Store<AppState> store,
    PairingCodeSubmittedAction action,
  ) async {
    (await sl<ConfirmPairingCodeUseCase>()(
      ConfirmPairingCodeParams(
        code: action.code,
        displayName: action.displayName,
      ),
    )).fold(
      (Failure failure) {
        // A wrong code or a too-soon retry stays on the same still-active challenge with an
        // inline mistake message; everything else (expired, hard_limit_reached, other transport
        // failures) ends the flow.
        if (failure is PairingRetriableFailure) {
          store.dispatch(
            PairingConfirmFailedWithAttemptsRemainingAction(
              message: failure.message,
            ),
          );
        } else {
          store.dispatch(PairingFailedAction(failure.message));
        }
      },
      (_) {
        store.dispatch(const PairingConfirmedAction());
        store.dispatch(const PairingSessionTrustedAction());
      },
    );
  }

  /// Handles [PairingDisposedAction] by disconnecting through
  /// [DisconnectUseCase], unless [PairingDisposedAction.wasTrusted] -- pairing
  /// had already succeeded, so the established trust and connection are kept
  /// rather than torn down on the way out. Otherwise, best-effort cleanup:
  /// the reducer has already reset [AppState.pairing] by the time this runs,
  /// and there is no surviving screen to report a disconnect failure to.
  Future<void> _pairingDisposed(
    Store<AppState> store,
    PairingDisposedAction action,
  ) async {
    if (action.wasTrusted) {
      return;
    }
    await sl<DisconnectUseCase>()(NoParams());
  }

  /// Handles [PairingBackRequestedAction] by navigating to home.
  void _pairingBackRequested(
    Store<AppState> store,
    PairingBackRequestedAction action,
  ) {
    sl<NavigatorService>().go(AppRoutes.home);
  }

  /// Handles [PairingSessionTrustedAction] by starting [_connectionStatusSubscription] through
  /// [ObserveConnectionStatusUseCase], unless one is already running -- a later reconnect or
  /// re-pair dispatching this action again must not stack a second subscription onto the same
  /// underlying SDK stream. Dispatches by status: [PairingConnectionStatus.lost] as ordinary
  /// transport loss ([PairingDisconnectedAction], distinct from a rejected pairing attempt), and
  /// [PairingConnectionStatus.restored] as recovery ([PairingConnectionRestoredAction]) -- both
  /// meaningful only while the session this subscription was started for is still the trusted one.
  /// [PairingConnectionStatus.invalidated] ends that session, so alongside dispatching
  /// [PairingFailedAction] (carrying [SessionInvalidatedFailure.administrative]'s reason-agnostic
  /// message) it also cancels and clears [_connectionStatusSubscription]: without that, this same
  /// subscription would keep delivering [PairingConnectionStatus.lost]/`restored` events raised by
  /// an unrelated later pairing attempt's own connect/reconnect cycle, and a stray `restored` would
  /// have the reducer falsely report the new attempt as trusted.
  void _pairingSessionTrusted(
    Store<AppState> store,
    PairingSessionTrustedAction action,
  ) {
    if (_connectionStatusSubscription != null) {
      return;
    }
    _connectionStatusSubscription =
        sl<ObserveConnectionStatusUseCase>()(NoParams()).listen((
          PairingConnectionStatus status,
        ) {
          switch (status) {
            case PairingConnectionStatus.lost:
              store.dispatch(const PairingDisconnectedAction());
            case PairingConnectionStatus.restored:
              store.dispatch(const PairingConnectionRestoredAction());
            case PairingConnectionStatus.invalidated:
              unawaited(_connectionStatusSubscription?.cancel());
              _connectionStatusSubscription = null;
              store.dispatch(
                PairingFailedAction(
                  SessionInvalidatedFailure.administrative.message,
                ),
              );
          }
        });
  }
}
