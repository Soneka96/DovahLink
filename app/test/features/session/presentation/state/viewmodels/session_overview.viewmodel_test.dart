import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_overview.viewmodel.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../../fixtures/fixtures.dart';

/// Exercises the future Session Overview's typed Redux projection.
void main() {
  group('Method fromStore behaves correctly', () {
    test(
      'fromStore exposes all eight values with synchronization metadata',
      () {
        final SessionOverviewViewModel viewModel =
            SessionOverviewViewModel.fromStore(
              const CreateStore()(initialState: _buildOverviewAppState()),
            );

        expect(viewModel.characterVitals.status, LiveStateStatus.stale);
        expect(viewModel.characterVitals.value?.health.current, 105);
        expect(viewModel.characterVitals.value?.health.max, 100);
        expect(viewModel.characterVitals.value?.magicka.current, 45);
        expect(viewModel.characterVitals.value?.magicka.max, 80);
        expect(viewModel.characterVitals.value?.stamina.current, 67);
        expect(viewModel.characterVitals.value?.stamina.max, 90);
        expect(viewModel.characterVitals.stateAuthorityId, 'authority-a');
        expect(viewModel.characterVitals.playContextId, 'context-a');
        expect(viewModel.characterVitals.revision, 2);

        expect(viewModel.characterXp.status, LiveStateStatus.synchronized);
        expect(viewModel.characterXp.value, 62.5);
        expect(viewModel.characterXp.stateAuthorityId, 'authority-a');
        expect(viewModel.characterXp.playContextId, 'context-a');
        expect(viewModel.characterXp.revision, 3);
        expect(viewModel.characterLevel.status, LiveStateStatus.unavailable);
        expect(viewModel.characterLevel.value, isNull);
        expect(viewModel.characterLevel.stateAuthorityId, 'authority-a');
        expect(viewModel.characterLevel.playContextId, 'context-a');
        expect(viewModel.characterLevel.revision, 4);

        expect(
          viewModel.characterIdentity.status,
          LiveStateStatus.synchronized,
        );
        expect(viewModel.characterIdentity.value?.name, 'Player');
        expect(viewModel.characterIdentity.value?.race, 'Nord');
        expect(viewModel.characterIdentity.stateAuthorityId, 'authority-a');
        expect(viewModel.characterIdentity.playContextId, 'context-a');
        expect(viewModel.characterIdentity.revision, 5);
        expect(
          viewModel.supernaturalTraits.status,
          LiveStateStatus.synchronized,
        );
        expect(viewModel.supernaturalTraits.value?.isVampire, isFalse);
        expect(viewModel.supernaturalTraits.value?.hasVampireLordForm, isTrue);
        expect(viewModel.supernaturalTraits.value?.hasWerewolfForm, isTrue);
        expect(viewModel.supernaturalTraits.stateAuthorityId, 'authority-a');
        expect(viewModel.supernaturalTraits.playContextId, 'context-a');
        expect(viewModel.supernaturalTraits.revision, 6);

        expect(viewModel.playerLocation.status, LiveStateStatus.synchronized);
        expect(viewModel.playerLocation.value?.cellId, 17);
        expect(viewModel.playerLocation.value?.cellKind, LiveCellKind.interior);
        expect(viewModel.playerLocation.value?.cellName, 'Cell');
        expect(viewModel.playerLocation.value?.locationId, isNull);
        expect(viewModel.playerLocation.value?.locationName, isNull);
        expect(viewModel.playerLocation.value?.worldspaceId, 18);
        expect(viewModel.playerLocation.value?.worldspaceName, 'Worldspace');
        expect(viewModel.playerLocation.stateAuthorityId, 'authority-a');
        expect(viewModel.playerLocation.playContextId, 'context-a');
        expect(viewModel.playerLocation.revision, 7);

        expect(viewModel.gameTime.status, LiveStateStatus.synchronized);
        expect(viewModel.gameTime.value?.year, 4);
        expect(viewModel.gameTime.value?.month, 8);
        expect(viewModel.gameTime.value?.monthName, 'Last Seed');
        expect(viewModel.gameTime.value?.day, 12);
        expect(viewModel.gameTime.value?.hour, 14);
        expect(viewModel.gameTime.value?.minute, 30);
        expect(viewModel.gameTime.stateAuthorityId, 'authority-a');
        expect(viewModel.gameTime.playContextId, 'context-a');
        expect(viewModel.gameTime.revision, 8);

        expect(viewModel.trackedQuests.status, LiveStateStatus.synchronized);
        expect(viewModel.trackedQuests.value, hasLength(2));
        expect(viewModel.trackedQuests.value?[0].questId, 1);
        expect(viewModel.trackedQuests.value?[0].title, 'Test Quest');
        expect(viewModel.trackedQuests.value?[0].type, 7);
        expect(viewModel.trackedQuests.value?[0].objectives, hasLength(2));
        expect(viewModel.trackedQuests.value?[0].objectives[0].index, 1);
        expect(viewModel.trackedQuests.value?[0].objectives[0].instanceId, 21);
        expect(
          viewModel.trackedQuests.value?[0].objectives[0].text,
          'First objective',
        );
        expect(
          viewModel.trackedQuests.value?[0].objectives[0].status,
          LiveQuestObjectiveStatus.displayed,
        );
        expect(viewModel.trackedQuests.value?[0].objectives[1].text, isNull);
        expect(
          viewModel.trackedQuests.value?[0].objectives[1].status,
          LiveQuestObjectiveStatus.dormant,
        );
        expect(viewModel.trackedQuests.value?[1].questId, 2);
        expect(viewModel.trackedQuests.value?[1].title, 'Second quest');
        expect(viewModel.trackedQuests.stateAuthorityId, 'authority-a');
        expect(viewModel.trackedQuests.playContextId, 'context-a');
        expect(viewModel.trackedQuests.revision, 9);
      },
    );

    test(
      'fromStore retains unavailable and synchronized-empty distinctions',
      () {
        final SessionLiveState base = _buildOverviewAppState().liveState;
        final SessionLiveState withEmptyQuests = base.copyWith(
          trackedQuests: const LiveDomainState<List<LiveTrackedQuest>>(
            status: LiveStateStatus.synchronized,
            value: <LiveTrackedQuest>[],
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 9,
          ),
        );
        final Store<AppState> store = const CreateStore()(
          initialState: AppState.initial(liveState: withEmptyQuests),
        );

        final SessionOverviewViewModel viewModel =
            SessionOverviewViewModel.fromStore(store);

        expect(viewModel.characterLevel.status, LiveStateStatus.unavailable);
        expect(viewModel.characterLevel.value, isNull);
        expect(viewModel.trackedQuests.status, LiveStateStatus.synchronized);
        expect(viewModel.trackedQuests.value, isEmpty);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('equality includes every selected synchronization projection', () {
      final SessionLiveState initial = _buildOverviewAppState().liveState;
      const LiveDomainState<double?> updatedXp = LiveDomainState<double?>(
        status: LiveStateStatus.stale,
        value: 62.5,
        stateAuthorityId: 'authority-a',
        playContextId: 'context-a',
        revision: 3,
      );
      final SessionOverviewViewModel current =
          SessionOverviewViewModel.fromStore(
            const CreateStore()(
              initialState: AppState.initial(liveState: initial),
            ),
          );
      final SessionOverviewViewModel stale = SessionOverviewViewModel.fromStore(
        const CreateStore()(
          initialState: AppState.initial(
            liveState: initial.copyWith(characterXp: updatedXp),
          ),
        ),
      );

      expect(current == stale, isFalse);
    });

    test('props include all eight selected synchronization projections', () {
      final SessionOverviewViewModel viewModel =
          SessionOverviewViewModel.fromStore(
            const CreateStore()(initialState: _buildOverviewAppState()),
          );

      expect(viewModel.props, [
        viewModel.characterVitals,
        viewModel.characterXp,
        viewModel.characterLevel,
        viewModel.characterIdentity,
        viewModel.supernaturalTraits,
        viewModel.playerLocation,
        viewModel.gameTime,
        viewModel.trackedQuests,
      ]);
    });
  });
}

/// Builds a representative app-owned state for Overview projection tests.
/// @return A live-state slice with all eight domains populated or unavailable.
AppState _buildOverviewAppState() => AppState.initial(
  liveState: SessionLiveState(
    characterVitals:
        const LiveDomainState<
          ({
            ({double current, double max}) health,
            ({double current, double max}) magicka,
            ({double current, double max}) stamina,
          })
        >(
          status: LiveStateStatus.stale,
          value: (
            health: (current: 105, max: 100),
            magicka: (current: 45, max: 80),
            stamina: (current: 67, max: 90),
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 2,
        ),
    characterXp: const LiveDomainState<double?>(
      status: LiveStateStatus.synchronized,
      value: 62.5,
      stateAuthorityId: 'authority-a',
      playContextId: 'context-a',
      revision: 3,
    ),
    characterLevel: const LiveDomainState<int?>(
      status: LiveStateStatus.unavailable,
      value: null,
      stateAuthorityId: 'authority-a',
      playContextId: 'context-a',
      revision: 4,
    ),
    characterIdentity: const LiveDomainState<({String name, String race})?>(
      status: LiveStateStatus.synchronized,
      value: (name: 'Player', race: 'Nord'),
      stateAuthorityId: 'authority-a',
      playContextId: 'context-a',
      revision: 5,
    ),
    supernaturalTraits:
        const LiveDomainState<
          ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
        >(
          status: LiveStateStatus.synchronized,
          value: (
            isVampire: false,
            hasVampireLordForm: true,
            hasWerewolfForm: true,
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 6,
        ),
    playerLocation:
        const LiveDomainState<
          ({
            int cellId,
            LiveCellKind cellKind,
            String? cellName,
            int? locationId,
            String? locationName,
            int? worldspaceId,
            String? worldspaceName,
          })?
        >(
          status: LiveStateStatus.synchronized,
          value: (
            cellId: 17,
            cellKind: LiveCellKind.interior,
            cellName: 'Cell',
            locationId: null,
            locationName: null,
            worldspaceId: 18,
            worldspaceName: 'Worldspace',
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 7,
        ),
    gameTime:
        const LiveDomainState<
          ({
            int year,
            int month,
            String monthName,
            int day,
            int hour,
            int minute,
          })?
        >(
          status: LiveStateStatus.synchronized,
          value: (
            year: 4,
            month: 8,
            monthName: 'Last Seed',
            day: 12,
            hour: 14,
            minute: 30,
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 8,
        ),
    trackedQuests: LiveDomainState<List<LiveTrackedQuest>>(
      status: LiveStateStatus.synchronized,
      value: [
        Fixtures.buildLiveTrackedQuest(
          questId: 1,
          type: 7,
          objectives: [
            (
              index: 1,
              instanceId: 21,
              text: 'First objective',
              status: LiveQuestObjectiveStatus.displayed,
            ),
            (
              index: 2,
              instanceId: 22,
              text: null,
              status: LiveQuestObjectiveStatus.dormant,
            ),
          ],
        ),
        Fixtures.buildLiveTrackedQuest(questId: 2, title: 'Second quest'),
      ],
      stateAuthorityId: 'authority-a',
      playContextId: 'context-a',
      revision: 9,
    ),
  ),
);
