import 'package:dovahlink_client_sdk/src/dovahlink_character.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/game_time_state.dart';
import 'package:dovahlink_client_sdk/src/state/player_location_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import 'package:dovahlink_client_sdk/src/state/tracked_quests_state.dart';

/// Exposes the admitted session's Host context and grouped domain views.
///
/// The Host context describes the SDK session, not Flutter's selected card. Its trust state may be
/// `unpaired`; the reported Host ID is not cryptographic proof of the peer's identity.
abstract interface class IDovahLinkCurrentHost {
  /// The Host context for the admitted session, or `null` before admission or after teardown.
  DovahLinkHost? get host;

  /// The admitted session's trust standing, or `null` before admission.
  DovahLinkTrustState? get trustState;

  /// The admitted session identifier, or `null` before admission.
  String? get sessionId;

  /// The typed game-state views grouped by owning domain.
  IDovahLinkCharacter get character;

  /// Replays the player-location Snapshot and its synchronization status.
  Stream<StateSynchronization<PlayerLocationState?>> get playerLocationChanges;

  /// Replays the Skyrim calendar Snapshot and its synchronization status.
  Stream<StateSynchronization<GameTimeState?>> get gameTimeChanges;

  /// Replays all tracked quests and their synchronization status.
  Stream<StateSynchronization<TrackedQuestsState?>> get trackedQuestsChanges;

  /// Adds [area] to the desired state domains and synchronizes the complete set with the Host.
  /// @param area The state domain to request.
  /// @return The state domains rejected by the Host.
  /// @throws [DovahLinkConnectionException] if there is no trusted session.
  /// @throws [DovahLinkProtocolException] if the Host returns a malformed acknowledgement.
  Future<Set<DovahLinkStateArea>> subscribeStateArea(DovahLinkStateArea area);

  /// Removes [area] from desired state domains and synchronizes the complete set with the Host.
  /// @param area The state domain to remove.
  /// @return The state domains rejected by the Host.
  /// @throws [DovahLinkConnectionException] if there is no trusted session.
  /// @throws [DovahLinkProtocolException] if the Host returns a malformed acknowledgement.
  Future<Set<DovahLinkStateArea>> unsubscribeStateArea(DovahLinkStateArea area);
}

/// Implements [IDovahLinkCurrentHost] over the existing session and Character owners.
class DovahLinkCurrentHost implements IDovahLinkCurrentHost {
  /// Owns the admitted Host context, session ID, and trust state.
  final ISessionService _sessionService;

  /// Owns the grouped Character state view.
  final IDovahLinkCharacter _character;

  /// Owns desired state-area subscriptions.
  final ISubscriptionService _subscriptionService;

  /// The current player's location synchronization stream.
  final Stream<StateSynchronization<PlayerLocationState?>>
  _playerLocationChanges;

  /// The current Skyrim calendar synchronization stream.
  final Stream<StateSynchronization<GameTimeState?>> _gameTimeChanges;

  /// The current tracked-quests synchronization stream.
  final Stream<StateSynchronization<TrackedQuestsState?>> _trackedQuestsChanges;

  /// Creates the current Host view over the existing session and domain owners.
  /// @param sessionService Owns the admitted session context.
  /// @param character Exposes the existing Character domain streams.
  /// @param playerLocationChanges Replays the current player-location synchronization state.
  /// @param gameTimeChanges Replays the current Skyrim calendar synchronization state.
  /// @param trackedQuestsChanges Replays the current tracked-quests synchronization state.
  /// @param subscriptionService Owns desired state subscription operations.
  DovahLinkCurrentHost({
    required ISessionService sessionService,
    required IDovahLinkCharacter character,
    required Stream<StateSynchronization<PlayerLocationState?>>
    playerLocationChanges,
    required Stream<StateSynchronization<GameTimeState?>> gameTimeChanges,
    required Stream<StateSynchronization<TrackedQuestsState?>>
    trackedQuestsChanges,
    required ISubscriptionService subscriptionService,
  }) : _sessionService = sessionService,
       _character = character,
       _playerLocationChanges = playerLocationChanges,
       _gameTimeChanges = gameTimeChanges,
       _trackedQuestsChanges = trackedQuestsChanges,
       _subscriptionService = subscriptionService;

  /// Implements [IDovahLinkCurrentHost.host].
  @override
  DovahLinkHost? get host => _sessionService.currentHost;

  /// Implements [IDovahLinkCurrentHost.trustState].
  @override
  DovahLinkTrustState? get trustState => _sessionService.currentTrustState;

  /// Implements [IDovahLinkCurrentHost.sessionId].
  @override
  String? get sessionId => _sessionService.currentSessionId;

  /// Implements [IDovahLinkCurrentHost.character].
  @override
  IDovahLinkCharacter get character => _character;

  /// Implements [IDovahLinkCurrentHost.playerLocationChanges].
  @override
  Stream<StateSynchronization<PlayerLocationState?>>
  get playerLocationChanges => _playerLocationChanges;

  /// Implements [IDovahLinkCurrentHost.gameTimeChanges].
  @override
  Stream<StateSynchronization<GameTimeState?>> get gameTimeChanges =>
      _gameTimeChanges;

  /// Implements [IDovahLinkCurrentHost.trackedQuestsChanges].
  @override
  Stream<StateSynchronization<TrackedQuestsState?>> get trackedQuestsChanges =>
      _trackedQuestsChanges;

  /// Implements [IDovahLinkCurrentHost.subscribeStateArea].
  @override
  Future<Set<DovahLinkStateArea>> subscribeStateArea(DovahLinkStateArea area) =>
      _subscriptionService.subscribeStateArea(area);

  /// Implements [IDovahLinkCurrentHost.unsubscribeStateArea].
  @override
  Future<Set<DovahLinkStateArea>> unsubscribeStateArea(
    DovahLinkStateArea area,
  ) => _subscriptionService.unsubscribeStateArea(area);
}
