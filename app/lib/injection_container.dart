import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

import 'package:get_it/get_it.dart';
import 'package:go_router/go_router.dart';
import 'package:redux/redux.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/app.viewmodel.dart';
import 'package:dovahlink_client/features/appearance/appearance.injection_container.dart';
import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/features/pairing/pairing.injection_container.dart';
import 'package:dovahlink_client/platform/windows/windows_lifecycle_bridge.dart';
import 'package:dovahlink_client/shared/navigation/app_router.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/utils/app_shutdown_service.dart';
import 'package:dovahlink_client/shared/utils/existing_dovahlink_client.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show IClientStorage, UnsupportedClientStorage;
import 'package:dovahlink_client_sdk/dovahlink_client_windows.dart'
    show DpapiClientStorage;

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
  final ExistingDovahLinkClient existingClient = ExistingDovahLinkClient();
  sl.registerSingleton<ExistingDovahLinkClient>(existingClient);
  sl.registerSingleton<IExistingDovahLinkClient>(existingClient);
  sl.registerFactoryParam<DovahLinkAppViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return DovahLinkAppViewModel.fromStore(store);
  });
  sl.registerLazySingleton<IClientStorage>(() {
    return switch (defaultTargetPlatform) {
      TargetPlatform.windows => DpapiClientStorage(),
      _ => const UnsupportedClientStorage(),
    };
  });
  initConnectionDependencies();
  initPairingDependencies();
  sl.registerLazySingleton<IAppShutdownService>(
    () => AppShutdownService(pairingMiddleware: sl(), existingClient: sl()),
  );
  initAppearanceDependencies();
  if (defaultTargetPlatform == TargetPlatform.windows) {
    sl.registerLazySingleton<IWindowsLifecycleBridge>(
      () => WindowsLifecycleBridge(
        channel: const MethodChannel('dovahlink/window_lifecycle'),
        shutdownService: sl(),
      ),
    );
    sl<IWindowsLifecycleBridge>().register();
  }
}
