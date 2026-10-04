import 'package:dovahlink_client_sdk/src/state/character_identity_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_supernatural_traits_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_vitals_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_xp_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Exposes the independently synchronized Character domains for the admitted Host.
abstract interface class IDovahLinkCharacter {
  /// Replays the coherent Vitals state and emits later Snapshot changes.
  Stream<StateSynchronization<CharacterVitalsState>> get vitalsChanges;

  /// Replays the experience state and emits later Snapshot changes.
  Stream<StateSynchronization<CharacterXpState>> get xpChanges;

  /// Replays the Level baseline and emits later Level Event changes.
  Stream<StateSynchronization<CharacterLevelState>> get levelChanges;

  /// Replays the complete Identity state, or explicit unavailability.
  Stream<StateSynchronization<CharacterIdentityState?>> get identityChanges;

  /// Replays the three independent supernatural-traits values, or unavailability.
  Stream<StateSynchronization<CharacterSupernaturalTraitsState?>>
  get supernaturalTraitsChanges;
}

/// Projects the Character domain streams owned by the SDK's state module.
class DovahLinkCharacter implements IDovahLinkCharacter {
  /// The coherent Vitals synchronization stream.
  final Stream<StateSynchronization<CharacterVitalsState>> _vitalsChanges;

  /// The Character XP synchronization stream.
  final Stream<StateSynchronization<CharacterXpState>> _xpChanges;

  /// The Character Level synchronization stream.
  final Stream<StateSynchronization<CharacterLevelState>> _levelChanges;

  /// The Character Identity synchronization stream.
  final Stream<StateSynchronization<CharacterIdentityState?>> _identityChanges;

  /// The supernatural-traits synchronization stream.
  final Stream<StateSynchronization<CharacterSupernaturalTraitsState?>>
  _supernaturalTraitsChanges;

  /// Creates the Character view over its existing state trackers.
  /// @param vitalsChanges The Vitals tracker stream.
  /// @param xpChanges The XP tracker stream.
  /// @param levelChanges The Level tracker stream.
  /// @param identityChanges The Identity tracker stream.
  /// @param supernaturalTraitsChanges The supernatural-traits tracker stream.
  DovahLinkCharacter({
    required Stream<StateSynchronization<CharacterVitalsState>> vitalsChanges,
    required Stream<StateSynchronization<CharacterXpState>> xpChanges,
    required Stream<StateSynchronization<CharacterLevelState>> levelChanges,
    required Stream<StateSynchronization<CharacterIdentityState?>>
    identityChanges,
    required Stream<StateSynchronization<CharacterSupernaturalTraitsState?>>
    supernaturalTraitsChanges,
  }) : _vitalsChanges = vitalsChanges,
       _xpChanges = xpChanges,
       _levelChanges = levelChanges,
       _identityChanges = identityChanges,
       _supernaturalTraitsChanges = supernaturalTraitsChanges;

  /// Implements [IDovahLinkCharacter.vitalsChanges].
  @override
  Stream<StateSynchronization<CharacterVitalsState>> get vitalsChanges =>
      _vitalsChanges;

  /// Implements [IDovahLinkCharacter.xpChanges].
  @override
  Stream<StateSynchronization<CharacterXpState>> get xpChanges => _xpChanges;

  /// Implements [IDovahLinkCharacter.levelChanges].
  @override
  Stream<StateSynchronization<CharacterLevelState>> get levelChanges =>
      _levelChanges;

  /// Implements [IDovahLinkCharacter.identityChanges].
  @override
  Stream<StateSynchronization<CharacterIdentityState?>> get identityChanges =>
      _identityChanges;

  /// Implements [IDovahLinkCharacter.supernaturalTraitsChanges].
  @override
  Stream<StateSynchronization<CharacterSupernaturalTraitsState?>>
  get supernaturalTraitsChanges => _supernaturalTraitsChanges;
}
