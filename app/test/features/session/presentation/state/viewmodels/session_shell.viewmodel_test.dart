import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_shell.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterLevelState,
        CharacterXpState,
        DovahLinkStateStatus;

/// Exercises Session Shell ViewModel projection and callbacks.
void main() {
  group('Method fromStore behaves correctly', () {
    test('fromStore projects the requested Known Host', () {
      final host = Fixtures.buildHost(hostId: 'selected-host');
      final store = const CreateStore()();
      store.dispatch(
        ConnectionKnownHostsChangedAction([
          Fixtures.buildKnownHost(
            host: host,
            availability: HostAvailability.online,
            sessionState: KnownHostSessionState.connected,
          ),
        ]),
      );

      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        store,
        hostId: 'selected-host',
      );

      expect(viewModel.host?.host.hostId, 'selected-host');
      expect(viewModel.host?.state, DovahConnectionCardState.connected);
    });

    test('fromStore leaves an absent Known Host unavailable', () {
      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        const CreateStore()(),
        hostId: 'missing-host',
      );

      expect(viewModel.host, isNull);
    });

    test('fromStore projects the truthful character header details', () {
      final SessionLiveState liveState = const SessionLiveState.initial()
          .copyWith(
            characterIdentity:
                Fixtures.buildStateSynchronization<CharacterIdentityState?>(
                  value: Fixtures.buildCharacterIdentity(name: ' Gonçalo '),
                ),
            characterLevel:
                Fixtures.buildStateSynchronization<CharacterLevelState>(
                  value: Fixtures.buildCharacterLevel(value: 43),
                ),
            characterXp: Fixtures.buildStateSynchronization<CharacterXpState>(
              value: Fixtures.buildCharacterXp(value: 320),
            ),
          );
      final Store<AppState> store = Store<AppState>(
        (AppState state, Object? action) => state,
        initialState: AppState.initial(liveState: liveState),
      );

      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        store,
        hostId: 'selected-host',
      );

      expect(viewModel.characterName, 'Gonçalo');
      expect(viewModel.characterLevelLabel, 'Level 43 (320 XP)');
      expect(
        viewModel.characterSummary,
        'Skyrim SE · Gonçalo · Level 43 (320 XP)',
      );
    });

    test('fromStore omits level and XP when level is unavailable', () {
      final SessionLiveState liveState = const SessionLiveState.initial()
          .copyWith(
            characterIdentity:
                Fixtures.buildStateSynchronization<CharacterIdentityState?>(
                  value: Fixtures.buildCharacterIdentity(name: 'Aela'),
                ),
            characterXp: Fixtures.buildStateSynchronization<CharacterXpState>(
              value: Fixtures.buildCharacterXp(value: 320),
            ),
          );
      final Store<AppState> store = Store<AppState>(
        (AppState state, Object? action) => state,
        initialState: AppState.initial(liveState: liveState),
      );

      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        store,
        hostId: 'selected-host',
      );

      expect(viewModel.characterLevelLabel, isNull);
      expect(viewModel.characterSummary, 'Skyrim SE · Aela');
    });

    test('fromStore keeps level when the XP value is unavailable', () {
      final SessionLiveState liveState = const SessionLiveState.initial()
          .copyWith(
            characterLevel:
                Fixtures.buildStateSynchronization<CharacterLevelState>(
                  value: Fixtures.buildCharacterLevel(value: 43),
                ),
            characterXp: Fixtures.buildStateSynchronization<CharacterXpState>(
              value: Fixtures.buildCharacterXp(value: null),
            ),
          );
      final Store<AppState> store = Store<AppState>(
        (AppState state, Object? action) => state,
        initialState: AppState.initial(liveState: liveState),
      );

      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        store,
        hostId: 'selected-host',
      );

      expect(viewModel.characterLevelLabel, 'Level 43');
      expect(viewModel.characterSummary, 'Skyrim SE · Level 43');
    });

    test(
      'fromStore preserves synchronization status precedence for the header',
      () {
        final SessionLiveState liveState = const SessionLiveState.initial()
            .copyWith(
              characterIdentity:
                  Fixtures.buildStateSynchronization<CharacterIdentityState?>(
                    value: Fixtures.buildCharacterIdentity(name: 'Aela'),
                    status: DovahLinkStateStatus.failed,
                  ),
              characterLevel:
                  Fixtures.buildStateSynchronization<CharacterLevelState>(
                    value: Fixtures.buildCharacterLevel(value: 43),
                    status: DovahLinkStateStatus.recovering,
                  ),
            );
        final Store<AppState> store = Store<AppState>(
          (AppState state, Object? action) => state,
          initialState: AppState.initial(liveState: liveState),
        );

        final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
          store,
          hostId: 'selected-host',
        );

        expect(viewModel.isCharacterSummaryStale, isTrue);
        expect(viewModel.isCharacterSummaryRecovering, isFalse);
      },
    );
  });

  group('Callback onBack behaves correctly', () {
    test('onBack dispatches the Session Shell navigation request', () {
      final List<Object?> actions = [];
      final Store<AppState> recordingStore = Store<AppState>((
        AppState state,
        Object? action,
      ) {
        actions.add(action);
        return state;
      }, initialState: AppState.initial());
      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        recordingStore,
        hostId: 'missing-host',
      );

      viewModel.onBack();

      expect(actions, [isA<SessionShellBackRequestedAction>()]);
    });
  });
}
