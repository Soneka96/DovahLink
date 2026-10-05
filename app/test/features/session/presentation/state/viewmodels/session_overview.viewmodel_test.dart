import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_overview.viewmodel.dart';
import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_quest.viewdata.dart';
import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_vitals.viewdata.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterLevelState,
        CharacterSupernaturalTraitsState,
        CharacterVitalsState,
        CharacterXpState,
        DovahLinkStateStatus,
        GameTimeState,
        PlayerLocationState,
        StateSynchronization,
        TrackedQuest,
        TrackedQuestsState;

/// Exercises that the Overview ViewModel exposes the stored SDK values directly.
void main() {
  group('Method fromStore behaves correctly', () {
    test(
      'fromStore carries all eight SDK synchronization values unchanged',
      () {
        final SessionLiveState liveState = _buildLiveState();
        final Store<AppState> store = Store<AppState>(
          (AppState state, Object? action) => state,
          initialState: AppState.initial(liveState: liveState),
        );

        final SessionOverviewViewModel viewModel =
            SessionOverviewViewModel.fromStore(store);

        expect(viewModel.characterVitals, same(liveState.characterVitals));
        expect(viewModel.characterXp, same(liveState.characterXp));
        expect(viewModel.characterLevel, same(liveState.characterLevel));
        expect(viewModel.characterIdentity, same(liveState.characterIdentity));
        expect(
          viewModel.supernaturalTraits,
          same(liveState.supernaturalTraits),
        );
        expect(viewModel.playerLocation, same(liveState.playerLocation));
        expect(viewModel.gameTime, same(liveState.gameTime));
        expect(viewModel.trackedQuests, same(liveState.trackedQuests));
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('props include all eight SDK synchronization values', () {
      final SessionLiveState liveState = _buildLiveState();
      final SessionOverviewViewModel viewModel = _buildViewModel(liveState);

      expect(viewModel.props, [
        liveState.characterVitals,
        liveState.characterXp,
        liveState.characterLevel,
        liveState.characterIdentity,
        liveState.supernaturalTraits,
        liveState.playerLocation,
        liveState.gameTime,
        liveState.trackedQuests,
      ]);
    });

    test('equality includes each SDK synchronization value', () {
      final SessionLiveState first = _buildLiveState();
      final SessionLiveState second = _buildLiveState(
        trackedQuests: const StateSynchronization<TrackedQuestsState?>(
          status: DovahLinkStateStatus.failed,
          value: null,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 8,
        ),
      );
      final SessionOverviewViewModel firstViewModel = _buildViewModel(first);
      final SessionOverviewViewModel secondViewModel = _buildViewModel(second);

      expect(firstViewModel == secondViewModel, isFalse);
    });
  });

  group('Property characterName behaves correctly', () {
    test('Property characterName returns the trimmed SDK name', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterIdentity: _synchronized<CharacterIdentityState?>(
            Fixtures.buildCharacterIdentity(name: '  Aela  '),
          ),
        ),
      );

      expect(viewModel.characterName, 'Aela');
    });

    test('Property characterName omits a missing or blank SDK name', () {
      final SessionOverviewViewModel missing = _buildViewModel(
        _buildLiveState(),
      );
      final SessionOverviewViewModel blank = _buildViewModel(
        _buildLiveState(
          characterIdentity: _synchronized<CharacterIdentityState?>(
            Fixtures.buildCharacterIdentity(name: '  '),
          ),
        ),
      );

      expect(missing.characterName, isNull);
      expect(blank.characterName, isNull);
    });
  });

  group('Property characterLevelLabel behaves correctly', () {
    test('Property characterLevelLabel combines available level and XP', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterLevel: _synchronized<CharacterLevelState>(
            Fixtures.buildCharacterLevel(value: 43),
          ),
          characterXp: _synchronized<CharacterXpState>(
            Fixtures.buildCharacterXp(value: 320),
          ),
        ),
      );

      expect(viewModel.characterLevelLabel, 'Level 43 (320 XP)');
    });

    test('Property characterLevelLabel preserves fractional XP', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterLevel: _synchronized<CharacterLevelState>(
            Fixtures.buildCharacterLevel(value: 43),
          ),
          characterXp: _synchronized<CharacterXpState>(
            Fixtures.buildCharacterXp(value: 320.5),
          ),
        ),
      );

      expect(viewModel.characterLevelLabel, 'Level 43 (320.5 XP)');
    });

    test('Property characterLevelLabel omits XP when XP is unavailable', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterLevel: _synchronized<CharacterLevelState>(
            Fixtures.buildCharacterLevel(value: 43),
          ),
          characterXp: _synchronized<CharacterXpState>(
            Fixtures.buildCharacterXp(value: null),
          ),
        ),
      );

      expect(viewModel.characterLevelLabel, 'Level 43');
    });

    test('Property characterLevelLabel omits XP without a level', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterXp: _synchronized<CharacterXpState>(
            Fixtures.buildCharacterXp(value: 320),
          ),
        ),
      );

      expect(viewModel.characterLevelLabel, isNull);
    });
  });

  group('Property locationName behaves correctly', () {
    test('Property locationName prefers the selected location name', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(),
          ),
        ),
      );

      expect(viewModel.locationName, 'The Bannered Mare');
    });

    test('Property locationName falls back to the current cell name', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(locationId: null, locationName: null),
          ),
        ),
      );

      expect(viewModel.locationName, 'Whiterun');
    });

    test('Property locationName skips a blank preferred name', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(locationName: '  '),
          ),
        ),
      );

      expect(viewModel.locationName, 'Whiterun');
    });

    test('Property locationName falls back to the worldspace name', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(
              locationId: null,
              locationName: null,
              cellName: null,
              worldspaceId: 24,
              worldspaceName: 'Skyrim',
            ),
          ),
        ),
      );

      expect(viewModel.locationName, 'Skyrim');
    });

    test('Property locationName omits absent or blank place names', () {
      final SessionOverviewViewModel missing = _buildViewModel(
        _buildLiveState(
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(
              locationId: null,
              locationName: null,
              cellName: null,
            ),
          ),
        ),
      );
      final SessionOverviewViewModel blank = _buildViewModel(
        _buildLiveState(
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(
              locationName: '  ',
              cellName: '\t',
              worldspaceName: '',
              worldspaceId: 24,
            ),
          ),
        ),
      );

      expect(missing.locationName, isNull);
      expect(blank.locationName, isNull);
    });
  });

  group('Property gameTimeLabel behaves correctly', () {
    test('Property gameTimeLabel formats the Skyrim year and evening time', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          gameTime: _synchronized<GameTimeState?>(
            Fixtures.buildGameTime(year: 201, hour: 18, minute: 42),
          ),
        ),
      );

      expect(viewModel.gameTimeLabel, '4E 201, 6:42 PM');
    });

    test('Property gameTimeLabel handles midnight and noon', () {
      final SessionOverviewViewModel midnight = _buildViewModel(
        _buildLiveState(
          gameTime: _synchronized<GameTimeState?>(
            Fixtures.buildGameTime(year: 201, hour: 0, minute: 5),
          ),
        ),
      );
      final SessionOverviewViewModel noon = _buildViewModel(
        _buildLiveState(
          gameTime: _synchronized<GameTimeState?>(
            Fixtures.buildGameTime(year: 201, hour: 12, minute: 0),
          ),
        ),
      );

      expect(midnight.gameTimeLabel, '4E 201, 12:05 AM');
      expect(noon.gameTimeLabel, '4E 201, 12:00 PM');
    });

    test('Property gameTimeLabel omits an unavailable calendar value', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(),
      );

      expect(viewModel.gameTimeLabel, isNull);
    });
  });

  group('Property contextLine behaves correctly', () {
    test('Property contextLine joins current character, place, and time', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterIdentity: _synchronized<CharacterIdentityState?>(
            Fixtures.buildCharacterIdentity(name: 'Gonçalo'),
          ),
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(),
          ),
          gameTime: _synchronized<GameTimeState?>(
            Fixtures.buildGameTime(year: 201, hour: 18, minute: 42),
          ),
        ),
      );

      expect(
        viewModel.contextLine,
        'Gonçalo · The Bannered Mare · 4E 201, 6:42 PM',
      );
    });

    test('Property contextLine omits unavailable segments cleanly', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterIdentity: _synchronized<CharacterIdentityState?>(
            Fixtures.buildCharacterIdentity(name: 'Gonçalo'),
          ),
          gameTime: _synchronized<GameTimeState?>(
            Fixtures.buildGameTime(year: 201, hour: 18, minute: 42),
          ),
        ),
      );

      expect(viewModel.contextLine, 'Gonçalo · 4E 201, 6:42 PM');
    });

    test('Property contextLine omits the character when it is unavailable', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(),
          ),
          gameTime: _synchronized<GameTimeState?>(
            Fixtures.buildGameTime(year: 201, hour: 18, minute: 42),
          ),
        ),
      );

      expect(viewModel.contextLine, 'The Bannered Mare · 4E 201, 6:42 PM');
    });

    test('Property contextLine omits the time when it is unavailable', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterIdentity: _synchronized<CharacterIdentityState?>(
            Fixtures.buildCharacterIdentity(name: 'Gonçalo'),
          ),
          playerLocation: _synchronized<PlayerLocationState?>(
            Fixtures.buildPlayerLocation(),
          ),
        ),
      );

      expect(viewModel.contextLine, 'Gonçalo · The Bannered Mare');
    });

    test('Property contextLine is absent when no segments are usable', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(),
      );

      expect(viewModel.contextLine, isNull);
    });
  });

  group('Property supernaturalLabel behaves correctly', () {
    for (final (
          String description,
          bool isVampire,
          bool hasVampireLordForm,
          bool hasWerewolfForm,
          String? expected,
        )
        in <(String, bool, bool, bool, String?)>[
          ('none', false, false, false, null),
          ('vampire', true, false, false, 'Vampire'),
          ('vampire lord', true, true, false, 'Vampire Lord'),
          ('werewolf', false, false, true, 'Werewolf'),
          ('vampire and werewolf', true, false, true, 'Vampire · Werewolf'),
          (
            'vampire lord and werewolf',
            true,
            true,
            true,
            'Vampire Lord · Werewolf',
          ),
          (
            'vampire lord and werewolf without vampire status',
            false,
            true,
            true,
            'Vampire Lord · Werewolf',
          ),
          (
            'vampire lord without vampire status',
            false,
            true,
            false,
            'Vampire Lord',
          ),
        ]) {
      test('Property supernaturalLabel presents $description', () {
        final SessionOverviewViewModel viewModel = _buildViewModel(
          _buildLiveState(
            supernaturalTraits:
                Fixtures.buildStateSynchronization<
                  CharacterSupernaturalTraitsState?
                >(
                  value: Fixtures.buildSupernaturalTraits(
                    isVampire: isVampire,
                    hasVampireLordForm: hasVampireLordForm,
                    hasWerewolfForm: hasWerewolfForm,
                  ),
                ),
          ),
        );

        expect(viewModel.supernaturalLabel, expected);
      });
    }

    test('Property supernaturalLabel omits unavailable traits', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(),
      );

      expect(viewModel.supernaturalLabel, isNull);
    });
  });

  group('Property vitalsViewData behaves correctly', () {
    test('Property vitalsViewData projects the SDK Vitals value', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          characterVitals: _synchronized<CharacterVitalsState>(
            Fixtures.buildCharacterVitals(),
          ),
        ),
      );

      final SessionOverviewVitalsViewData viewData = viewModel.vitalsViewData;
      expect(viewData.status, DovahLinkStateStatus.synchronized);
      expect(viewData.healthRatio, closeTo(0.8, 0.0001));
      expect(viewData.magickaRatio, closeTo(0.5, 0.0001));
      expect(viewData.staminaRatio, closeTo(50 / 90, 0.0001));
    });
  });

  group('Property questsViewData behaves correctly', () {
    test('Property questsViewData projects the truthful empty summary', () {
      final SessionOverviewViewModel viewModel = _buildViewModel(
        _buildLiveState(
          trackedQuests: _synchronized<TrackedQuestsState?>(
            Fixtures.buildTrackedQuests(quests: <TrackedQuest>[]),
          ),
        ),
      );

      final SessionOverviewQuestViewData viewData = viewModel.questsViewData;
      expect(viewData.title, 'NO QUEST TRACKED');
      expect(viewData.detail, 'No path is marked.');
      expect(viewData.status, DovahLinkStateStatus.synchronized);
    });
  });
}

/// Creates an Overview ViewModel from a test live-state slice.
SessionOverviewViewModel _buildViewModel(SessionLiveState state) =>
    SessionOverviewViewModel(
      characterVitals: state.characterVitals,
      characterXp: state.characterXp,
      characterLevel: state.characterLevel,
      characterIdentity: state.characterIdentity,
      supernaturalTraits: state.supernaturalTraits,
      playerLocation: state.playerLocation,
      gameTime: state.gameTime,
      trackedQuests: state.trackedQuests,
    );

/// Creates one state with concrete SDK synchronization values for every domain.
SessionLiveState _buildLiveState({
  StateSynchronization<CharacterVitalsState>? characterVitals,
  StateSynchronization<TrackedQuestsState?>? trackedQuests,
  StateSynchronization<CharacterIdentityState?>? characterIdentity,
  StateSynchronization<CharacterLevelState>? characterLevel,
  StateSynchronization<CharacterXpState>? characterXp,
  StateSynchronization<PlayerLocationState?>? playerLocation,
  StateSynchronization<GameTimeState?>? gameTime,
  StateSynchronization<CharacterSupernaturalTraitsState?>? supernaturalTraits,
}) => SessionLiveState(
  characterVitals:
      characterVitals ??
      const StateSynchronization<CharacterVitalsState>.notSubscribed(),
  characterXp:
      characterXp ??
      const StateSynchronization<CharacterXpState>.notSubscribed(),
  characterLevel:
      characterLevel ??
      const StateSynchronization<CharacterLevelState>.notSubscribed(),
  characterIdentity:
      characterIdentity ??
      const StateSynchronization<CharacterIdentityState?>.notSubscribed(),
  supernaturalTraits:
      supernaturalTraits ??
      const StateSynchronization<
        CharacterSupernaturalTraitsState?
      >.notSubscribed(),
  playerLocation:
      playerLocation ??
      const StateSynchronization<PlayerLocationState?>.notSubscribed(),
  gameTime:
      gameTime ?? const StateSynchronization<GameTimeState?>.notSubscribed(),
  trackedQuests:
      trackedQuests ??
      const StateSynchronization<TrackedQuestsState?>.notSubscribed(),
);

/// Creates a synchronization projection around one test value.
StateSynchronization<T> _synchronized<T>(
  T? value, {
  DovahLinkStateStatus status = DovahLinkStateStatus.synchronized,
}) => Fixtures.buildStateSynchronization<T>(value: value, status: status);
