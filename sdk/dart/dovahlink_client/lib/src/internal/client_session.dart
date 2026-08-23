import 'dart:async';

import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_recovery_observer.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/message_receiver.dart';
import 'package:dovahlink_client_sdk/src/internal/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/internal/session_connector.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/internal/session_lifecycle_state.dart';
import 'package:dovahlink_client_sdk/src/internal/session_trust_writer.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Owns transport lifecycle, connection state, and stream ownership, per
/// `ai/context/sdk/architecture.md`'s "Internal composition" and "Session-state ownership". The
/// sole owner of every socket-scoped field this engine has: [connectionState], [currentSessionId],
/// [currentTrustState], [invalidationReason], the connection generation, and the transport
/// subscription. No other class -- including [DovahLinkClient] -- assigns `sessionId` or trust
/// state directly; [admitSession] and [markTrusted] are the only write commands.
class ClientSession
    implements
        ConnectionLifecycleReporter,
        SessionConnector,
        SessionTrustWriter,
        MessageReceiver,
        SessionLifecycleState {
  /// The transport this session connects, sends over, and closes.
  final DovahLinkTransport _transport;

  /// Creates a client session over [transport], timed per [timeoutDurations]. Builds its own
  /// [RequestManager] and [MessageRouter] in this constructor's body -- after [_transport] is
  /// assigned, so `this` is available to hand them as their [SessionContext] and
  /// [ConnectionLifecycleReporter] -- so that by the time this constructor returns, this session
  /// is fully wired and no caller can ever observe a partially-initialized instance.
  ClientSession({
    required DovahLinkTransport transport,
    required Map<TimeoutClass, Duration> timeoutDurations,
  }) : _transport = transport {
    _requestManager = RequestManager(
      transport: transport,
      timeoutDurations: timeoutDurations,
      sessionContext: this,
      reporter: this,
    );
    _messageRouter = MessageRouter(
      replyResolver: _requestManager,
      reporter: this,
    );
    _teardownCoordinator = ConnectionTeardownCoordinator(
      transport: transport,
      lifecycleQueue: _lifecycleQueue,
      pendingOperationFailureHandler: _requestManager.failAll,
      state: this,
    );
  }

  /// Creates a client session with directly-injected [requestManager] and [messageRouter],
  /// bypassing the internal construction the unnamed constructor performs. Test-only: lets a
  /// caller substitute mocked collaborators while still constructing atomically -- both are
  /// assigned as regular constructor parameters, never late-mutated after the fact.
  @visibleForTesting
  ClientSession.withCollaborators({
    required DovahLinkTransport transport,
    required RequestManager requestManager,
    required MessageRouter messageRouter,
  }) : _transport = transport,
       _requestManager = requestManager,
       _messageRouter = messageRouter {
    _teardownCoordinator = ConnectionTeardownCoordinator(
      transport: transport,
      lifecycleQueue: _lifecycleQueue,
      pendingOperationFailureHandler: requestManager.failAll,
      state: this,
    );
  }

  /// Owns pending requests, timeouts, and retry behavior; see [requestManager] for the shared
  /// coordinator used by this session's request-oriented services.
  late final RequestManager _requestManager;

  /// Owns envelope decoding, correlation, and unsolicited routing.
  late final MessageRouter _messageRouter;

  /// Coordinates resource cleanup without owning this session's socket-scoped state.
  late final ConnectionTeardownCoordinator _teardownCoordinator;

  /// The subscription currently reading [_transport]'s inbound message stream, or `null` when no
  /// connection is being received for. The SDK owns exactly one of these at a time, per
  /// `ai/context/sdk/architecture.md`'s "Inbound message handling".
  StreamSubscription<String>? _messageSubscription;

  /// Identifies the connection [_messageSubscription] currently belongs to. Incremented every
  /// time that connection is torn down, so a callback still in flight from an old subscription
  /// can recognize it no longer belongs to the current connection and must not mutate this
  /// session's state -- teardown itself still awaits full cancellation before a new subscription
  /// is established; this is defense in depth on top of that ordering, not a substitute for it.
  int _connectionGeneration = 0;

  /// Serializes connect, close, and invalidation cleanup so an old transport close can never run
  /// after a newer connection has been established.
  final LifecycleOperationQueue _lifecycleQueue = LifecycleOperationQueue();

  /// The current connection lifecycle phase.
  DovahLinkConnectionState _connectionState =
      DovahLinkConnectionState.disconnected;

  /// The current trust standing, or `null` before [admitSession] is called.
  DovahLinkTrustState? _trustState;

  /// The server-issued session identifier, or `null` before [admitSession] is called.
  String? _sessionId;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? _invalidationReason;

  /// The URI most recently passed to [connect], used to retry the same endpoint after ordinary
  /// transport loss. `null` before [connect] is ever called.
  Uri? _lastConnectedUri;

  /// Notified when ordinary transport loss finishes tearing down the connection, so bounded
  /// automatic reconnect may begin. `null` (the default) until [DovahLinkClient] assigns the real
  /// coordinator after constructing it -- building that coordinator requires
  /// [AuthenticationService], which itself depends on this session, so it cannot be a constructor
  /// parameter here; see `ai/context/sdk/architecture.md`'s "Internal composition". A session with
  /// no observer assigned never attempts automatic reconnect.
  ConnectionRecoveryObserver? recoveryObserver;

  /// The [RequestManager] this session owns.
  RequestManager get requestManager => _requestManager;

  /// Implements [SessionConnector.connectionState].
  @override
  DovahLinkConnectionState get connectionState => _connectionState;

  /// Implements [SessionContext.currentSessionId].
  @override
  String? get currentSessionId => _sessionId;

  /// Implements [SessionContext.currentTrustState].
  @override
  DovahLinkTrustState? get currentTrustState => _trustState;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason =>
      _invalidationReason;

  /// Implements [SessionLifecycleState.isAdministrativelyInvalidated].
  @override
  bool get isAdministrativelyInvalidated =>
      _connectionState == DovahLinkConnectionState.administrativelyInvalidated;

  /// Implements [SessionLifecycleState.connectionGeneration].
  @override
  int get connectionGeneration => _connectionGeneration;

  /// Implements [SessionConnector.connect]. One attempt within a bounded-reconnect cycle (entered
  /// while [connectionState] is already `reconnecting`) keeps [connectionState] at `reconnecting`
  /// for the whole attempt instead of passing through `connecting`/`disconnected`, so recovery
  /// stays outwardly visible as one continuous `reconnecting` phase rather than flickering between
  /// attempts; an ordinary, non-recovery call still shows the normal
  /// `connecting` -> `connected`/`disconnected` transition.
  @override
  Future<void> connect(Uri uri) => _lifecycleQueue.run(() async {
    final bool isRecoveryAttempt =
        _connectionState == DovahLinkConnectionState.reconnecting;
    _connectionGeneration++;
    _invalidationReason = null;
    if (!isRecoveryAttempt) {
      _connectionState = DovahLinkConnectionState.connecting;
    }
    _lastConnectedUri = uri;
    try {
      await _transport.connect(uri);
      _connectionState = DovahLinkConnectionState.connected;
      ensureReceiving();
    } on Object catch (error) {
      if (!isRecoveryAttempt) {
        _connectionState = DovahLinkConnectionState.disconnected;
      }
      throw DovahLinkConnectionException('Failed to connect to $uri: $error');
    }
  });

  /// Implements [SessionConnector.disconnect].
  @override
  Future<void> disconnect({
    bool orphanRetrySafeOperations = false,
    Exception? reason,
  }) async {
    await _teardownCoordinator.tearDown(
      reason ?? const DovahLinkConnectionException('Disconnected.'),
      orphanRetrySafeOperations: orphanRetrySafeOperations,
    );
  }

  /// Implements [SessionConnector.admitSession].
  @override
  void admitSession({
    required String sessionId,
    required DovahLinkTrustState trustState,
  }) {
    _sessionId = sessionId;
    _trustState = trustState;
    _requestManager.retryOrphanedOperations();
  }

  /// Implements [SessionTrustWriter.markTrusted].
  @override
  void markTrusted() {
    _trustState = DovahLinkTrustState.trusted;
  }

  /// Implements [ConnectionLifecycleReporter.onUnhealthy]. Ordinary transport loss: tears down,
  /// then -- only if that teardown actually reached plain `disconnected` (not raced by a
  /// concurrent administrative invalidation, which owns its own terminal state) and both a
  /// last-connected URI and [recoveryObserver] are available -- transitions to `reconnecting` and
  /// hands off to the observer to attempt bounded automatic recovery.
  @override
  void onUnhealthy(Exception reason) {
    unawaited(_beginRecoveryAfterOrdinaryTransportLoss(reason));
  }

  /// Implements [ConnectionLifecycleReporter.onProtocolViolation].
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

  /// See [ConnectionLifecycleReporter.onSessionInvalidated]. Receiving one with no currently
  /// authenticated session is an impossible state per `protocol/schema/README.md` (the event is
  /// only ever sent for an existing authenticated session) and fails closed as a protocol
  /// violation via [ConnectionTeardownCoordinator.tearDown] rather than being accepted. Otherwise sets
  /// [connectionState] and [invalidationReason], fails every pending or retry-orphaned operation
  /// immediately (administrative invalidation is terminal and never eligible for
  /// [RequestManager.retryOrphanedOperations] -- recovery is Stage 2's explicit, user-initiated
  /// Retry only), then delegates resource closure to
  /// [ConnectionTeardownCoordinator.closeAfterInvalidation]. The immediate failure intentionally
  /// precedes best-effort resource closure so callers stop awaiting work without depending on
  /// transport cleanup; a following socket close the bridge itself performs
  /// cannot overwrite this typed reason back to generic transport loss -- see
  /// [_handleReceiveFailure]'s generation/state guard.
  @override
  void onSessionInvalidated(AdministrativeInvalidationReason reason) {
    if (_connectionState ==
        DovahLinkConnectionState.administrativelyInvalidated) {
      return;
    }
    if (_sessionId == null || _trustState == null) {
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

    _invalidationReason = reason;
    _connectionState = DovahLinkConnectionState.administrativelyInvalidated;
    _sessionId = null;
    _trustState = null;
    _connectionGeneration++;

    _requestManager.failAll(
      DovahLinkConnectionException(
        'Session invalidated ($reason) while awaiting a reply.',
      ),
      orphanRetrySafeOperations: false,
    );

    unawaited(
      _teardownCoordinator.closeAfterInvalidation(_connectionGeneration),
    );
  }

  /// Implements [ConnectionLifecycleReporter.onUnsolicitedError]. Not ordinary connectivity loss
  /// -- torn down without orphaning any retry-safe operation for automatic reconnect, carrying
  /// [error]'s own bridge-reported classification rather than a generic malformed-message reason.
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

  /// Ensures exactly one subscription is reading [_transport]'s inbound message stream for the
  /// current connection. Idempotent: a no-op once a subscription already exists. Called both from
  /// [connect] (the normal production trigger -- the SDK owns one reader for the connection's
  /// whole lifetime, not only while a request happens to be outstanding) and defensively by a
  /// caller that sends before ever calling [connect]: without this, a `retrySafe` request sent on
  /// a never-connected session would be silently orphaned awaiting a reconnect that never
  /// happens, instead of failing with a typed exception.
  /// Implements [MessageReceiver.ensureReceiving].
  @override
  void ensureReceiving() {
    if (_messageSubscription != null) {
      return;
    }
    final int generation = _connectionGeneration;
    final Stream<String> messages;
    try {
      messages = _transport.messages;
    } on Object catch (error) {
      // A caller that sends before ever calling connect() reaches this synchronously (the real
      // transport's messages getter throws when not connected) -- convert to the same typed
      // exception every other connection-level failure in this class already uses, rather than
      // letting a raw transport-specific error type escape.
      throw DovahLinkConnectionException('Cannot receive: $error');
    }
    _messageSubscription = messages.listen(
      (String raw) => _handleIncomingMessage(raw, generation),
      onError: (Object error, StackTrace stackTrace) => _handleReceiveFailure(
        generation,
        DovahLinkConnectionException('Connection lost while receiving: $error'),
      ),
      onDone: () => _handleReceiveFailure(
        generation,
        const DovahLinkConnectionException('Connection closed by the bridge.'),
      ),
    );
  }

  /// Implements [SessionLifecycleState.bumpConnectionGeneration].
  @override
  void bumpConnectionGeneration() {
    _connectionGeneration++;
  }

  /// Implements [SessionLifecycleState.detachMessageSubscription].
  @override
  StreamSubscription<String>? detachMessageSubscription() {
    final StreamSubscription<String>? subscription = _messageSubscription;
    _messageSubscription = null;
    return subscription;
  }

  /// Implements [SessionLifecycleState.resetAfterConnectionTeardown].
  @override
  void resetAfterConnectionTeardown({required bool preserveReconnecting}) {
    final bool staysReconnecting =
        preserveReconnecting &&
        _connectionState == DovahLinkConnectionState.reconnecting;
    _connectionState = staysReconnecting
        ? DovahLinkConnectionState.reconnecting
        : DovahLinkConnectionState.disconnected;
    _trustState = null;
    _sessionId = null;
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
      if (_connectionState != DovahLinkConnectionState.disconnected) {
        return;
      }
      final Uri? uri = _lastConnectedUri;
      final ConnectionRecoveryObserver? observer = recoveryObserver;
      if (uri == null || observer == null) {
        return;
      }
      _connectionState = DovahLinkConnectionState.reconnecting;
      observer.onOrdinaryTransportLoss(uri);
    });
  }

  /// Dispatches one inbound message from the connection [generation] its subscription was
  /// established under. A stale message from an already-superseded generation is ignored, never
  /// mutating current state; otherwise decoding and routing belongs to [_messageRouter].
  void _handleIncomingMessage(String raw, int generation) {
    if (generation != _connectionGeneration) {
      return;
    }
    _messageRouter.handleIncoming(raw);
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
    if (generation != _connectionGeneration) {
      return;
    }
    unawaited(_beginRecoveryAfterOrdinaryTransportLoss(reason));
  }
}
