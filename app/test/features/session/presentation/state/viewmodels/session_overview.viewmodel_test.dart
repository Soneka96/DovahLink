import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_overview.viewmodel.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

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
  StateSynchronization<TrackedQuestsState?>? trackedQuests,
}) => SessionLiveState(
  characterVitals:
      const StateSynchronization<CharacterVitalsState>.notSubscribed(),
  characterXp: const StateSynchronization<CharacterXpState>.notSubscribed(),
  characterLevel:
      const StateSynchronization<CharacterLevelState>.notSubscribed(),
  characterIdentity:
      const StateSynchronization<CharacterIdentityState?>.notSubscribed(),
  supernaturalTraits:
      const StateSynchronization<
        CharacterSupernaturalTraitsState?
      >.notSubscribed(),
  playerLocation:
      const StateSynchronization<PlayerLocationState?>.notSubscribed(),
  gameTime: const StateSynchronization<GameTimeState?>.notSubscribed(),
  trackedQuests:
      trackedQuests ??
      const StateSynchronization<TrackedQuestsState?>.notSubscribed(),
);
