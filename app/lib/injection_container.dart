import 'package:get_it/get_it.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/features/pairing/pairing.injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_router.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';

/// Global service locator used by registered application dependencies.
final GetIt sl = GetIt.instance;

/// Registers shared and feature dependencies once before [runApp].
void initDependencies() {
  if (sl.isRegistered<GoRouter>()) {
    return;
  }
  sl.registerLazySingleton<GoRouter>(createRouter);
  sl.registerLazySingleton<NavigatorService>(
    () => NavigatorService(sl<GoRouter>()),
  );
  initConnectionDependencies();
  initPairingDependencies();
}
