import 'dart:async';

import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The single authoritative owner of every session-scoped mutable fact this engine has, per
/// `ai/context/sdk/architecture.md`'s "Session-state ownership". Created exactly once by the
/// composition root and shared by direct reference only with the session subsystem's own internal
/// components (`SessionService`, `SessionAdmissionServiceImpl`, `SessionTrustServiceImpl`) that
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

  /// The server-issued session identifier, or `null` before [admit] is called.
  String? _sessionId;

  /// The current trust standing, or `null` before [admit] is called.
  DovahLinkTrustState? _trustState;

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

  /// The server-issued session identifier of the current session, or `null` before one is
  /// admitted.
  String? get sessionId => _sessionId;

  /// The current trust standing, or `null` before one is admitted.
  DovahLinkTrustState? get trustState => _trustState;

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
  /// [invalidationReason], and records [uri] as [lastConnectedUri]. Leaves [connectionState] at
  /// [DovahLinkConnectionState.reconnecting] when a bounded-recovery attempt is already in
  /// progress, so recovery stays outwardly visible as one continuous `reconnecting` phase instead
  /// of flickering through `connecting`; otherwise transitions to
  /// [DovahLinkConnectionState.connecting].
  void beginConnectAttempt(Uri uri) {
    final bool isRecoveryAttempt =
        _connectionState == DovahLinkConnectionState.reconnecting;
    _connectionGeneration++;
    _invalidationReason = null;
    if (!isRecoveryAttempt) {
      _connectionState = DovahLinkConnectionState.connecting;
    }
    _lastConnectedUri = uri;
    _connectionStateStream.update(_connectionState);
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
  }

  /// Transitions to [DovahLinkConnectionState.reconnecting], entered only after ordinary transport
  /// loss tears down cleanly with a known endpoint and eligible bounded recovery.
  void markReconnecting() {
    _connectionState = DovahLinkConnectionState.reconnecting;
    _connectionStateStream.update(_connectionState);
  }

  /// Admits a newly authenticated session, recording [sessionId] and [trustState] and promoting
  /// [connectionState] to [DovahLinkConnectionState.connected] -- the point at which a
  /// bounded-recovery attempt's [DovahLinkConnectionState.reauthenticating] phase resolves to a
  /// trusted, usable session. A no-op transition when [connectionState] is already `connected` (the
  /// ordinary, non-recovery `connect` then `hello` flow).
  void admit({
    required String sessionId,
    required DovahLinkTrustState trustState,
  }) {
    _sessionId = sessionId;
    _trustState = trustState;
    _connectionState = DovahLinkConnectionState.connected;
    _connectionStateStream.update(_connectionState);
  }

  /// Upgrades the current session's trust standing to [DovahLinkTrustState.trusted].
  void markTrusted() {
    _trustState = DovahLinkTrustState.trusted;
  }

  /// Records an authoritative `session_invalidated` push: sets [invalidationReason], transitions
  /// [connectionState] to [DovahLinkConnectionState.administrativelyInvalidated], clears
  /// [sessionId] and [trustState], and advances the generation. Terminal for the current session.
  void invalidate(AdministrativeInvalidationReason reason) {
    _invalidationReason = reason;
    _connectionState = DovahLinkConnectionState.administrativelyInvalidated;
    _sessionId = null;
    _trustState = null;
    _connectionGeneration++;
    _connectionStateStream.update(_connectionState);
  }

  /// Advances the connection generation so callbacks from an older connection become stale.
  void bumpGeneration() {
    _connectionGeneration++;
  }

  /// Resets connection-scoped identity after transport resources have been closed. Resolves back to
  /// [DovahLinkConnectionState.reconnecting] instead of [DovahLinkConnectionState.disconnected] when
  /// [preserveReconnecting] is `true` and the session was already `reconnecting` or
  /// [DovahLinkConnectionState.reauthenticating] -- an intermediate teardown mid-recovery (including
  /// a failed re-authentication attempt), not recovery's own final give-up. Always clears
  /// [sessionId] and [trustState], regardless of which connection phase results.
  void resetAfterTeardown({required bool preserveReconnecting}) {
    final bool wasRecovering =
        _connectionState == DovahLinkConnectionState.reconnecting ||
        _connectionState == DovahLinkConnectionState.reauthenticating;
    _connectionState = preserveReconnecting && wasRecovering
        ? DovahLinkConnectionState.reconnecting
        : DovahLinkConnectionState.disconnected;
    _trustState = null;
    _sessionId = null;
    _connectionStateStream.update(_connectionState);
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
