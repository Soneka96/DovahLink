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

  /// Persists [ThemePresetSelectedAction.preset]. The reducer already applied the preset to
  /// [AppState] before this handler runs, so a persistence failure here is a background
  /// concern -- it affects only whether the choice survives the next launch, not what the user
  /// sees now -- and is left silent rather than dispatching a failure action, per
  /// `ai/context/flutter/error-handling.md`'s "Background failures that should not interrupt the
  /// user remain silent". No UI in this application surface currently reacts to a persistence
  /// outcome for this action; a future one that needs to should dispatch its own result action
  /// from this handler instead of this comment being treated as documentation of that behavior.
  Future<void> _themePresetSelected(
    Store<AppState> store,
    ThemePresetSelectedAction action,
  ) async {
    await sl<SetThemePresetUseCase>()(
      SetThemePresetParams(preset: action.preset),
    );
  }
}
