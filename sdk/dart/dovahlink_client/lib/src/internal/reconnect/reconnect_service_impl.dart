import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_rejection_classifier.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Implements [ReconnectService], per `ai/context/sdk/architecture.md`'s "Internal composition".
/// Reconnects to the endpoint the session last connected to and re-authenticates, up to a bounded
/// attempt budget and a hard overall deadline (each defaulting to the centrally tuned
/// [kReconnectAttemptDelays]/[kReconnectDeadline]) -- whichever is exhausted first.
/// `SessionServiceImpl` continues to own transport/session state and teardown, and
/// `AuthenticationServiceImpl` continues to own authentication; this class only orchestrates when
/// and how often to retry both. [sessionService] and [authenticationService] are supplied by the
/// caller per `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never
/// constructs one of its own dependencies.
class ReconnectServiceImpl implements ReconnectService {
  /// Reconnects to and disconnects from the bridge, and reports live connection state.
  final SessionService _sessionService;

  /// Re-authenticates the reconnected transport, admitting a fresh session on success.
  final AuthenticationService _authenticationService;

  /// The delay before each attempt after the first, and the attempt budget. Defaults to the
  /// centrally tuned [kReconnectAttemptDelays]; overridable so a test can exercise the retry loop
  /// with millisecond-scale delays instead of real seconds.
  final List<Duration> _attemptDelays;

  /// The hard overall deadline for one recovery cycle. Defaults to the centrally tuned
  /// [kReconnectDeadline]; overridable for the same reason as [_attemptDelays].
  final Duration _deadline;

  /// The clock the deadline is measured against. Defaults to [DateTime.now]; overridable so a
  /// test can advance time deterministically instead of depending on real elapsed time, which a
  /// heavily loaded test run could otherwise make flaky.
  final DateTime Function() _now;

  /// Creates a reconnect service recovering through [sessionService], re-authenticating through
  /// [authenticationService].
  ReconnectServiceImpl({
    required SessionService sessionService,
    required AuthenticationService authenticationService,
    List<Duration> attemptDelays = kReconnectAttemptDelays,
    Duration deadline = kReconnectDeadline,
    DateTime Function() now = DateTime.now,
  }) : _sessionService = sessionService,
       _authenticationService = authenticationService,
       _attemptDelays = attemptDelays,
       _deadline = deadline,
       _now = now;

  /// Implements [ReconnectService.onOrdinaryTransportLoss].
  @override
  void onOrdinaryTransportLoss(Uri uri) {
    unawaited(_recover(uri));
  }

  /// Attempts bounded recovery to [uri]: at most `_attemptDelays.length` attempts, spaced by its
  /// delays, never continuing past `_deadline` from the first attempt. Stops immediately --
  /// without consuming further attempts -- on a terminal protocol rejection from
  /// re-authenticating, per [ReconnectRejectionClassifier.isTerminal]; a retryable protocol
  /// rejection instead consumes the attempt and continues, the same as a generic
  /// transport/connectivity failure. Also stops, without touching the connection again, if
  /// something else (an explicit disconnect or an administrative invalidation) already moved the
  /// session out of `reconnecting`. On exhaustion or terminal rejection, finalizes the cycle with
  /// a disconnect that fails whatever operations `AuthenticationServiceImpl.hello`'s own
  /// mid-cycle cleanup preserved for retry.
  Future<void> _recover(Uri uri) async {
    final DateTime deadline = _now().add(_deadline);
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
          break;
        }
        continue;
      } on Object {
        continue;
      }
    }
    await _sessionService.disconnect(
      reason: const DovahLinkConnectionException(
        'Reconnect could not restore the connection.',
      ),
    );
  }
}
