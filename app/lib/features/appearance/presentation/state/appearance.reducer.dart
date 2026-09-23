import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';

/// Reduces appearance actions into [AppearanceState].
Reducer<AppearanceState> appearanceReducer = combineReducers<AppearanceState>([
  TypedReducer<AppearanceState, ThemePresetSelectedAction>(
    themePresetSelectedReducer,
  ).call,
]);

/// Handles [ThemePresetSelectedAction] by immediately updating [AppearanceState.activePreset].
AppearanceState themePresetSelectedReducer(
  AppearanceState state,
  ThemePresetSelectedAction action,
) => state.copyWith(activePreset: action.preset);
