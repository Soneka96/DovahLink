import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';
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
}

/// Projects the Character domain streams owned by the SDK's state module.
class DovahLinkCharacter implements IDovahLinkCharacter {
  /// The coherent Vitals synchronization stream.
  final Stream<StateSynchronization<CharacterVitalsState>> _vitalsChanges;

  /// The Character XP synchronization stream.
  final Stream<StateSynchronization<CharacterXpState>> _xpChanges;

  /// The Character Level synchronization stream.
  final Stream<StateSynchronization<CharacterLevelState>> _levelChanges;

  /// Creates the Character view over its existing state trackers.
  /// @param vitalsChanges The Vitals tracker stream.
  /// @param xpChanges The XP tracker stream.
  /// @param levelChanges The Level tracker stream.
  DovahLinkCharacter({
    required Stream<StateSynchronization<CharacterVitalsState>> vitalsChanges,
    required Stream<StateSynchronization<CharacterXpState>> xpChanges,
    required Stream<StateSynchronization<CharacterLevelState>> levelChanges,
  }) : _vitalsChanges = vitalsChanges,
       _xpChanges = xpChanges,
       _levelChanges = levelChanges;

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
}
