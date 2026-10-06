import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.selectors.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkStateStatus;

/// ViewModel representing the real Known Host shown by the Session Shell.
class SessionShellViewModel extends Equatable {
  /// The Host card projection for this route, or `null` if its Known Host was removed.
  final HostCardViewData? host;

  /// The current character name when the SDK provides one.
  final String? characterName;

  /// The truthful level and current XP label when the SDK provides a level.
  final String? characterLevelLabel;

  /// The SDK synchronization status for the character identity.
  final DovahLinkStateStatus identityStatus;

  /// The SDK synchronization status for the character level.
  final DovahLinkStateStatus levelStatus;

  /// The SDK synchronization status for current XP.
  final DovahLinkStateStatus xpStatus;

  /// Called when the user returns to Connections, leaving the session active.
  final void Function() onBack;

  /// Creates a Session Shell ViewModel.
  const SessionShellViewModel({
    required this.host,
    required this.onBack,
    this.characterName,
    this.characterLevelLabel,
    this.identityStatus = DovahLinkStateStatus.notSubscribed,
    this.levelStatus = DovahLinkStateStatus.notSubscribed,
    this.xpStatus = DovahLinkStateStatus.notSubscribed,
  });

  /// The game and available character details shown beside the Host name.
  String get characterSummary =>
      <String>['Skyrim SE', ?characterName, ?characterLevelLabel].join(' · ');

  /// Whether retained character data should use the stale visual treatment.
  bool get isCharacterSummaryStale =>
      [identityStatus, levelStatus, xpStatus].any(
        (DovahLinkStateStatus status) =>
            status == DovahLinkStateStatus.stale ||
            status == DovahLinkStateStatus.failed,
      );

  /// Whether character data is recovering, unless stale/failed status takes priority.
  bool get isCharacterSummaryRecovering =>
      !isCharacterSummaryStale &&
      [
        identityStatus,
        levelStatus,
        xpStatus,
      ].contains(DovahLinkStateStatus.recovering);

  /// Builds the presentation projection for [hostId] from Redux state.
  factory SessionShellViewModel.fromStore(
    Store<AppState> store, {
    required String hostId,
  }) {
    final AppState state = store.state;
    final List<HostCardViewData> cards = ConnectionSelectors.hostCardsSelector(
      state,
    );
    HostCardViewData? host;
    for (final HostCardViewData card in cards) {
      if (card.host.hostId == hostId) {
        host = card;
        break;
      }
    }
    final identity = LiveStateSelectors.characterIdentitySelector(state);
    final levelState = LiveStateSelectors.characterLevelSelector(state);
    final xpState = LiveStateSelectors.characterXpSelector(state);
    final String? characterName = _meaningfulText(identity.value?.name);
    final int? level = levelState.value?.value;
    final double? experience = xpState.value?.value;
    final String? characterLevelLabel = level == null
        ? null
        : 'Level $level${experience == null ? '' : ' (${_formatExperience(experience)} XP)'}';
    return SessionShellViewModel(
      host: host,
      onBack: () => store.dispatch(const SessionShellBackRequestedAction()),
      characterName: characterName,
      characterLevelLabel: characterLevelLabel,
      identityStatus: identity.status,
      levelStatus: levelState.status,
      xpStatus: xpState.status,
    );
  }

  static String _formatExperience(double value) =>
      value == value.truncateToDouble()
      ? value.toInt().toString()
      : value.toString();

  static String? _meaningfulText(String? value) {
    final String? trimmed = value?.trim();
    return trimmed == null || trimmed.isEmpty ? null : trimmed;
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    host,
    characterName,
    characterLevelLabel,
    identityStatus,
    levelStatus,
    xpStatus,
  ];
}
