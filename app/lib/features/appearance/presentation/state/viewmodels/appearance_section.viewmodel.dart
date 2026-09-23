import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by the appearance picker section.
class AppearanceSectionViewModel extends Equatable {
  /// Creates an appearance section ViewModel.
  const AppearanceSectionViewModel({
    required this.activePreset,
    required this.onSelectPreset,
  });

  /// The currently active theme preset.
  final DovahThemePreset activePreset;

  /// Dispatches [ThemePresetSelectedAction] for the given preset.
  final void Function(DovahThemePreset preset) onSelectPreset;

  /// Builds a ViewModel from the Redux [store].
  factory AppearanceSectionViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return AppearanceSectionViewModel(
      activePreset: AppearanceSelectors.activePresetSelector(state),
      onSelectPreset: (DovahThemePreset preset) =>
          store.dispatch(ThemePresetSelectedAction(preset)),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [activePreset];
}
