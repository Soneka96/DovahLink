import 'package:dovahlink_client_sdk/src/protocol/gameplay_readiness_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The single pre-admission readiness observation held until session admission decides its fate.
typedef StagedGameplayReadiness = ({
  String sessionId,
  String stateAuthorityId,
  GameplayReadinessPayload payload,
});

/// Owns this client's accepted gameplay-readiness fact, per `ai/context/sdk/architecture.md`'s
/// "Gameplay readiness". The Host decides whether gameplay is ready; this owner only decides
/// whether a Host report is valid for the current session, and otherwise reports `unknown`.
abstract interface class IGameplayReadinessService {
  /// The current readiness: the last valid Host report for the current session, or
  /// [DovahLinkGameplayReadiness.unknown].
  DovahLinkGameplayReadiness get current;

  /// Replays [current] immediately on listen, then each real transition.
  Stream<DovahLinkGameplayReadiness> get changes;

  /// Considers one decoded Host readiness message. Before a session is admitted it is staged
  /// (only the newest is kept) and decided at [onSessionAdmitted]; afterwards it is applied only
  /// if it belongs to the current session, is newer than every message already applied, and does
  /// not come from a state authority already superseded. Anything else is silently discarded.
  /// A session that supersedes more authorities than can be remembered stays `unknown` until it ends.
  /// @param sessionId The envelope's session identity.
  /// @param stateAuthorityId The envelope's state-authority continuity identity.
  /// @param payload The decoded readiness payload.
  void observe({
    required String sessionId,
    required String stateAuthorityId,
    required GameplayReadinessPayload payload,
  });

  /// Records that [sessionId] was admitted under [stateAuthorityId] and decides the staged
  /// observation: it is applied when it belongs to this session and is discarded otherwise. Called
  /// only after Host version and identity validation. Readiness stays `unknown` until a message is
  /// applied.
  /// @param sessionId The admitted session identity.
  /// @param stateAuthorityId The authority reported by the session's `hello_ack`.
  void onSessionAdmitted({
    required String sessionId,
    required String stateAuthorityId,
  });

  /// Records the state authority another message of the current session revealed. A different
  /// authority supersedes the previous one: readiness becomes `unknown` until a message under the
  /// new authority is applied, and a later message from a superseded authority is discarded.
  /// Ignored without an admitted session or once the authority history is exhausted.
  /// @param stateAuthorityId The authority carried by a state message routed to a domain.
  void onStateAuthorityObserved(String stateAuthorityId);

  /// Forgets the session, its sequence, its authority history, and any staged observation, and
  /// returns to `unknown`. Called whenever a connection ends, however it ends. A new connection
  /// attempt always follows such an ending, so none begins with stale readiness.
  void reset();

  /// Resets and then ignores every later call. Called when the client closes.
  void close();
}

/// Implements [IGameplayReadinessService].
class GameplayReadinessService implements IGameplayReadinessService {
  /// The most superseded authorities remembered for one session. Authority identifiers are opaque,
  /// so a forgotten one could not be told from a new one. Rather than forget the oldest, the
  /// session fails closed when this is exceeded: see [_authorityHistoryExhausted].
  static const int maxRememberedSupersededAuthorities = 32;

  /// Replays the current readiness to every subscriber.
  final CurrentValueStream<DovahLinkGameplayReadiness> _readiness =
      CurrentValueStream<DovahLinkGameplayReadiness>(
        DovahLinkGameplayReadiness.unknown,
      );

  /// Authorities this session has moved past, oldest first.
  final List<String> _supersededAuthorities = <String>[];

  /// Whether this session has superseded more authorities than [maxRememberedSupersededAuthorities].
  /// Readiness then stays `unknown` and every later observation is ignored until the session ends,
  /// because a message could no longer be proven to come from the current authority.
  bool _authorityHistoryExhausted = false;

  /// The admitted session readiness may currently be applied to, or `null` before admission.
  String? _sessionId;

  /// The newest state authority this session has revealed, or `null` before admission.
  String? _authorityId;

  /// The sequence of the last applied message of this session; `0` before the first.
  int _lastSequence = 0;

  /// The newest observation received before admission, or `null`.
  StagedGameplayReadiness? _staged;

  /// Whether [close] has run.
  bool _closed = false;

  /// Implements [IGameplayReadinessService.current].
  @override
  DovahLinkGameplayReadiness get current => _readiness.value;

  /// Implements [IGameplayReadinessService.changes].
  @override
  Stream<DovahLinkGameplayReadiness> get changes => _readiness.stream;

  /// Implements [IGameplayReadinessService.observe].
  @override
  void observe({
    required String sessionId,
    required String stateAuthorityId,
    required GameplayReadinessPayload payload,
  }) {
    if (_closed) {
      return;
    }
    final String? admittedSessionId = _sessionId;
    if (admittedSessionId == null) {
      _stage((
        sessionId: sessionId,
        stateAuthorityId: stateAuthorityId,
        payload: payload,
      ));
      return;
    }
    if (sessionId != admittedSessionId ||
        _authorityHistoryExhausted ||
        payload.sequence <= _lastSequence ||
        _supersededAuthorities.contains(stateAuthorityId)) {
      return;
    }

    _adoptAuthority(stateAuthorityId);
    if (_authorityHistoryExhausted) {
      _readiness.update(DovahLinkGameplayReadiness.unknown);
      return;
    }
    _lastSequence = payload.sequence;
    _readiness.update(
      payload.ready
          ? DovahLinkGameplayReadiness.ready
          : DovahLinkGameplayReadiness.notReady,
    );
  }

  /// Implements [IGameplayReadinessService.onSessionAdmitted].
  @override
  void onSessionAdmitted({
    required String sessionId,
    required String stateAuthorityId,
  }) {
    if (_closed) {
      return;
    }
    final StagedGameplayReadiness? staged = _staged;
    reset();
    _sessionId = sessionId;
    _authorityId = stateAuthorityId;
    if (staged != null && staged.sessionId == sessionId) {
      observe(
        sessionId: staged.sessionId,
        stateAuthorityId: staged.stateAuthorityId,
        payload: staged.payload,
      );
    }
  }

  /// Implements [IGameplayReadinessService.onStateAuthorityObserved].
  @override
  void onStateAuthorityObserved(String stateAuthorityId) {
    if (_closed ||
        _sessionId == null ||
        _authorityHistoryExhausted ||
        _supersededAuthorities.contains(stateAuthorityId)) {
      return;
    }
    if (_adoptAuthority(stateAuthorityId)) {
      _readiness.update(DovahLinkGameplayReadiness.unknown);
    }
  }

  /// Implements [IGameplayReadinessService.reset].
  @override
  void reset() {
    if (_closed) {
      return;
    }
    _sessionId = null;
    _authorityId = null;
    _lastSequence = 0;
    _staged = null;
    _supersededAuthorities.clear();
    _authorityHistoryExhausted = false;
    _readiness.update(DovahLinkGameplayReadiness.unknown);
  }

  /// Implements [IGameplayReadinessService.close].
  @override
  void close() {
    reset();
    _closed = true;
  }

  /// Keeps [observation] when it is the first pending one, belongs to a different session, or is
  /// newer than the pending one of the same session. A different session wins because it arrived
  /// later on the one inbound reader; the SDK has no other way to order two session identities, and
  /// a callback from an ended connection never reaches this service (its subscription is cancelled).
  /// @param observation The observation received before admission.
  void _stage(StagedGameplayReadiness observation) {
    final StagedGameplayReadiness? pending = _staged;
    if (pending == null ||
        pending.sessionId != observation.sessionId ||
        observation.payload.sequence > pending.payload.sequence) {
      _staged = observation;
    }
  }

  /// Makes [stateAuthorityId] the session's newest authority, remembering the one it replaces, or
  /// marking the history exhausted when there is no room to remember it.
  /// @param stateAuthorityId The authority just revealed.
  /// @return Whether this changed the session's authority.
  bool _adoptAuthority(String stateAuthorityId) {
    final String? previous = _authorityId;
    if (previous == stateAuthorityId) {
      return false;
    }
    if (previous != null) {
      if (_supersededAuthorities.length >= maxRememberedSupersededAuthorities) {
        _authorityHistoryExhausted = true;
      } else {
        _supersededAuthorities.add(previous);
      }
    }
    _authorityId = stateAuthorityId;
    return true;
  }
}
