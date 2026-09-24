import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Redux-backed presentation value for the root application widget.
class DovahLinkAppViewModel extends Equatable {
  /// Creates the root application ViewModel.
  const DovahLinkAppViewModel({required this.activePreset});

  /// The active theme preset.
  final DovahThemePreset activePreset;

  /// Creates the ViewModel from the current Redux [store].
  factory DovahLinkAppViewModel.fromStore(Store<AppState> store) =>
      DovahLinkAppViewModel(
        activePreset: AppearanceSelectors.activePresetSelector(store.state),
      );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [activePreset];
}
