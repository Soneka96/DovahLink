import 'package:get_it/get_it.dart';
import 'package:go_router/go_router.dart';
import 'package:redux/redux.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/app.viewmodel.dart';
import 'package:dovahlink_client/features/appearance/appearance.injection_container.dart';
import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/features/pairing/pairing.injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_router.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Global service locator used by registered application dependencies.
final GetIt sl = GetIt.instance;

/// Registers shared and feature dependencies once before [runApp] without reading appearance
/// preferences during startup.
Future<void> initDependencies() async {
  if (sl.isRegistered<GoRouter>()) {
    return;
  }
  sl.registerLazySingleton<GoRouter>(createRouter);
  sl.registerLazySingleton<NavigatorService>(
    () => NavigatorService(sl<GoRouter>()),
  );
  sl.registerLazySingleton<SharedPreferencesAsync>(
    () => SharedPreferencesAsync(),
  );
  sl.registerFactoryParam<DovahLinkAppViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return DovahLinkAppViewModel.fromStore(store);
  });
  initConnectionDependencies();
  initPairingDependencies();
  initAppearanceDependencies();
}
