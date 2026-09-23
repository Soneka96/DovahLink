import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_rejection_classifier.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Defines the callback that starts bounded recovery after ordinary transport loss.
abstract interface class IReconnectService {
  /// Reports that ordinary transport loss finished tearing down the connection previously
  /// established at [uri], starting bounded automatic recovery.
  void onOrdinaryTransportLoss(Uri uri);
}

/// Reconnects to the endpoint the session last connected to and re-authenticates, up to a bounded
/// attempt budget and a hard overall deadline (each defaulting to the centrally tuned
/// [kReconnectAttemptDelays]/[kReconnectDeadline]) -- whichever is exhausted first.
/// [SessionService] owns transport/session state and teardown; [AuthenticationService] owns
/// authentication. This class only chooses when and how often to retry. Incompatible Host versions
/// are terminal and their typed failure is passed to teardown without another attempt.
class ReconnectService implements IReconnectService {
  /// Reconnects to and disconnects from the Host, and reports live connection state.
  final ISessionService _sessionService;

  /// Re-authenticates the reconnected transport, admitting a fresh session on success.
  final IAuthenticationService _authenticationService;

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

  /// Creates a reconnect service recovering through [sessionService], re-authenticating through
  /// [authenticationService].
  ReconnectService({
    required ISessionService sessionService,
    required IAuthenticationService authenticationService,
    List<Duration> attemptDelays = kReconnectAttemptDelays,
    Duration deadline = kReconnectDeadline,
    DateTime Function() now = DateTime.now,
  }) : _sessionService = sessionService,
       _authenticationService = authenticationService,
       _attemptDelays = attemptDelays,
       _deadline = deadline,
       _now = now;

  /// Implements [IReconnectService.onOrdinaryTransportLoss].
  @override
  void onOrdinaryTransportLoss(Uri uri) {
    unawaited(_recover(uri));
  }

  /// Attempts one recovery for each [ReconnectService._attemptDelays] entry, stopping at
  /// [ReconnectService._deadline]. Retryable protocol and transport failures consume an attempt;
  /// [ReconnectRejectionClassifier.isTerminal] failures stop immediately. For
  /// [CredentialRejectionReason.revoked] or [CredentialRejectionReason.blocked] credentials,
  /// forgets the credential before stopping. If an explicit disconnect or
  /// invalidation already moved the session out of [DovahLinkConnectionState.reconnecting], leaves
  /// that teardown alone. On exhaustion or terminal failure, disconnects so orphaned operations
  /// preserved during recovery are failed.
  Future<void> _recover(Uri uri) async {
    final DateTime deadline = _now().add(_deadline);
    Exception? terminalFailure;
    for (int attempt = 0; attempt < _attemptDelays.length; attempt++) {
      if (attempt > 0) {
        final Duration untilDeadline = deadline.difference(_now());
        if (untilDeadline <= Duration.zero) {
          break;
        }
        final Duration delay = _attemptDelays[attempt] < untilDeadline
            ? _attemptDelays[attempt]
            : untilDeadline;
        await Future<void>.delayed(delay);
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
        await _authenticationService.hello();
        return;
      } on DovahLinkProtocolException catch (error) {
        if (ReconnectRejectionClassifier.isTerminal(error)) {
          if (CredentialRejectionReason.fromProtocolErrorCode(error.code) !=
              null) {
            try {
              await _authenticationService.forgetCredential();
            } on Object {
              // Best-effort cleanup must not prevent the recovery cycle from finalizing with a
              // disconnect. A later explicit authentication can retry this cleanup.
            }
          }
          break;
        }
        continue;
      } on DovahLinkCompatibilityException catch (error) {
        terminalFailure = error;
        break;
      } on Object {
        continue;
      }
    }
    await _sessionService.disconnect(
      reason:
          terminalFailure ??
          const DovahLinkConnectionException(
            'Reconnect could not restore the connection.',
          ),
    );
  }
}
