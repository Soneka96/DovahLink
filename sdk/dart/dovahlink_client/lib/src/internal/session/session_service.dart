import 'dart:async';
import 'dart:io';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_invalidation.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/session/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/session/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
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

  /// The selected Known Host relationship and its admitted session phase.
  Stream<KnownHostSessionSnapshot> get knownHostSessionChanges;

  /// Emits each administrative invalidation with its exact Known Host and reason.
  Stream<DovahLinkKnownHostInvalidation> get knownHostInvalidations;

  /// The server-issued session identifier of the current session, or `null` before one is
  /// admitted.
  String? get currentSessionId;

  /// The current trust standing, or `null` before one is admitted.
  DovahLinkTrustState? get currentTrustState;

  /// An opaque identity of the connection currently owning this session, advanced whenever a
  /// connection is established, torn down, or invalidated. A caller starting asynchronous work
  /// captures it so a later failure report can say which connection the work belonged to -- see
  /// [onUnhealthy]. It carries no meaning beyond equality.
  int get connectionGeneration;

  /// The Host context for the admitted session, or `null` before admission or after teardown.
  /// @return The current session's Host identity and metadata, or `null` when no session is admitted.
  DovahLinkHost? get currentHost;

  /// The Known Host relationship admitted for this session or retained for bounded recovery.
  /// Candidate sessions return `null` even when the candidate claims a saved Host ID.
  DovahLinkHostId? get currentKnownHostId;

  /// The active connection endpoint while connected or reauthenticating.
  /// @return The endpoint for the active transport, or `null` while disconnected or connecting.
  Uri? get currentEndpoint;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason;

  /// Whether terminal client shutdown has started.
  bool get isTerminallyClosed;

  /// Establishes the transport connection to [uri]. An attempt that has not completed within the
  /// centrally tuned connect timeout is abandoned rather than left to resolve indefinitely, so it
  /// can never block automatic recovery's own deadline. [knownHostId] binds visible session state
  /// to the exact saved relationship; candidate connections leave it `null`.
  /// @param knownHostId The selected durable Known Host relationship, or `null` for a candidate.
  /// @throws [DovahLinkConnectionException] if the socket cannot be established, including when
  /// the attempt times out.
  Future<void> connect(Uri uri, {DovahLinkHostId? knownHostId});

  /// Associates the active session with a durable Known Host created by successful pairing.
  /// @param hostId The Known Host relationship made durable by the active session.
  /// @throws [DovahLinkConnectionException] if the active session does not identify [hostId].
  void associateKnownHost(DovahLinkHostId hostId);

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
  ///
  /// [connectionGeneration], when supplied, is the [ISessionService.connectionGeneration] captured
  /// when the failing work began. A report whose connection has since ended or been superseded is
  /// ignored: an asynchronous failure belonging to an old connection must never start recovery for
  /// it after a deliberate disconnect or recovery give-up, nor tear down a newer connection.
  /// @param reason Why the connection is considered unhealthy.
  /// @param connectionGeneration The connection the failing work belonged to, or `null` for a
  /// report about whichever connection is current.
  void onUnhealthy(Exception reason, {int? connectionGeneration});

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
  /// Host detects before it can correlate a reply -- for example before decoding completes, or
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

  /// The bounded wait allowed for one [IDovahLinkTransport.connect] attempt before it is
  /// abandoned as failed. Defaults to the centrally tuned [kConnectTimeout]; overridable so a test
  /// can exercise timeout handling with millisecond-scale delays instead of real seconds.
  final Duration _connectTimeout;

  /// Invalidates ordinary-loss recovery handoffs that have not yet reached admission.
  int _recoveryHandoffGeneration = 0;

  /// Whether this client's session lifecycle has been permanently closed.
  bool _isTerminallyClosed = false;

  /// Creates a session service over [transport] and [state], coordinating teardown through
  /// [teardownCoordinator], serializing lifecycle operations through [lifecycleQueue], and
  /// bounding each connect attempt by [connectTimeout].
  SessionService({
    required IDovahLinkTransport transport,
    required SessionState state,
    required LifecycleOperationQueue lifecycleQueue,
    required ConnectionTeardownCoordinator teardownCoordinator,
    Duration connectTimeout = kConnectTimeout,
  }) : _transport = transport,
       _state = state,
       _lifecycleQueue = lifecycleQueue,
       _teardownCoordinator = teardownCoordinator,
       _connectTimeout = connectTimeout;

  /// Notified after a real (non-duplicate) teardown, so pending operations can be failed or
  /// orphaned. `null` until [DovahLinkClient] assigns an [IRequestService.failAll] tear-off after
  /// constructing it -- this service is built before its `RequestService`, which itself depends on
  /// `ISessionService`, so a constructor dependency in this direction would cycle; see
  /// `ai/context/sdk/architecture.md`'s "Callbacks".
  void Function(Exception reason, {required bool orphanRetrySafeOperations})?
  onTeardown;

  /// Notified when ordinary transport loss finishes tearing down the connection, so bounded
  /// automatic reconnect may begin. Carries the durable relationship selected for the lost
  /// session; candidate sessions pass `null` even when they claim a saved Host ID. `null` until
  /// [DovahLinkClient] assigns it after constructing `ReconnectService` -- the same
  /// construction-order reasoning as [onTeardown] applies, since `ReconnectService` itself depends
  /// on `ISessionService`.
  void Function(Uri uri, [DovahLinkHostId? knownHostId])?
  onOrdinaryTransportLoss;

  /// Receives each inbound message this session's subscription reads, already filtered to the
  /// current connection generation. `null` until `DovahLinkClient` assigns
  /// `requestService.handleIncoming` -- the same construction-order reasoning as [onTeardown]
  /// applies, since `RequestService` itself depends on `ISessionService`.
  void Function(String raw)? onIncomingMessage;

  /// Implements [ISessionService.connectionState].
  @override
  DovahLinkConnectionState get connectionState => _state.connectionState;

  /// Implements [ISessionService.connectionStateChanges].
  @override
  Stream<DovahLinkConnectionState> get connectionStateChanges =>
      _state.connectionStateChanges;

  /// Implements [ISessionService.knownHostSessionChanges].
  @override
  Stream<KnownHostSessionSnapshot> get knownHostSessionChanges =>
      _state.knownHostSessionChanges;

  /// Implements [ISessionService.knownHostInvalidations].
  @override
  Stream<DovahLinkKnownHostInvalidation> get knownHostInvalidations =>
      _state.knownHostInvalidations;

  /// Implements [ISessionService.currentSessionId].
  @override
  String? get currentSessionId => _state.sessionId;

  /// Implements [ISessionService.currentTrustState].
  @override
  DovahLinkTrustState? get currentTrustState => _state.trustState;

  /// Implements [ISessionService.connectionGeneration].
  @override
  int get connectionGeneration => _state.connectionGeneration;

  /// Implements [ISessionService.currentHost].
  @override
  DovahLinkHost? get currentHost => _state.currentHost;

  /// Implements [ISessionService.currentKnownHostId].
  @override
  DovahLinkHostId? get currentKnownHostId => _state.knownHostId;

  /// Implements [ISessionService.currentEndpoint].
  @override
  Uri? get currentEndpoint => _state.currentEndpoint;

  /// Implements [ISessionService.invalidationReason].
  @override
  AdministrativeInvalidationReason? get invalidationReason =>
      _state.invalidationReason;

  /// Implements [ISessionService.isTerminallyClosed].
  @override
  bool get isTerminallyClosed => _isTerminallyClosed;

  /// Implements [ISessionService.connect]. One attempt within a bounded-reconnect cycle (entered
  /// while [connectionState] is already `reconnecting`) keeps [connectionState] at `reconnecting`
  /// while the socket itself is being established, instead of passing through
  /// `connecting`/`disconnected`, so recovery stays outwardly visible as one continuous
  /// `reconnecting` phase rather than flickering between attempts; once the socket actually
  /// connects, [SessionState.markConnected] moves such an attempt to `reauthenticating` rather than
  /// `connected` -- trust is not yet confirmed until the caller's own `hello` following this method
  /// admits a session. An ordinary, non-recovery call still shows the normal `connecting` ->
  /// `connected`/`disconnected` transition. See [SessionState.beginConnectAttempt]. An attempt
  /// exceeding [_connectTimeout] is abandoned via [IDovahLinkTransport.close] -- so a socket
  /// handshake that never completes cannot block this method, or transitively `ReconnectService`'s
  /// own bounded-recovery deadline, indefinitely -- and fails with [DovahLinkConnectionException]
  /// the same as any other connect failure.
  @override
  Future<void> connect(Uri uri, {DovahLinkHostId? knownHostId}) =>
      _lifecycleQueue.run(() async {
        if (_isTerminallyClosed) {
          throw const DovahLinkConnectionException(
            'Cannot connect after the client has been closed.',
          );
        }
        if (knownHostId == null) {
          _state.beginConnectAttempt(uri);
        } else {
          _state.beginConnectAttempt(uri, knownHostId: knownHostId);
        }
        try {
          await _transport.connect(uri).timeout(_connectTimeout);
          _state.markConnected();
          _startReceiving();
        } on Object catch (error) {
          if (error is TimeoutException) {
            unawaited(_transport.close());
          }
          _state.markConnectFailed();
          throw DovahLinkConnectionException(
            'Failed to connect to $uri: $error',
            httpStatusCode: error is WebSocketException
                ? error.httpStatusCode
                : null,
          );
        }
      });

  /// Implements [ISessionService.associateKnownHost].
  @override
  void associateKnownHost(DovahLinkHostId hostId) {
    if (_state.connectionState != DovahLinkConnectionState.connected ||
        _state.currentHost?.hostId != hostId.value) {
      throw const DovahLinkConnectionException(
        'The current session does not match the Known Host relationship.',
      );
    }
    _state.associateKnownHost(hostId);
  }

  /// Implements [ISessionService.disconnect].
  @override
  Future<void> disconnect({
    bool orphanRetrySafeOperations = false,
    Exception? reason,
  }) {
    _recoveryHandoffGeneration++;
    return _teardownCoordinator.tearDown(
      reason ?? const DovahLinkConnectionException('Disconnected.'),
      orphanRetrySafeOperations: orphanRetrySafeOperations,
    );
  }

  /// Permanently closes this client's session lifecycle and disconnects its active session.
  Future<void> close() {
    _isTerminallyClosed = true;
    return disconnect();
  }

  /// Implements [ISessionService.onUnhealthy]. Ordinary transport loss: tears down, and -- only if
  /// that teardown was not raced by a concurrent administrative invalidation or explicit
  /// disconnect, and both a last-connected URI and [onOrdinaryTransportLoss] are available --
  /// resolves directly to `reconnecting` and hands off to attempt bounded automatic recovery. A
  /// report carrying a [connectionGeneration] that is no longer current belongs to an ended
  /// connection and is dropped without any state change, so it can neither restart recovery after a
  /// deliberate disconnect or give-up nor tear down a newer connection.
  @override
  void onUnhealthy(Exception reason, {int? connectionGeneration}) {
    if (connectionGeneration != null &&
        connectionGeneration != _state.connectionGeneration) {
      return;
    }
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
  /// following socket close the Host itself performs cannot overwrite this typed reason back to
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
  /// Host-reported classification rather than a generic malformed-message reason.
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
          const DovahLinkConnectionException('Connection closed by the host.'),
        ),
      ),
    );
  }

  /// Runs teardown after an ordinary loss, then starts bounded recovery if its handoff generation
  /// is still current and the connection remains eligible. Eligibility (a current handoff
  /// generation, a last-connected URI, an [onOrdinaryTransportLoss] callback, and a client not
  /// permanently closed) is decided inside teardown at its reset point, so an eligible session goes
  /// directly to `reconnecting` without a transient `disconnected`. SessionState retains the
  /// selected Known Host relationship ID across this teardown so it can be handed to
  /// ReconnectService. The recovery start runs as a queued step after teardown, serialized with
  /// connect and disconnect operations, and is skipped if the handoff went stale in between.
  Future<void> _beginRecoveryAfterOrdinaryTransportLoss(
    Exception reason,
  ) async {
    final int handoffGeneration = _recoveryHandoffGeneration;
    final DovahLinkHostId? knownHostId = _state.knownHostId;
    final bool enteredRecovery = await _teardownCoordinator.tearDown(
      reason,
      canRecover: () =>
          handoffGeneration == _recoveryHandoffGeneration &&
          !_isTerminallyClosed &&
          _state.lastConnectedUri != null &&
          onOrdinaryTransportLoss != null,
    );
    if (!enteredRecovery) {
      return;
    }
    await _lifecycleQueue.run(() async {
      if (handoffGeneration != _recoveryHandoffGeneration ||
          _state.connectionState != DovahLinkConnectionState.reconnecting) {
        return;
      }
      final Uri? uri = _state.lastConnectedUri;
      final void Function(Uri uri, [DovahLinkHostId? knownHostId])? observer =
          onOrdinaryTransportLoss;
      if (uri == null || observer == null) {
        return;
      }
      observer(uri, knownHostId);
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
