import 'package:get_it/get_it.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/appearance/appearance.injection_container.dart';
import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/features/pairing/pairing.injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_router.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';

/// Global service locator used by registered application dependencies.
final GetIt sl = GetIt.instance;

/// Registers shared and feature dependencies once before [runApp]. Async only because the
/// appearance feature's persistence depends on [SharedPreferences.getInstance], which is itself
/// async; every other registration here remains synchronous.
Future<void> initDependencies() async {
  if (sl.isRegistered<GoRouter>()) {
    return;
  }
  final SharedPreferences preferences = await SharedPreferences.getInstance();
  sl.registerLazySingleton<GoRouter>(createRouter);
  sl.registerLazySingleton<NavigatorService>(
    () => NavigatorService(sl<GoRouter>()),
  );
  sl.registerLazySingleton<SharedPreferences>(() => preferences);
  initConnectionDependencies();
  initPairingDependencies();
  initAppearanceDependencies();
}
