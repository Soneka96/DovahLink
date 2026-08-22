import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Owns pending requests, timeouts, and retry behavior, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". The SDK owns exactly one of these
/// per [DovahLinkTransport] connection; see `ai/context/sdk/architecture.md`'s "Inbound message
/// handling" for the correlation model this implements.
class RequestManager {
  /// The transport this manager sends outgoing envelopes over.
  final DovahLinkTransport _transport;

  /// The bounded wait allowed for each [TimeoutClass], keyed by class.
  final Map<TimeoutClass, Duration> _timeoutDurations;

  /// The live session identity/trust state each outgoing and retried envelope is stamped with or
  /// revalidated against.
  final SessionContext _sessionContext;

  /// Where a detected timeout or send failure is reported for connection teardown.
  final ConnectionLifecycleReporter _reporter;

  /// Creates a request manager sending over [transport], timed per [timeoutDurations], reading
  /// live session state through [sessionContext], and reporting connection-health events to
  /// [reporter].
  RequestManager({
    required DovahLinkTransport transport,
    required Map<TimeoutClass, Duration> timeoutDurations,
    required SessionContext sessionContext,
    required ConnectionLifecycleReporter reporter,
  }) : _transport = transport,
       _timeoutDurations = timeoutDurations,
       _sessionContext = sessionContext,
       _reporter = reporter;

  /// Source of randomness for generating outgoing `messageId` values.
  final Random _random = Random.secure();

  /// Every operation awaiting a correlated reply on the current connection, keyed by the outgoing
  /// `messageId` it was transmitted under.
  final Map<String, PendingOperation> _pendingOperations =
      <String, PendingOperation>{};

  /// Retry-safe operations an ordinary (non-administrative) transport loss orphaned before they
  /// received a reply, awaiting a chance to be retransmitted once the next `hello` succeeds.
  final List<PendingOperation> _orphanedRetryableOperations =
      <PendingOperation>[];

  /// Sends one envelope carrying [messageType]/[payload], classified against [policy], and
  /// returns its correlated reply. The reply may arrive on a later wire attempt than the one this
  /// call makes: if the connection is lost before a reply arrives and [policy] is `retrySafe`,
  /// this same call keeps waiting while the operation is retransmitted once, automatically, after
  /// the next successful `hello` -- see [retryOrphanedOperations]. A [DovahLinkConnectionException]
  /// means the connection itself is unhealthy (transport failure, timeout, or a
  /// non-retriable/failed retry); a [DovahLinkProtocolException] means the bridge answered on an
  /// otherwise-live connection with a wire-level rejection or an unexpected reply.
  Future<Envelope> sendAndAwait({
    required ProtocolMessageType messageType,
    required JsonMap payload,
    required ProtocolMessageType expectedType,
    required RequestPolicy policy,
  }) async {
    final PendingOperation operation = PendingOperation(
      messageType: messageType,
      payload: payload,
      policy: policy,
    );
    _transmit(operation);
    final Envelope envelope = await operation.completer.future;
    return _validateReply(expectedType: expectedType, envelope: envelope);
  }

  /// Generates a fresh `messageId`, registers [operation] as pending under it, arms its timeout,
  /// and sends it. Used both for an operation's initial transmission and its at-most-one retry
  /// after reconnect, so both share identical wire behavior. Fire-and-forget: a send failure
  /// reports through [ConnectionLifecycleReporter.onUnhealthy] rather than throwing here, since
  /// nothing local is positioned to catch it synchronously once retries are involved -- see
  /// [operation]'s shared `completer`.
  void _transmit(PendingOperation operation) {
    final String messageId = _generateMessageId();
    _pendingOperations[messageId] = operation;
    operation.timer = Timer(
      _timeoutDurations[operation.policy.timeoutClass]!,
      () {
        _reporter.onUnhealthy(
          DovahLinkConnectionException(
            'Timed out awaiting a reply to ${operation.messageType}.',
          ),
        );
      },
    );

    final Envelope outgoing = Envelope(
      messageType: operation.messageType,
      messageId: messageId,
      sessionId: _sessionContext.currentSessionId,
      correlationId: null,
      payload: operation.payload,
      bridgeInstanceId: null,
      playContextId: null,
      clientId: null,
    );
    unawaited(
      _transport.send(jsonEncode(outgoing.toJson())).catchError((Object error) {
        _reporter.onUnhealthy(
          DovahLinkConnectionException(
            'Failed to send ${operation.messageType}: $error',
          ),
        );
      }),
    );
  }

  /// Translates a wire `error` or an unexpected message type into a typed exception; otherwise
  /// returns [envelope] unchanged.
  Envelope _validateReply({
    required ProtocolMessageType expectedType,
    required Envelope envelope,
  }) {
    if (envelope.messageType == ProtocolMessageType.error) {
      try {
        final ErrorPayload error = ErrorPayload.fromJson(envelope.payload);
        throw DovahLinkProtocolException(
          code: error.code,
          message: error.message,
          retryable: error.retryable,
        );
      } on ProtocolFormatException catch (error) {
        throw DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: error.message,
          retryable: false,
        );
      }
    }
    if (envelope.messageType != expectedType) {
      throw DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'Expected $expectedType but received ${envelope.messageType}.',
        retryable: false,
      );
    }
    return envelope;
  }

  /// Resolves the pending operation matching [correlationId] with [envelope], completing its
  /// caller's [sendAndAwait]. Returns `false` if no pending operation matches -- a protocol
  /// violation for the caller (typically [MessageRouter]) to report, not a case this method
  /// handles itself.
  bool resolveReply(String correlationId, Envelope envelope) {
    final PendingOperation? operation = _pendingOperations.remove(
      correlationId,
    );
    if (operation == null) {
      return false;
    }
    operation.timer?.cancel();
    if (!operation.completer.isCompleted) {
      operation.completer.complete(envelope);
    }
    return true;
  }

  /// Resolves every pending operation, used for both an ordinary transport-level teardown and an
  /// unconditional failure (a protocol violation or a deliberate disconnect). When
  /// [orphanRetrySafeOperations] is `true`, a `retrySafe` operation that has not already been
  /// retried once is parked in [_orphanedRetryableOperations] instead of being failed immediately,
  /// awaiting [retryOrphanedOperations]; when `false`, every pending operation is failed
  /// immediately and any already-orphaned operation is failed too.
  void failAll(Exception reason, {required bool orphanRetrySafeOperations}) {
    final List<PendingOperation> pending = _pendingOperations.values.toList();
    _pendingOperations.clear();
    for (final PendingOperation operation in pending) {
      operation.timer?.cancel();
      if (orphanRetrySafeOperations &&
          operation.policy.retrySafe &&
          !operation.hasRetried) {
        _orphanedRetryableOperations.add(operation);
      } else if (!operation.completer.isCompleted) {
        operation.completer.completeError(reason);
      }
    }

    if (!orphanRetrySafeOperations) {
      final List<PendingOperation> orphaned = _orphanedRetryableOperations
          .toList();
      _orphanedRetryableOperations.clear();
      for (final PendingOperation operation in orphaned) {
        if (!operation.completer.isCompleted) {
          operation.completer.completeError(reason);
        }
      }
    }
  }

  /// Retransmits, at most once each, every operation an earlier ordinary transport loss orphaned,
  /// now that a new session has been admitted. An operation whose
  /// [RequestPolicy.requiredTrustState] the new session no longer satisfies fails without
  /// retransmission instead of being retried into a session it was never classified for.
  void retryOrphanedOperations() {
    final List<PendingOperation> toRetry = _orphanedRetryableOperations
        .toList();
    _orphanedRetryableOperations.clear();
    for (final PendingOperation operation in toRetry) {
      final DovahLinkTrustState? required = operation.policy.requiredTrustState;
      if (required != null && required != _sessionContext.currentTrustState) {
        if (!operation.completer.isCompleted) {
          operation.completer.completeError(
            DovahLinkConnectionException(
              'Cannot retry ${operation.messageType} after reconnect: the new session no '
              'longer satisfies its required trust state.',
            ),
          );
        }
        continue;
      }
      operation.hasRetried = true;
      _transmit(operation);
    }
  }

  /// Generates a cryptographically random, session-unique `messageId`.
  String _generateMessageId() => _generateRandomHex(16);

  /// Generates [byteCount] cryptographically random bytes, hex-encoded.
  String _generateRandomHex(int byteCount) {
    final List<int> bytes = List<int>.generate(
      byteCount,
      (_) => _random.nextInt(256),
    );
    return bytes
        .map((int byte) => byte.toRadixString(16).padLeft(2, '0'))
        .join();
  }
}
