import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/domain/usecases/params/set_theme_preset.params.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/set_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Handles appearance actions, resolving its use cases through the shared [sl] container.
class AppearanceMiddleware extends MiddlewareClass<AppState> {
  /// See [MiddlewareClass.call].
  @override
  void call(Store<AppState> store, dynamic action, NextDispatcher next) {
    next(action);

    switch (action) {
      case ThemePresetSelectedAction _:
        _themePresetSelected(store, action);
      default:
        break;
    }
  }

  /// Persists [ThemePresetSelectedAction.preset]. The reducer applies the preset immediately, so a
  /// persistence failure affects whether it survives the next launch but does not undo the active
  /// appearance.
  Future<void> _themePresetSelected(
    Store<AppState> store,
    ThemePresetSelectedAction action,
  ) async {
    await sl<SetThemePresetUseCase>()(
      SetThemePresetParams(preset: action.preset),
    );
  }
}
