import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/character_health_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_magicka_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_stamina_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_xp_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Exposes the admitted session's Host context and typed game-state streams.
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

  /// Emits the current character experience synchronization view and later changes.
  Stream<StateSynchronization<CharacterXpState>> get characterXpChanges;

  /// Emits the current character health synchronization view and later changes.
  Stream<StateSynchronization<CharacterHealthState>> get characterHealthChanges;

  /// Emits the current character magicka synchronization view and later changes.
  Stream<StateSynchronization<CharacterMagickaState>>
  get characterMagickaChanges;

  /// Emits the current character stamina synchronization view and later changes.
  Stream<StateSynchronization<CharacterStaminaState>>
  get characterStaminaChanges;

  /// Emits the current character level synchronization view and later changes.
  Stream<StateSynchronization<CharacterLevelState>> get characterLevelChanges;

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

/// Implements [IDovahLinkCurrentHost] over the existing session, tracker, and subscription owners.
class DovahLinkCurrentHost implements IDovahLinkCurrentHost {
  /// Owns the admitted Host context, session ID, and trust state.
  final ISessionService _sessionService;

  /// Owns desired state-area subscriptions.
  final ISubscriptionService _subscriptionService;

  /// Emits character experience synchronization changes.
  final Stream<StateSynchronization<CharacterXpState>> _characterXpChanges;

  /// Emits character health synchronization changes.
  final Stream<StateSynchronization<CharacterHealthState>>
  _characterHealthChanges;

  /// Emits character magicka synchronization changes.
  final Stream<StateSynchronization<CharacterMagickaState>>
  _characterMagickaChanges;

  /// Emits character stamina synchronization changes.
  final Stream<StateSynchronization<CharacterStaminaState>>
  _characterStaminaChanges;

  /// Emits character level synchronization changes.
  final Stream<StateSynchronization<CharacterLevelState>>
  _characterLevelChanges;

  /// Creates the current Host view over the client's existing session and state owners.
  /// @param sessionService Owns the admitted session context.
  /// @param subscriptionService Owns desired state subscription operations.
  /// @param characterXpChanges The existing experience tracker stream.
  /// @param characterHealthChanges The existing health tracker stream.
  /// @param characterMagickaChanges The existing magicka tracker stream.
  /// @param characterStaminaChanges The existing stamina tracker stream.
  /// @param characterLevelChanges The existing level tracker stream.
  DovahLinkCurrentHost({
    required ISessionService sessionService,
    required ISubscriptionService subscriptionService,
    required Stream<StateSynchronization<CharacterXpState>> characterXpChanges,
    required Stream<StateSynchronization<CharacterHealthState>>
    characterHealthChanges,
    required Stream<StateSynchronization<CharacterMagickaState>>
    characterMagickaChanges,
    required Stream<StateSynchronization<CharacterStaminaState>>
    characterStaminaChanges,
    required Stream<StateSynchronization<CharacterLevelState>>
    characterLevelChanges,
  }) : _sessionService = sessionService,
       _subscriptionService = subscriptionService,
       _characterXpChanges = characterXpChanges,
       _characterHealthChanges = characterHealthChanges,
       _characterMagickaChanges = characterMagickaChanges,
       _characterStaminaChanges = characterStaminaChanges,
       _characterLevelChanges = characterLevelChanges;

  /// Implements [IDovahLinkCurrentHost.host].
  @override
  DovahLinkHost? get host => _sessionService.currentHost;

  /// Implements [IDovahLinkCurrentHost.trustState].
  @override
  DovahLinkTrustState? get trustState => _sessionService.currentTrustState;

  /// Implements [IDovahLinkCurrentHost.sessionId].
  @override
  String? get sessionId => _sessionService.currentSessionId;

  /// Implements [IDovahLinkCurrentHost.characterXpChanges].
  @override
  Stream<StateSynchronization<CharacterXpState>> get characterXpChanges =>
      _characterXpChanges;

  /// Implements [IDovahLinkCurrentHost.characterHealthChanges].
  @override
  Stream<StateSynchronization<CharacterHealthState>>
  get characterHealthChanges => _characterHealthChanges;

  /// Implements [IDovahLinkCurrentHost.characterMagickaChanges].
  @override
  Stream<StateSynchronization<CharacterMagickaState>>
  get characterMagickaChanges => _characterMagickaChanges;

  /// Implements [IDovahLinkCurrentHost.characterStaminaChanges].
  @override
  Stream<StateSynchronization<CharacterStaminaState>>
  get characterStaminaChanges => _characterStaminaChanges;

  /// Implements [IDovahLinkCurrentHost.characterLevelChanges].
  @override
  Stream<StateSynchronization<CharacterLevelState>> get characterLevelChanges =>
      _characterLevelChanges;

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
