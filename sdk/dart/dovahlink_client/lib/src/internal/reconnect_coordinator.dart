import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_recovery_observer.dart';
import 'package:dovahlink_client_sdk/src/internal/session_connector.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns bounded automatic recovery from ordinary transport loss, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". Reconnects to the endpoint the
/// session last connected to and re-authenticates, up to a bounded attempt budget and a hard
/// overall deadline (each defaulting to the centrally tuned [kReconnectAttemptDelays]/
/// [kReconnectDeadline]) -- whichever is exhausted first. `ClientSession` continues to own
/// transport/session state and teardown, and `AuthenticationService` continues to own
/// authentication; this class only orchestrates when and how often to retry both.
class ReconnectCoordinator implements ConnectionRecoveryObserver {
  /// Reconnects to and disconnects from the bridge, and reports live connection state.
  final SessionConnector _sessionConnector;

  /// Re-authenticates the reconnected transport, admitting a fresh session on success.
  final Future<HelloResult> Function() _reauthenticate;

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

  /// Creates a coordinator recovering through [sessionConnector], re-authenticating through
  /// [reauthenticate].
  ReconnectCoordinator({
    required SessionConnector sessionConnector,
    required Future<HelloResult> Function() reauthenticate,
    List<Duration> attemptDelays = kReconnectAttemptDelays,
    Duration deadline = kReconnectDeadline,
    DateTime Function() now = DateTime.now,
  }) : _sessionConnector = sessionConnector,
       _reauthenticate = reauthenticate,
       _attemptDelays = attemptDelays,
       _deadline = deadline,
       _now = now;

  /// Implements [ConnectionRecoveryObserver.onOrdinaryTransportLoss].
  @override
  void onOrdinaryTransportLoss(Uri uri) {
    unawaited(_recover(uri));
  }

  /// Attempts bounded recovery to [uri]: at most `_attemptDelays.length` attempts, spaced by its
  /// delays, never continuing past `_deadline` from the first attempt. Stops immediately --
  /// without consuming further attempts -- on a typed authentication or administrative rejection
  /// ([DovahLinkProtocolException]) from re-authenticating, since retrying the same rejected
  /// credential cannot succeed; only a generic transport/connectivity failure consumes the attempt
  /// budget. Also stops, without touching the connection again, if something else (an explicit
  /// disconnect or an administrative invalidation) already moved the session out of
  /// [DovahLinkConnectionState.reconnecting]. On exhaustion or rejection, finalizes the cycle with
  /// a disconnect that fails whatever operations `AuthenticationService.hello`'s own mid-cycle
  /// cleanup preserved for retry.
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
      if (_sessionConnector.connectionState !=
          DovahLinkConnectionState.reconnecting) {
        return;
      }
      if (_now().isAfter(deadline)) {
        break;
      }
      try {
        await _sessionConnector.connect(uri);
        await _reauthenticate();
        return;
      } on DovahLinkProtocolException {
        break;
      } on Object {
        continue;
      }
    }
    await _sessionConnector.disconnect(
      reason: const DovahLinkConnectionException(
        'Reconnect could not restore the connection.',
      ),
    );
  }
}
