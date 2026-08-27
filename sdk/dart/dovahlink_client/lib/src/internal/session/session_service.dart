import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/session/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/session/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// Owns transport lifecycle, connection state, and stream ownership, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". The single architectural interface
/// implemented by `SessionService`, the sole authoritative owner of every session-scoped mutable
/// fact this engine has (backed internally by `SessionState`). Every read and command a legitimate
/// consumer of the connection's lifecycle needs -- `connect`/`disconnect`, its current phase and
/// identity, and reacting to a connection-health signal a collaborator elsewhere detects -- lives on
/// this one contract; see `ai/context/sdk/architecture.md`'s "Request/session boundary" for why the
/// four reactive report methods belong here rather than on a separate interface or callback.
abstract interface class ISessionService {
  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState;

  /// A stream of every [connectionState] transition: the current value immediately on listen,
  /// then each subsequent real change, per `ai/context/sdk/api-design.md`'s "New-subscriber
  /// state replay".
  Stream<DovahLinkConnectionState> get connectionStateChanges;

  /// The server-issued session identifier of the current session, or `null` before one is
  /// admitted.
  String? get currentSessionId;

  /// The current trust standing, or `null` before one is admitted.
  DovahLinkTrustState? get currentTrustState;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason;

  /// Establishes the transport connection to [uri].
  /// @throws [DovahLinkConnectionException] if the socket cannot be established.
  Future<void> connect(Uri uri);

  /// Closes the connection and resets in-memory session state. Idempotent. A `retrySafe` pending
  /// or already-orphaned operation is failed rather than preserved unless [orphanRetrySafeOperations]
  /// is `true` -- callers finalizing bounded recovery (successfully or not) want the default; a
  /// caller that expects more recovery attempts to follow passes `true` to preserve them, with
  /// [reason] describing why this specific call closed the connection, when it differs from a
  /// caller's own default.
  Future<void> disconnect({
    bool orphanRetrySafeOperations = false,
    Exception? reason,
  });

  /// Reports that the connection is no longer healthy (a send failure, a timeout, or a transport
  /// error/close) and must be torn down.
  void onUnhealthy(Exception reason);

  /// Reports a protocol-level anomaly on an otherwise-live connection (malformed JSON, an
  /// unmatched correlation ID, or an unrecognized DTO-boundary value) that must be torn down
  /// without treating it as safe to retry. [orphanRetrySafeOperations] controls whether a
  /// `retrySafe` pending operation is parked for a later retry instead of failed immediately, per
  /// `ai/context/sdk/api-design.md`'s "Request retry safety, session requirement, and timeout
  /// class".
  void onProtocolViolation(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  });

  /// Reports an authoritative `session_invalidated` push, decoded and validated by the caller.
  void onSessionInvalidated(AdministrativeInvalidationReason reason);

  /// Reports a decoded unsolicited `error` push (`correlationId: null`), sent for a violation the
  /// bridge detects before it can correlate a reply -- for example before decoding completes, or
  /// before a session exists. Not ordinary connectivity loss: the connection is torn down without
  /// automatic reconnect, carrying [error]'s own code/message/retryable classification rather than
  /// a generic malformed-message reason.
  void onUnsolicitedError(ErrorPayload error);
}

/// Implements [ISessionService], per `ai/context/sdk/architecture.md`'s "Internal composition" and
/// "Session-state ownership". Backed by [SessionState], the single authoritative owner of every
/// session-scoped mutable fact this engine has; this class never duplicates that state, only
/// drives its transitions. Every collaborator ([LifecycleOperationQueue],
/// [ConnectionTeardownCoordinator]) is supplied by the caller per
/// `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never constructs one of
/// its own dependencies. `ConnectionTeardownCoordinator` depends directly on the same [SessionState]
/// instance this class holds.
class SessionService implements ISessionService {
  /// The transport this service connects, sends over, and closes.
  final IDovahLinkTransport _transport;

  /// The single authoritative owner of this session's mutable facts.
  final SessionState _state;

  /// Serializes connect, close, and invalidation cleanup so an old transport close can never run
  /// after a newer connection has been established.
  final LifecycleOperationQueue _lifecycleQueue;

  /// Coordinates resource cleanup without owning this session's socket-scoped state. Built by the
  /// caller with a `pendingOperationFailureHandler` that forwards into this instance's own
  /// [onTeardown] -- necessarily supplied after construction, since the coordinator must exist
  /// before this constructor runs; see the caller's own composition code.
  final ConnectionTeardownCoordinator _teardownCoordinator;

  /// Creates a session service over [transport] and [state], coordinating teardown through
  /// [teardownCoordinator] and serializing lifecycle operations through [lifecycleQueue].
  SessionService({
    required IDovahLinkTransport transport,
    required SessionState state,
    required LifecycleOperationQueue lifecycleQueue,
    required ConnectionTeardownCoordinator teardownCoordinator,
  }) : _transport = transport,
       _state = state,
       _lifecycleQueue = lifecycleQueue,
       _teardownCoordinator = teardownCoordinator;

  /// Notified after a real (non-duplicate) teardown, so pending operations can be failed or
  /// orphaned. `null` until [DovahLinkClient] assigns a [RequestService.failAll] tear-off after
  /// constructing it -- this service is built before its `RequestService`, which itself depends on
  /// `ISessionService`, so a constructor dependency in this direction would cycle; see
  /// `ai/context/sdk/architecture.md`'s "Callbacks".
  void Function(Exception reason, {required bool orphanRetrySafeOperations})?
  onTeardown;

  /// Notified when ordinary transport loss finishes tearing down the connection, so bounded
  /// automatic reconnect may begin. `null` until [DovahLinkClient] assigns it after constructing
  /// `ReconnectServiceImpl` -- the same construction-order reasoning as [onTeardown] applies, since
  /// `ReconnectServiceImpl` itself depends on `ISessionService`.
  void Function(Uri uri)? onOrdinaryTransportLoss;

  /// Receives each inbound message this session's subscription reads, already filtered to the
  /// current connection generation. `null` until `DovahLinkClient` assigns
  /// `requestServiceImpl.handleIncoming` -- the same construction-order reasoning as [onTeardown]
  /// applies, since `RequestServiceImpl` itself depends on `ISessionService`.
  void Function(String raw)? onIncomingMessage;

  /// Implements [ISessionService.connectionState].
  @override
  DovahLinkConnectionState get connectionState => _state.connectionState;

  /// Implements [ISessionService.connectionStateChanges].
  @override
  Stream<DovahLinkConnectionState> get connectionStateChanges =>
      _state.connectionStateChanges;

  /// Implements [ISessionService.currentSessionId].
  @override
  String? get currentSessionId => _state.sessionId;

  /// Implements [ISessionService.currentTrustState].
  @override
  DovahLinkTrustState? get currentTrustState => _state.trustState;

  /// Implements [ISessionService.invalidationReason].
  @override
  AdministrativeInvalidationReason? get invalidationReason =>
      _state.invalidationReason;

  /// Implements [ISessionService.connect]. One attempt within a bounded-reconnect cycle (entered
  /// while [connectionState] is already `reconnecting`) keeps [connectionState] at `reconnecting`
  /// while the socket itself is being established, instead of passing through
  /// `connecting`/`disconnected`, so recovery stays outwardly visible as one continuous
  /// `reconnecting` phase rather than flickering between attempts; once the socket actually
  /// connects, [SessionState.markConnected] moves such an attempt to `reauthenticating` rather than
  /// `connected` -- trust is not yet confirmed until the caller's own `hello` following this method
  /// admits a session. An ordinary, non-recovery call still shows the normal `connecting` ->
  /// `connected`/`disconnected` transition. See [SessionState.beginConnectAttempt].
  @override
  Future<void> connect(Uri uri) => _lifecycleQueue.run(() async {
    _state.beginConnectAttempt(uri);
    try {
      await _transport.connect(uri);
      _state.markConnected();
      _startReceiving();
    } on Object catch (error) {
      _state.markConnectFailed();
      throw DovahLinkConnectionException('Failed to connect to $uri: $error');
    }
  });

  /// Implements [ISessionService.disconnect].
  @override
  Future<void> disconnect({
    bool orphanRetrySafeOperations = false,
    Exception? reason,
  }) => _teardownCoordinator.tearDown(
    reason ?? const DovahLinkConnectionException('Disconnected.'),
    orphanRetrySafeOperations: orphanRetrySafeOperations,
  );

  /// Implements [ISessionService.onUnhealthy]. Ordinary transport loss: tears down, then -- only if
  /// that teardown actually reached plain `disconnected` (not raced by a concurrent administrative
  /// invalidation, which owns its own terminal state) and both a last-connected URI and
  /// [onOrdinaryTransportLoss] are available -- transitions to `reconnecting` and hands off to
  /// attempt bounded automatic recovery.
  @override
  void onUnhealthy(Exception reason) {
    unawaited(_beginRecoveryAfterOrdinaryTransportLoss(reason));
  }

  /// Implements [ISessionService.onProtocolViolation].
  @override
  void onProtocolViolation(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  }) {
    unawaited(
      _teardownCoordinator.tearDown(
        reason,
        orphanRetrySafeOperations: orphanRetrySafeOperations,
      ),
    );
  }

  /// See [ISessionService.onSessionInvalidated]. Receiving one with no currently authenticated
  /// session is an impossible state per `protocol/schema/README.md` (the event is only ever sent
  /// for an existing authenticated session) and fails closed as a protocol violation rather than
  /// being accepted. Otherwise sets [connectionState] and [invalidationReason] and notifies
  /// [onTeardown] immediately -- administrative invalidation is terminal and never eligible for
  /// retry, so this precedes best-effort resource closure rather than waiting on it, matching
  /// [ConnectionTeardownCoordinator.tearDown]'s ordering for every other reactive path -- then
  /// delegates resource closure to [ConnectionTeardownCoordinator.closeAfterInvalidation]. A
  /// following socket close the bridge itself performs cannot overwrite this typed reason back to
  /// generic transport loss -- see [_handleReceiveFailure]'s generation/state guard.
  @override
  void onSessionInvalidated(AdministrativeInvalidationReason reason) {
    if (_state.isAdministrativelyInvalidated) {
      return;
    }
    if (_state.sessionId == null || _state.trustState == null) {
      unawaited(
        _teardownCoordinator.tearDown(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message:
                'Received session_invalidated with no authenticated session.',
            retryable: false,
          ),
          orphanRetrySafeOperations: false,
        ),
      );
      return;
    }

    _state.invalidate(reason);
    onTeardown?.call(
      DovahLinkConnectionException(
        'Session invalidated ($reason) while awaiting a reply.',
      ),
      orphanRetrySafeOperations: false,
    );

    unawaited(
      _teardownCoordinator.closeAfterInvalidation(_state.connectionGeneration),
    );
  }

  /// Implements [ISessionService.onUnsolicitedError]. Not ordinary connectivity loss -- torn down
  /// without orphaning any retry-safe operation for automatic reconnect, carrying [error]'s own
  /// bridge-reported classification rather than a generic malformed-message reason.
  @override
  void onUnsolicitedError(ErrorPayload error) {
    unawaited(
      _teardownCoordinator.tearDown(
        DovahLinkProtocolException(
          code: error.code,
          message: error.message,
          retryable: error.retryable,
        ),
        orphanRetrySafeOperations: false,
      ),
    );
  }

  /// Ensures exactly one subscription is reading the transport's inbound message stream for the
  /// connection [connect] just established. Called only from [connect]'s own success path -- the
  /// SDK owns one reader for the connection's whole lifetime, not only while a request happens to
  /// be outstanding -- per `ai/context/sdk/architecture.md`'s "Request/session boundary": nothing
  /// outside this class ever needs to trigger receiving, since `RequestService` reasons about
  /// [connectionState] instead.
  void _startReceiving() {
    final int generation = _state.connectionGeneration;
    final Stream<String> messages;
    try {
      messages = _transport.messages;
    } on Object catch (error) {
      throw DovahLinkConnectionException('Cannot receive: $error');
    }
    _state.attachMessageSubscription(
      messages.listen(
        (String raw) => _handleIncomingMessage(raw, generation),
        onError: (Object error, StackTrace stackTrace) => _handleReceiveFailure(
          generation,
          DovahLinkConnectionException(
            'Connection lost while receiving: $error',
          ),
        ),
        onDone: () => _handleReceiveFailure(
          generation,
          const DovahLinkConnectionException(
            'Connection closed by the bridge.',
          ),
        ),
      ),
    );
  }

  /// Runs the teardown [onUnhealthy] reports, then starts bounded recovery if the connection is
  /// still eligible for it once that teardown completes. The eligibility check and the
  /// `reconnecting` transition run as their own queued step, after teardown's, so a `connect()` or
  /// `disconnect()` call already queued immediately behind the teardown is serialized against this
  /// decision instead of racing it outside the queue.
  Future<void> _beginRecoveryAfterOrdinaryTransportLoss(
    Exception reason,
  ) async {
    await _teardownCoordinator.tearDown(reason);
    await _lifecycleQueue.run(() async {
      if (_state.connectionState != DovahLinkConnectionState.disconnected) {
        return;
      }
      final Uri? uri = _state.lastConnectedUri;
      final void Function(Uri uri)? observer = onOrdinaryTransportLoss;
      if (uri == null || observer == null) {
        return;
      }
      _state.markReconnecting();
      observer(uri);
    });
  }

  /// Dispatches one inbound message from the connection [generation] its subscription was
  /// established under. A stale message from an already-superseded generation is ignored, never
  /// mutating current state; otherwise decoding, correlation, and unsolicited routing belong to
  /// `RequestService`, which is not itself known to this class -- `DovahLinkClient` wires the
  /// received-message path directly from the transport's subscription to `RequestService`, so this
  /// method only performs the generation check, not the dispatch itself.
  ///
  /// Implemented as a hook `DovahLinkClient` assigns, the same shape as [onTeardown]/
  /// [onOrdinaryTransportLoss], so `SessionService` never depends on `RequestService`.
  void _handleIncomingMessage(String raw, int generation) {
    if (generation != _state.connectionGeneration) {
      return;
    }
    onIncomingMessage?.call(raw);
  }

  /// Handles a transport-level failure (`onError`/`onDone`) reported for the connection
  /// [generation]'s subscription -- ordinary transport loss, the same as [onUnhealthy], so it may
  /// begin bounded automatic recovery the same way. Ignored if [generation] has already been
  /// superseded -- a stale receiver's own failure must not tear down a newer, already-healthy
  /// connection.
  void _handleReceiveFailure(
    int generation,
    DovahLinkConnectionException reason,
  ) {
    if (generation != _state.connectionGeneration) {
      return;
    }
    unawaited(_beginRecoveryAfterOrdinaryTransportLoss(reason));
  }
}
