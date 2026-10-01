import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_rejection_classifier.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns separate initial-connection retry and established-session recovery policies.
abstract interface class IReconnectService {
  /// Emits the current initial-retry status on listen and each lifecycle transition.
  Stream<DovahLinkInitialConnectionRetryStatus>
  get initialConnectionRetryChanges;

  /// Runs one explicit authentication attempt, then retries connection/protocol failures every
  /// three seconds until success, cancellation, or administrative invalidation.
  /// @param attempt Authenticates the originally selected candidate or Known Host.
  /// @return The successful initial handshake, from the first attempt or a retry.
  Future<HelloResult> connectWithInitialRetry(
    Future<HelloResult> Function() attempt,
  );

  /// Cancels an initial retry without affecting bounded established-session recovery.
  void stopInitialConnectionRetry();

  /// Reports that ordinary transport loss finished tearing down the connection previously
  /// established at [uri], starting bounded automatic recovery.
  /// @param uri The last endpoint used by the interrupted session.
  /// @param knownHostId The relationship selected for the lost session, or `null` for a candidate.
  void onOrdinaryTransportLoss(Uri uri, [DovahLinkHostId? knownHostId]);

  /// Stops the active recovery cycle without preventing a later cycle from starting.
  void stopRecovery();
}

/// Owns initial retries for one explicit connection attempt and bounded recovery of an established
/// session. Initial retries retain the selected attempt callback and wait three seconds between
/// connection/protocol failures until cancelled or successful; established recovery instead has a
/// bounded attempt budget and deadline. Both policies use the same [SessionService] and
/// [AuthenticationService]. This class never owns session state or transport operations.
class ReconnectService implements IReconnectService {
  /// The typed result for an initial attempt cancelled before it can finish.
  static const DovahLinkConnectionException _initialRetryCancelled =
      DovahLinkConnectionException(
        'The initial connection retry was cancelled.',
      );

  /// The typed initial-retry lifecycle state.
  final CurrentValueStream<DovahLinkInitialConnectionRetryStatus>
  _initialConnectionRetryState =
      CurrentValueStream<DovahLinkInitialConnectionRetryStatus>(
        DovahLinkInitialConnectionRetryStatus.inactive,
      );

  /// Reconnects to and disconnects from the Host, and reports live connection state.
  final ISessionService _sessionService;

  /// Re-authenticates the reconnected transport, admitting a fresh session on success.
  final IAuthenticationService _authenticationService;

  /// Reports the final availability transition for the Host owned by a recovery cycle.
  final IHostAvailabilityService _hostAvailabilityService;

  /// The delay before each attempt after the first, and the attempt budget. Defaults to the
  /// centrally tuned [kReconnectAttemptDelays]; overridable so a test can exercise the retry loop
  /// with millisecond-scale delays instead of real seconds.
  final List<Duration> _attemptDelays;

  /// The hard overall deadline for one recovery cycle. Defaults to [kReconnectDeadline] and is
  /// configurable for tests alongside [ReconnectService._attemptDelays].
  final Duration _deadline;

  /// The clock the deadline is measured against. Defaults to [DateTime.now]; overridable so a
  /// test can advance time deterministically instead of depending on real elapsed time, which a
  /// heavily loaded test run could otherwise make flaky.
  final DateTime Function() _now;

  /// Optional deterministic retry ticks for tests; production uses the configured delay.
  final Stream<void>? _initialConnectionRetryTicks;

  /// Delay retained from the former Flutter initial-retry policy.
  final Duration _initialConnectionRetryDelay;

  /// Generation identifying the current recovery cycle.
  int _recoveryGeneration = 0;

  /// Timer waiting for the next recovery attempt, when one is pending.
  Timer? _retryTimer;

  /// Completer released when the pending retry delay ends or is cancelled.
  Completer<void>? _retryDelayCompleter;

  /// Generation invalidating an initial connection retry intent.
  int _initialConnectionRetryGeneration = 0;

  /// Timer waiting before the next initial connection retry.
  Timer? _initialConnectionRetryTimer;

  /// Completer released when an initial retry delay ends or is cancelled.
  Completer<void>? _initialConnectionRetryDelayCompleter;

  /// Test tick subscription for the current initial retry delay.
  StreamSubscription<void>? _initialConnectionRetryTickSubscription;

  /// Whether an automatic initial retry is currently authenticating.
  bool _isInitialConnectionRetryAttemptInFlight = false;

  /// Creates a reconnect service recovering through [sessionService], re-authenticating through
  /// [authenticationService], and reporting outcomes to [hostAvailabilityService].
  /// @param sessionService Owns the connection lifecycle.
  /// @param authenticationService Re-authenticates each restored transport.
  /// @param hostAvailabilityService Owns runtime Known Host availability.
  ReconnectService({
    required ISessionService sessionService,
    required IAuthenticationService authenticationService,
    required IHostAvailabilityService hostAvailabilityService,
    List<Duration> attemptDelays = kReconnectAttemptDelays,
    Duration deadline = kReconnectDeadline,
    DateTime Function() now = DateTime.now,
    Duration initialConnectionRetryDelay = kInitialConnectionRetryDelay,
    Stream<void>? initialConnectionRetryTicks,
  }) : _sessionService = sessionService,
       _authenticationService = authenticationService,
       _hostAvailabilityService = hostAvailabilityService,
       _attemptDelays = attemptDelays,
       _deadline = deadline,
       _now = now,
       _initialConnectionRetryDelay = initialConnectionRetryDelay,
       _initialConnectionRetryTicks = initialConnectionRetryTicks;

  /// Implements [IReconnectService.initialConnectionRetryChanges].
  @override
  Stream<DovahLinkInitialConnectionRetryStatus>
  get initialConnectionRetryChanges => _initialConnectionRetryState.stream;

  /// Implements [IReconnectService.connectWithInitialRetry]. Established-session recovery stays
  /// in [_recover] with its own bounded attempt budget and deadline.
  @override
  Future<HelloResult> connectWithInitialRetry(
    Future<HelloResult> Function() attempt,
  ) async {
    stopInitialConnectionRetry();
    stopRecovery();
    _authenticationService.cancelPendingAuthentication();
    final int generation = _initialConnectionRetryGeneration;

    try {
      final HelloResult result = await attempt();
      if (generation != _initialConnectionRetryGeneration) {
        throw _initialRetryCancelled;
      }
      return result;
    } on DovahLinkConnectionException {
      if (generation != _initialConnectionRetryGeneration ||
          _sessionService.connectionState ==
              DovahLinkConnectionState.administrativelyInvalidated) {
        rethrow;
      }
    } on DovahLinkProtocolException {
      if (generation != _initialConnectionRetryGeneration ||
          _sessionService.connectionState ==
              DovahLinkConnectionState.administrativelyInvalidated) {
        rethrow;
      }
    }

    _initialConnectionRetryState.update(
      DovahLinkInitialConnectionRetryStatus.retrying,
    );
    try {
      while (generation == _initialConnectionRetryGeneration) {
        final Completer<void> delayCompleter = Completer<void>();
        _initialConnectionRetryDelayCompleter = delayCompleter;
        final Stream<void>? retryTicks = _initialConnectionRetryTicks;
        if (retryTicks == null) {
          _initialConnectionRetryTimer = Timer(
            _initialConnectionRetryDelay,
            () {
              if (identical(
                _initialConnectionRetryDelayCompleter,
                delayCompleter,
              )) {
                _initialConnectionRetryDelayCompleter = null;
                _initialConnectionRetryTimer = null;
              }
              if (!delayCompleter.isCompleted) {
                delayCompleter.complete();
              }
            },
          );
        } else {
          _initialConnectionRetryTickSubscription = retryTicks.listen((_) {
            if (identical(
              _initialConnectionRetryDelayCompleter,
              delayCompleter,
            )) {
              _initialConnectionRetryDelayCompleter = null;
              final StreamSubscription<void>? subscription =
                  _initialConnectionRetryTickSubscription;
              _initialConnectionRetryTickSubscription = null;
              unawaited(subscription?.cancel());
            }
            if (!delayCompleter.isCompleted) {
              delayCompleter.complete();
            }
          });
        }
        await delayCompleter.future;
        if (generation != _initialConnectionRetryGeneration) {
          throw _initialRetryCancelled;
        }
        if (_sessionService.connectionState ==
            DovahLinkConnectionState.administrativelyInvalidated) {
          throw const DovahLinkConnectionException(
            'The Host administratively invalidated the session.',
          );
        }
        _isInitialConnectionRetryAttemptInFlight = true;
        try {
          try {
            final HelloResult result = await attempt();
            if (generation != _initialConnectionRetryGeneration) {
              throw _initialRetryCancelled;
            }
            return result;
          } on DovahLinkConnectionException {
            if (generation != _initialConnectionRetryGeneration) {
              throw _initialRetryCancelled;
            }
            if (_sessionService.connectionState ==
                DovahLinkConnectionState.administrativelyInvalidated) {
              rethrow;
            }
          } on DovahLinkProtocolException {
            if (generation != _initialConnectionRetryGeneration) {
              throw _initialRetryCancelled;
            }
            if (_sessionService.connectionState ==
                DovahLinkConnectionState.administrativelyInvalidated) {
              rethrow;
            }
          }
        } finally {
          _isInitialConnectionRetryAttemptInFlight = false;
        }
      }
      throw _initialRetryCancelled;
    } finally {
      if (generation == _initialConnectionRetryGeneration) {
        _initialConnectionRetryState.update(
          DovahLinkInitialConnectionRetryStatus.inactive,
        );
      }
    }
  }

  /// Implements [IReconnectService.stopInitialConnectionRetry].
  @override
  void stopInitialConnectionRetry() {
    _initialConnectionRetryGeneration++;
    if (_isInitialConnectionRetryAttemptInFlight) {
      _authenticationService.cancelPendingAuthentication();
    }
    _initialConnectionRetryTimer?.cancel();
    _initialConnectionRetryTimer = null;
    final StreamSubscription<void>? tickSubscription =
        _initialConnectionRetryTickSubscription;
    _initialConnectionRetryTickSubscription = null;
    unawaited(tickSubscription?.cancel());
    final Completer<void>? delayCompleter =
        _initialConnectionRetryDelayCompleter;
    _initialConnectionRetryDelayCompleter = null;
    if (delayCompleter != null && !delayCompleter.isCompleted) {
      delayCompleter.complete();
    }
    _initialConnectionRetryState.update(
      DovahLinkInitialConnectionRetryStatus.inactive,
    );
  }

  /// Implements [IReconnectService.onOrdinaryTransportLoss].
  @override
  void onOrdinaryTransportLoss(Uri uri, [DovahLinkHostId? knownHostId]) {
    _cancelRetryDelay();
    final int recoveryGeneration = ++_recoveryGeneration;
    unawaited(_recover(uri, knownHostId, recoveryGeneration));
  }

  /// Implements [IReconnectService.stopRecovery].
  @override
  void stopRecovery() {
    _recoveryGeneration++;
    _cancelRetryDelay();
  }

  /// Attempts one recovery for each [ReconnectService._attemptDelays] entry, stopping at
  /// [ReconnectService._deadline]. Retryable protocol and transport failures consume an attempt;
  /// [ReconnectRejectionClassifier.isTerminal] protocol failures and Host identity mismatches stop
  /// immediately. For
  /// [CredentialRejectionReason.revoked] or [CredentialRejectionReason.blocked] credentials,
  /// forgets the credential before stopping. If an explicit disconnect or
  /// invalidation already moved the session out of [DovahLinkConnectionState.reconnecting], leaves
  /// that teardown alone. On exhaustion or terminal failure, disconnects so orphaned operations
  /// preserved during recovery are failed.
  /// Runs the bounded recovery cycle while [recoveryGeneration] is still current.
  /// @param uri The last endpoint used by the interrupted session.
  /// @param knownHostId The authenticated Known Host relationship for this cycle, if any.
  /// @param recoveryGeneration The generation that invalidates this cycle when recovery is stopped.
  Future<void> _recover(
    Uri uri,
    DovahLinkHostId? knownHostId,
    int recoveryGeneration,
  ) async {
    final DateTime deadline = _now().add(_deadline);
    Exception? terminalFailure;
    DovahLinkHostAvailability giveUpAvailability =
        DovahLinkHostAvailability.unknown;
    for (int attempt = 0; attempt < _attemptDelays.length; attempt++) {
      if (attempt > 0) {
        final Duration untilDeadline = deadline.difference(_now());
        if (untilDeadline <= Duration.zero) {
          break;
        }
        final Duration delay = _attemptDelays[attempt] < untilDeadline
            ? _attemptDelays[attempt]
            : untilDeadline;
        await _waitForRetry(delay);
        if (recoveryGeneration != _recoveryGeneration) {
          return;
        }
      }
      if (_sessionService.connectionState !=
          DovahLinkConnectionState.reconnecting) {
        return;
      }
      if (_now().isAfter(deadline)) {
        break;
      }
      try {
        await _sessionService.connect(uri);
        if (recoveryGeneration != _recoveryGeneration) {
          return;
        }
        await _authenticationService.helloLastKnownHost();
        if (recoveryGeneration != _recoveryGeneration ||
            _sessionService.connectionState !=
                DovahLinkConnectionState.connected) {
          return;
        }
        if (knownHostId != null) {
          _hostAvailabilityService.setAvailability(
            knownHostId,
            DovahLinkHostAvailability.online,
          );
        }
        return;
      } on DovahLinkConnectionException catch (error) {
        if (recoveryGeneration != _recoveryGeneration) {
          return;
        }
        terminalFailure = error;
        giveUpAvailability = DovahLinkHostAvailability.offline;
        continue;
      } on DovahLinkProtocolException catch (error) {
        if (recoveryGeneration != _recoveryGeneration) {
          return;
        }
        terminalFailure = error;
        giveUpAvailability = DovahLinkHostAvailability.unknown;
        if (ReconnectRejectionClassifier.isTerminal(error)) {
          if (CredentialRejectionReason.fromProtocolErrorCode(error.code) !=
              null) {
            try {
              await _authenticationService.forgetLastKnownCredential();
            } on Object {
              // Best-effort cleanup must not prevent the recovery cycle from finalizing with a
              // disconnect. A later explicit authentication can retry this cleanup.
            }
          }
          break;
        }
        continue;
      } on DovahLinkCompatibilityException catch (error) {
        if (recoveryGeneration != _recoveryGeneration) {
          return;
        }
        terminalFailure = error;
        giveUpAvailability = DovahLinkHostAvailability.unknown;
        break;
      } on DovahLinkHostIdentityMismatchException catch (error) {
        if (recoveryGeneration != _recoveryGeneration) {
          return;
        }
        terminalFailure = error;
        giveUpAvailability = DovahLinkHostAvailability.unknown;
        break;
      } on Object catch (error) {
        if (recoveryGeneration != _recoveryGeneration) {
          return;
        }
        if (error is Exception) {
          terminalFailure = error;
        }
        giveUpAvailability = DovahLinkHostAvailability.unknown;
        continue;
      }
    }
    if (recoveryGeneration != _recoveryGeneration) {
      return;
    }
    if (_sessionService.connectionState ==
        DovahLinkConnectionState.administrativelyInvalidated) {
      return;
    }
    await _sessionService.disconnect(
      reason:
          terminalFailure ??
          const DovahLinkConnectionException(
            'Reconnect could not restore the connection.',
          ),
    );
    if (recoveryGeneration != _recoveryGeneration ||
        _sessionService.connectionState ==
            DovahLinkConnectionState.administrativelyInvalidated) {
      return;
    }
    if (knownHostId != null) {
      _hostAvailabilityService.setAvailability(knownHostId, giveUpAvailability);
    }
  }

  /// Waits for [delay], allowing [stopRecovery] to release the pending wait immediately.
  /// @param delay The bounded wait before the next connection attempt.
  Future<void> _waitForRetry(Duration delay) {
    final Completer<void> completer = Completer<void>();
    _retryDelayCompleter = completer;
    _retryTimer = Timer(delay, () {
      if (identical(_retryDelayCompleter, completer)) {
        _retryDelayCompleter = null;
        _retryTimer = null;
      }
      completer.complete();
    });
    return completer.future;
  }

  /// Cancels a pending retry timer and releases its waiting recovery cycle.
  void _cancelRetryDelay() {
    _retryTimer?.cancel();
    _retryTimer = null;
    final Completer<void>? completer = _retryDelayCompleter;
    _retryDelayCompleter = null;
    if (completer != null && !completer.isCompleted) {
      completer.complete();
    }
  }
}
