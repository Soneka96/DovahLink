import 'package:redux/redux.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/appearance/data/datasources/appearance_local.datasource.dart';
import 'package:dovahlink_client/features/appearance/data/repositories/appearance.repository.dart';
import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/load_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/set_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Registers appearance dependencies.
void initAppearanceDependencies() {
  sl.registerLazySingleton<IAppearanceLocalDataSource>(
    () => AppearanceLocalDataSource(sl<SharedPreferencesAsync>()),
  );
  sl.registerLazySingleton<IAppearanceRepository>(
    () => AppearanceRepository(sl<IAppearanceLocalDataSource>()),
  );
  sl.registerLazySingleton<LoadThemePresetUseCase>(
    () => LoadThemePresetUseCase(sl<IAppearanceRepository>()),
  );
  sl.registerLazySingleton<SetThemePresetUseCase>(
    () => SetThemePresetUseCase(sl<IAppearanceRepository>()),
  );
  sl.registerFactoryParam<AppearanceSectionViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return AppearanceSectionViewModel.fromStore(store);
  });
}
