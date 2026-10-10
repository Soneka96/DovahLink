import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_invalidation.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The exact durable relationship and its current session lifecycle.
typedef KnownHostSessionSnapshot = ({
  DovahLinkHostId? hostId,
  DovahLinkKnownHostSessionState state,
});

/// The single authoritative owner of every session-scoped mutable fact this engine has, per
/// `ai/context/sdk/architecture.md`'s "Session-state ownership". Created exactly once by the
/// composition root and shared by direct reference only with the session subsystem's own internal
/// components (`SessionService`, `SessionAdmissionService`, `SessionTrustService`) that
/// legitimately participate in maintaining it; every other consumer depends on the appropriate
/// Service contract instead. A plain, direct-noun supporting class per
/// `ai/context/sdk/architecture.md`'s "Not everything is a Service" -- it holds state and exposes
/// the small set of transitions that state can undergo, and performs no I/O, correlation, or
/// lifecycle orchestration of its own.
class SessionState {
  /// Creates session state in its initial, disconnected, unauthenticated form.
  SessionState();

  /// The current connection lifecycle phase.
  DovahLinkConnectionState _connectionState =
      DovahLinkConnectionState.disconnected;

  /// Broadcasts every actual [connectionState] transition, per
  /// `ai/context/sdk/api-design.md`'s "New-subscriber state replay". Fed at the end of every
  /// method below that can change [_connectionState]; [CurrentValueStream.update] itself no-ops
  /// when a method leaves the value unchanged, so every call site below updates unconditionally.
  final CurrentValueStream<DovahLinkConnectionState> _connectionStateStream =
      CurrentValueStream<DovahLinkConnectionState>(
        DovahLinkConnectionState.disconnected,
      );

  /// Replays the session lifecycle for the exact Known Host relationship, independent of the
  /// broader connection phase and any unauthenticated Host identity claim.
  final CurrentValueStream<KnownHostSessionSnapshot> _knownHostSessionChanges =
      CurrentValueStream<KnownHostSessionSnapshot>((
        hostId: null,
        state: DovahLinkKnownHostSessionState.disconnected,
      ));

  /// Broadcasts each immutable administrative invalidation for a Known Host session.
  final StreamController<DovahLinkKnownHostInvalidation>
  _knownHostInvalidationsController =
      StreamController<DovahLinkKnownHostInvalidation>.broadcast();

  /// The server-issued session identifier, or `null` before [admit] is called.
  String? _sessionId;

  /// The current trust standing, or `null` before [admit] is called.
  DovahLinkTrustState? _trustState;

  /// The Host context for the admitted session, or `null` when no session is admitted.
  DovahLinkHost? _currentHost;

  /// The durable Known Host relationship selected for this session, or `null` for a candidate.
  DovahLinkHostId? _knownHostId;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? _invalidationReason;

  /// Identifies the current connection attempt. Incremented every time a connection is torn down
  /// or a new connect attempt begins, so a callback belonging to an old connection can recognize it
  /// is stale.
  int _connectionGeneration = 0;

  /// The URI most recently passed to [beginConnectAttempt], used to retry the same endpoint after
  /// ordinary transport loss. `null` before a connect attempt is ever begun.
  Uri? _lastConnectedUri;

  /// The subscription currently reading the transport's inbound message stream, or `null` when no
  /// connection is being received for.
  StreamSubscription<String>? _messageSubscription;

  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState => _connectionState;

  /// A stream of every [connectionState] transition: the current value immediately on listen,
  /// then each subsequent real change. See [_connectionStateStream].
  Stream<DovahLinkConnectionState> get connectionStateChanges =>
      _connectionStateStream.stream;

  /// Emits the exact Known Host relationship and its session lifecycle immediately on listen and
  /// whenever either changes.
  Stream<KnownHostSessionSnapshot> get knownHostSessionChanges =>
      _knownHostSessionChanges.stream;

  /// Emits each Known Host's invalidation with its reason captured in one value.
  Stream<DovahLinkKnownHostInvalidation> get knownHostInvalidations =>
      _knownHostInvalidationsController.stream;

  /// The server-issued session identifier of the current session, or `null` before one is
  /// admitted.
  String? get sessionId => _sessionId;

  /// The current trust standing, or `null` before one is admitted.
  DovahLinkTrustState? get trustState => _trustState;

  /// The Host context for the admitted session, or `null` before admission or after teardown.
  /// @return The current session's Host identity and metadata, or `null` when no session is admitted.
  DovahLinkHost? get currentHost => _currentHost;

  /// The durable relationship associated with the admitted session or pending recovery cycle.
  DovahLinkHostId? get knownHostId => _knownHostId;

  /// The current session lifecycle for [knownHostId], treating an open transport before admission
  /// as `connecting` rather than `connected`.
  DovahLinkKnownHostSessionState get knownHostSessionState {
    if (_knownHostId == null) {
      return DovahLinkKnownHostSessionState.disconnected;
    }
    return switch (_connectionState) {
      DovahLinkConnectionState.disconnected ||
      DovahLinkConnectionState.administrativelyInvalidated =>
        DovahLinkKnownHostSessionState.disconnected,
      DovahLinkConnectionState.connecting =>
        DovahLinkKnownHostSessionState.connecting,
      DovahLinkConnectionState.connected =>
        _sessionId == null || _trustState == null
            ? DovahLinkKnownHostSessionState.connecting
            : DovahLinkKnownHostSessionState.connected,
      DovahLinkConnectionState.reconnecting =>
        DovahLinkKnownHostSessionState.reconnecting,
      DovahLinkConnectionState.reauthenticating =>
        DovahLinkKnownHostSessionState.reauthenticating,
    };
  }

  /// The active connection endpoint while connected or reauthenticating.
  /// @return The endpoint for the active transport, or `null` while disconnected or connecting.
  Uri? get currentEndpoint =>
      _connectionState == DovahLinkConnectionState.connected ||
          _connectionState == DovahLinkConnectionState.reauthenticating
      ? _lastConnectedUri
      : null;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason =>
      _invalidationReason;

  /// The generation currently associated with the active or tearing-down connection.
  int get connectionGeneration => _connectionGeneration;

  /// The URI most recently passed to [beginConnectAttempt], or `null` before one is ever begun.
  Uri? get lastConnectedUri => _lastConnectedUri;

  /// Whether [connectionState] has already reached terminal administrative invalidation.
  bool get isAdministrativelyInvalidated =>
      _connectionState == DovahLinkConnectionState.administrativelyInvalidated;

  /// Begins a connect attempt to [uri]: advances the generation, clears any prior
  /// [invalidationReason] and [currentHost], records [uri] as [lastConnectedUri], and associates
  /// the attempt with [knownHostId] when supplied. Leaves [connectionState] at
  /// [DovahLinkConnectionState.reconnecting] when a bounded-recovery attempt is underway, so
  /// recovery stays outwardly visible as one continuous `reconnecting` phase and retains its Known
  /// Host relationship ID; otherwise transitions to [DovahLinkConnectionState.connecting].
  /// Candidate attempts supply no relationship ID.
  /// @param knownHostId The exact durable relationship being authenticated, or `null` for a candidate.
  void beginConnectAttempt(Uri uri, {DovahLinkHostId? knownHostId}) {
    final bool isRecoveryAttempt =
        _connectionState == DovahLinkConnectionState.reconnecting;
    _connectionGeneration++;
    _invalidationReason = null;
    _currentHost = null;
    if (!isRecoveryAttempt) {
      _knownHostId = knownHostId;
      _connectionState = DovahLinkConnectionState.connecting;
    }
    _lastConnectedUri = uri;
    _connectionStateStream.update(_connectionState);
    _knownHostSessionChanges.update((
      hostId: _knownHostId,
      state: knownHostSessionState,
    ));
  }

  /// Records a successful connect attempt. A bounded-recovery attempt (entered while
  /// [connectionState] is already [DovahLinkConnectionState.reconnecting]) transitions to
  /// [DovahLinkConnectionState.reauthenticating] instead of [DovahLinkConnectionState.connected] --
  /// the transport is back up, but trust is not yet re-established until [admit] follows a
  /// successful `hello`; otherwise transitions directly to [DovahLinkConnectionState.connected].
  void markConnected() {
    _connectionState = _connectionState == DovahLinkConnectionState.reconnecting
        ? DovahLinkConnectionState.reauthenticating
        : DovahLinkConnectionState.connected;
    _connectionStateStream.update(_connectionState);
    _knownHostSessionChanges.update((
      hostId: _knownHostId,
      state: knownHostSessionState,
    ));
  }

  /// Records a failed connect attempt. Leaves [connectionState] untouched when a bounded-recovery
  /// attempt is already in progress -- a failed attempt within a recovery cycle stays
  /// `reconnecting`, since the cycle may still retry; otherwise transitions to
  /// [DovahLinkConnectionState.disconnected].
  void markConnectFailed() {
    if (_connectionState != DovahLinkConnectionState.reconnecting) {
      _connectionState = DovahLinkConnectionState.disconnected;
    }
    _connectionStateStream.update(_connectionState);
    _knownHostSessionChanges.update((
      hostId: _knownHostId,
      state: knownHostSessionState,
    ));
  }

  /// Admits a newly authenticated session, recording [sessionId] and [trustState] and promoting
  /// [connectionState] to [DovahLinkConnectionState.connected] -- the point at which a
  /// bounded-recovery attempt's [DovahLinkConnectionState.reauthenticating] phase resolves to a
  /// trusted, usable session. A no-op transition when [connectionState] is already `connected` (the
  /// ordinary, non-recovery `connect` then `hello` flow).
  /// @param sessionId The server-issued identity for this connection's session.
  /// @param trustState The trust tier admitted by the Host.
  /// @param currentHost The Host identity and endpoint reported for this session.
  void admit({
    required String sessionId,
    required DovahLinkTrustState trustState,
    required DovahLinkHost currentHost,
  }) {
    _sessionId = sessionId;
    _trustState = trustState;
    _currentHost = currentHost;
    _connectionState = DovahLinkConnectionState.connected;
    _connectionStateStream.update(_connectionState);
    _knownHostSessionChanges.update((
      hostId: _knownHostId,
      state: knownHostSessionState,
    ));
  }

  /// Associates the active session with a Known Host created through successful pairing.
  /// @param hostId The durable relationship established by the active pairing session.
  void associateKnownHost(DovahLinkHostId hostId) {
    _knownHostId = hostId;
    _knownHostSessionChanges.update((
      hostId: _knownHostId,
      state: knownHostSessionState,
    ));
  }

  /// Upgrades the current session's trust standing to [DovahLinkTrustState.trusted].
  void markTrusted() {
    _trustState = DovahLinkTrustState.trusted;
  }

  /// Records an authoritative `session_invalidated` push: sets [invalidationReason], transitions
  /// [connectionState] to [DovahLinkConnectionState.administrativelyInvalidated], clears
  /// [sessionId], [trustState], [currentHost], and [knownHostId], and advances the generation.
  /// Terminal for the current session.
  void invalidate(AdministrativeInvalidationReason reason) {
    final DovahLinkHostId? invalidatedHostId = _knownHostId;
    _invalidationReason = reason;
    _connectionState = DovahLinkConnectionState.administrativelyInvalidated;
    _sessionId = null;
    _trustState = null;
    _currentHost = null;
    _knownHostId = null;
    _connectionGeneration++;
    if (invalidatedHostId != null) {
      _knownHostInvalidationsController.add(
        DovahLinkKnownHostInvalidation(
          hostId: invalidatedHostId,
          reason: reason,
        ),
      );
    }
    _connectionStateStream.update(_connectionState);
    _knownHostSessionChanges.update((
      hostId: invalidatedHostId,
      state: DovahLinkKnownHostSessionState.disconnected,
    ));
  }

  /// Closes the discrete invalidation event stream when its owning client shuts down.
  /// @return A future completing after the event stream closes.
  Future<void> close() => _knownHostInvalidationsController.close();

  /// Advances the connection generation so callbacks from an older connection become stale.
  void bumpGeneration() {
    _connectionGeneration++;
  }

  /// Resets connection-scoped identity after transport resources have been closed. Resolves back to
  /// [DovahLinkConnectionState.reconnecting] instead of [DovahLinkConnectionState.disconnected] when
  /// [preserveReconnecting] is `true` and the session was already `reconnecting` or
  /// [DovahLinkConnectionState.reauthenticating] -- an intermediate teardown mid-recovery (including
  /// a failed re-authentication attempt), not recovery's own final give-up. Also resolves directly
  /// to `reconnecting` when [preserveReconnecting] and [beginRecovery] are both `true` for a
  /// session that was not yet recovering, so ordinary transport loss never publishes a transient
  /// `disconnected` between `connected` and `reconnecting`. Always clears [sessionId],
  /// [trustState], and [currentHost]. Retains [knownHostId] when [preserveReconnecting] is `true`
  /// so the recovery handoff can capture the relationship before the next explicit connection
  /// attempt resets it.
  /// @param preserveReconnecting Whether this teardown may leave the session in recovery.
  /// @param beginRecovery Whether a session that was not recovering should enter recovery now.
  /// @return Whether this call moved a not-yet-recovering session into `reconnecting`.
  bool resetAfterTeardown({
    required bool preserveReconnecting,
    bool beginRecovery = false,
  }) {
    final DovahLinkHostId? disconnectedHostId = _knownHostId;
    final bool wasRecovering =
        _connectionState == DovahLinkConnectionState.reconnecting ||
        _connectionState == DovahLinkConnectionState.reauthenticating;
    final bool beginsRecovery =
        preserveReconnecting && beginRecovery && !wasRecovering;
    _connectionState = preserveReconnecting && (wasRecovering || beginsRecovery)
        ? DovahLinkConnectionState.reconnecting
        : DovahLinkConnectionState.disconnected;
    _trustState = null;
    _sessionId = null;
    _currentHost = null;
    if (!preserveReconnecting) {
      _knownHostId = null;
    }
    _connectionStateStream.update(_connectionState);
    _knownHostSessionChanges.update((
      hostId: preserveReconnecting ? _knownHostId : disconnectedHostId,
      state: knownHostSessionState,
    ));
    return beginsRecovery;
  }

  /// Records [subscription] as the one currently reading the transport's inbound message stream.
  void attachMessageSubscription(StreamSubscription<String> subscription) {
    _messageSubscription = subscription;
  }

  /// Detaches and returns the currently recorded message subscription, if any, clearing it so a
  /// later [attachMessageSubscription] can record a fresh one.
  StreamSubscription<String>? detachMessageSubscription() {
    final StreamSubscription<String>? subscription = _messageSubscription;
    _messageSubscription = null;
    return subscription;
  }
}
