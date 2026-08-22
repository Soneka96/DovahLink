import 'dart:async';

import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/message_receiver.dart';
import 'package:dovahlink_client_sdk/src/internal/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/internal/session_connector.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/internal/session_trust_writer.dart';
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
        SessionContext,
        ConnectionLifecycleReporter,
        SessionConnector,
        SessionTrustWriter,
        MessageReceiver {
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
      requestManager: _requestManager,
      reporter: this,
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
       _messageRouter = messageRouter;

  /// Owns pending requests, timeouts, and retry behavior; see [requestManager] for the accessor
  /// composition roots use to hand this same instance to other collaborators (for example
  /// `AuthenticationService`/`PairingService`) that need to send requests without depending on
  /// this whole session.
  late final RequestManager _requestManager;

  /// Owns envelope decoding, correlation, and unsolicited routing.
  late final MessageRouter _messageRouter;

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
  Future<void> _lifecycleTail = Future<void>.value();

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

  /// The [RequestManager] this session owns, exposed so a composition root can hand the same
  /// instance to other collaborators that need to send requests directly.
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

  /// Implements [SessionConnector.connect].
  @override
  Future<void> connect(Uri uri) => _runLifecycleOperation(() async {
    _connectionGeneration++;
    _invalidationReason = null;
    _connectionState = DovahLinkConnectionState.connecting;
    try {
      await _transport.connect(uri);
      _connectionState = DovahLinkConnectionState.connected;
      ensureReceiving();
    } on Object catch (error) {
      _connectionState = DovahLinkConnectionState.disconnected;
      throw DovahLinkConnectionException('Failed to connect to $uri: $error');
    }
  });

  /// Implements [SessionConnector.disconnect].
  @override
  Future<void> disconnect() async {
    await _teardownConnection(
      const DovahLinkConnectionException('Disconnected.'),
      orphanRetrySafeOperations: false,
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

  /// Implements [ConnectionLifecycleReporter.onUnhealthy].
  @override
  void onUnhealthy(Exception reason) {
    unawaited(_teardownConnection(reason));
  }

  /// Implements [ConnectionLifecycleReporter.onProtocolViolation].
  @override
  void onProtocolViolation(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  }) {
    unawaited(
      _teardownConnection(
        reason,
        orphanRetrySafeOperations: orphanRetrySafeOperations,
      ),
    );
  }

  /// See [ConnectionLifecycleReporter.onSessionInvalidated]. Receiving one with no currently
  /// authenticated session is an impossible state per `protocol/schema/README.md` (the event is
  /// only ever sent for an existing authenticated session) and fails closed as a protocol
  /// violation via [_teardownConnection] rather than being accepted. Otherwise sets
  /// [connectionState] and [invalidationReason], fails every pending or retry-orphaned operation
  /// (administrative invalidation is terminal and never eligible for
  /// [RequestManager.retryOrphanedOperations] -- recovery is Stage 2's explicit, user-initiated
  /// Retry only), and tears the connection down directly rather than through
  /// [_teardownConnection]/[disconnect], so a following socket close the bridge itself performs
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
        _teardownConnection(
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

    unawaited(_closeConnectionAfterInvalidation(_connectionGeneration));
  }

  /// Best-effort subscription cancellation and transport close for [onSessionInvalidated],
  /// matching [disconnect]'s own tolerance for a close that cannot complete cleanly.
  Future<void> _closeConnectionAfterInvalidation(int generation) async {
    await _runLifecycleOperation(() async {
      if (generation != _connectionGeneration) {
        return;
      }
      final StreamSubscription<String>? subscription = _messageSubscription;
      _messageSubscription = null;
      if (subscription != null) {
        try {
          await subscription.cancel();
        } on Object {
          // Best-effort, matching disconnect()'s existing tolerance.
        }
      }
      if (generation != _connectionGeneration) {
        return;
      }
      try {
        await _transport.close();
      } on Object {
        // Best-effort, matching disconnect()'s existing tolerance.
      }
    });
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
  /// [generation]'s subscription. Ignored if [generation] has already been superseded -- a stale
  /// receiver's own failure must not tear down a newer, already-healthy connection.
  void _handleReceiveFailure(
    int generation,
    DovahLinkConnectionException reason,
  ) {
    if (generation != _connectionGeneration) {
      return;
    }
    unawaited(_teardownConnection(reason));
  }

  /// The single path that tears an unhealthy connection down: bumps [_connectionGeneration] so a
  /// stale callback still in flight recognizes it no longer applies, awaits cancelling
  /// [_messageSubscription] and closing [_transport] before returning, then resolves every
  /// currently pending operation through [RequestManager.failAll]. A no-op if [connectionState]
  /// is already [DovahLinkConnectionState.administrativelyInvalidated] -- that typed reason is
  /// never overwritten by a failure that follows it, such as the bridge's own follow-up socket
  /// close. Otherwise always runs its full cleanup, even if [connectionState] was already
  /// [DovahLinkConnectionState.disconnected] (for example a caller that never called [connect] at
  /// all before a request failed) -- every step below ([_messageSubscription] cancellation,
  /// [_transport] close) is independently idempotent, so a redundant call is harmless, but
  /// skipping it entirely would leave [_transport] never actually closed for that caller.
  Future<void> _teardownConnection(
    Exception reason, {
    bool orphanRetrySafeOperations = true,
  }) => _runLifecycleOperation(() async {
    if (_connectionState ==
        DovahLinkConnectionState.administrativelyInvalidated) {
      return;
    }
    _connectionGeneration++;

    final StreamSubscription<String>? subscription = _messageSubscription;
    _messageSubscription = null;
    if (subscription != null) {
      try {
        await subscription.cancel();
      } on Object {
        // Best-effort: in-memory state resets below regardless.
      }
    }
    try {
      await _transport.close();
    } on Object {
      // Best-effort: in-memory state resets below regardless of whether the transport could
      // close cleanly.
    }

    _connectionState = DovahLinkConnectionState.disconnected;
    _trustState = null;
    _sessionId = null;

    _requestManager.failAll(
      reason,
      orphanRetrySafeOperations: orphanRetrySafeOperations,
    );
  });

  /// Runs one transport lifecycle operation after all earlier lifecycle operations finish.
  Future<void> _runLifecycleOperation(Future<void> Function() operation) {
    final Future<void> previous = _lifecycleTail;
    final Completer<void> completion = Completer<void>();
    _lifecycleTail = completion.future;
    return () async {
      try {
        await previous;
        await operation();
      } finally {
        completion.complete();
      }
    }();
  }
}
