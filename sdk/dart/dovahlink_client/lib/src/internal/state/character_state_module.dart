import 'package:dovahlink_client_sdk/src/dovahlink_character.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/character_identity_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_supernatural_traits_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_vitals_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_xp_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Owns Character domain trackers and their typed protocol registrations.
abstract interface class ICharacterStateModule {
  /// The public Character view over the module's tracker streams.
  IDovahLinkCharacter get character;

  /// The registrations passed to the shared state-message handler.
  List<IStateDomainDefinition<Object?>> get domains;
}

/// Composes the independently synchronized Character state domains over the shared generic state machinery.
class CharacterStateModule implements ICharacterStateModule {
  /// Tracks the coherent Vitals Snapshot state.
  final IStateRevisionTracker<CharacterVitalsState> _vitalsTracker;

  /// Tracks the independently sampled XP Snapshot state.
  final IStateRevisionTracker<CharacterXpState> _xpTracker;

  /// Tracks the Level baseline and Event state.
  final IStateRevisionTracker<CharacterLevelState> _levelTracker;

  /// Tracks the complete Identity Snapshot or explicit unavailability.
  final IStateRevisionTracker<CharacterIdentityState?> _identityTracker;

  /// Tracks the complete supernatural-traits Snapshot or explicit unavailability.
  final IStateRevisionTracker<CharacterSupernaturalTraitsState?>
  _supernaturalTraitsTracker;

  /// The public Character projection over the module's tracker streams.
  late final IDovahLinkCharacter _character;

  /// The domain registrations in dispatch order.
  late final List<IStateDomainDefinition<Object?>> _domains;

  /// Creates the replayable trackers, typed decoders, and state-area definitions.
  CharacterStateModule()
    : _vitalsTracker = StateRevisionTracker<CharacterVitalsState>(
        state: CurrentValueStream<StateSynchronization<CharacterVitalsState>>(
          const StateSynchronization<CharacterVitalsState>.notSubscribed(),
        ),
      ),
      _xpTracker = StateRevisionTracker<CharacterXpState>(
        state: CurrentValueStream<StateSynchronization<CharacterXpState>>(
          const StateSynchronization<CharacterXpState>.notSubscribed(),
        ),
      ),
      _levelTracker = StateRevisionTracker<CharacterLevelState>(
        state: CurrentValueStream<StateSynchronization<CharacterLevelState>>(
          const StateSynchronization<CharacterLevelState>.notSubscribed(),
        ),
      ),
      _identityTracker = StateRevisionTracker<CharacterIdentityState?>(
        state:
            CurrentValueStream<StateSynchronization<CharacterIdentityState?>>(
              const StateSynchronization<
                CharacterIdentityState?
              >.notSubscribed(),
            ),
      ),
      _supernaturalTraitsTracker =
          StateRevisionTracker<CharacterSupernaturalTraitsState?>(
            state:
                CurrentValueStream<
                  StateSynchronization<CharacterSupernaturalTraitsState?>
                >(
                  const StateSynchronization<
                    CharacterSupernaturalTraitsState?
                  >.notSubscribed(),
                ),
          ) {
    final StateDomainDefinition<CharacterVitalsState> vitalsDomain =
        StateDomainDefinition<CharacterVitalsState>(
          stateArea: DovahLinkStateArea.characterVitals.protocolValue,
          decode: CharacterVitalsState.fromJson,
          tracker: _vitalsTracker,
          isUnavailable: (CharacterVitalsState state) => state.isUnavailable,
        );
    final StateDomainDefinition<CharacterXpState> xpDomain =
        StateDomainDefinition<CharacterXpState>(
          stateArea: DovahLinkStateArea.characterXp.protocolValue,
          decode: CharacterXpState.fromJson,
          tracker: _xpTracker,
          isUnavailable: (CharacterXpState state) => state.value == null,
        );
    final StateDomainDefinition<CharacterIdentityState?> identityDomain =
        StateDomainDefinition<CharacterIdentityState?>(
          stateArea: DovahLinkStateArea.characterIdentity.protocolValue,
          decode: decodeCharacterIdentityState,
          tracker: _identityTracker,
          isUnavailable: (CharacterIdentityState? state) => state == null,
        );
    final StateDomainDefinition<CharacterSupernaturalTraitsState?>
    supernaturalTraitsDomain =
        StateDomainDefinition<CharacterSupernaturalTraitsState?>(
          stateArea:
              DovahLinkStateArea.characterSupernaturalTraits.protocolValue,
          decode: decodeCharacterSupernaturalTraitsState,
          tracker: _supernaturalTraitsTracker,
          isUnavailable: (CharacterSupernaturalTraitsState? state) =>
              state == null,
        );
    final StateDomainDefinition<CharacterLevelState> levelDomain =
        StateDomainDefinition<CharacterLevelState>(
          stateArea: DovahLinkStateArea.characterLevel.protocolValue,
          decode: CharacterLevelState.fromJson,
          tracker: _levelTracker,
          isUnavailable: (CharacterLevelState state) => state.value == null,
          supportsEvents: true,
        );
    _domains = List<IStateDomainDefinition<Object?>>.unmodifiable(
      <IStateDomainDefinition<Object?>>[
        vitalsDomain,
        xpDomain,
        identityDomain,
        supernaturalTraitsDomain,
        levelDomain,
      ],
    );
    _character = DovahLinkCharacter(
      vitalsChanges: _vitalsTracker.changes,
      xpChanges: _xpTracker.changes,
      levelChanges: _levelTracker.changes,
      identityChanges: _identityTracker.changes,
      supernaturalTraitsChanges: _supernaturalTraitsTracker.changes,
    );
  }

  /// Implements [ICharacterStateModule.character].
  @override
  IDovahLinkCharacter get character => _character;

  /// Implements [ICharacterStateModule.domains].
  @override
  List<IStateDomainDefinition<Object?>> get domains => _domains;
}
