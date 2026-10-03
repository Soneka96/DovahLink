import 'dart:io';

import 'package:redux/redux.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/device_identity/data/datasources/device_identity_local.datasource.dart';
import 'package:dovahlink_client/features/device_identity/data/repositories/device_identity.repository.dart';
import 'package:dovahlink_client/features/device_identity/domain/repositories/device_identity_repository.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/load_device_name.usecase.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/set_device_name.usecase.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.middleware.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/viewmodels/device_identity_section.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Registers local device-identity dependencies.
void initDeviceIdentityDependencies() {
  sl.registerLazySingleton<IDeviceIdentityLocalDataSource>(
    () => DeviceIdentityLocalDataSource(
      preferences: sl<SharedPreferencesAsync>(),
      platformDeviceName: () => Platform.localHostname,
    ),
  );
  sl.registerLazySingleton<IDeviceIdentityRepository>(
    () => DeviceIdentityRepository(sl<IDeviceIdentityLocalDataSource>()),
  );
  sl.registerLazySingleton<ILoadDeviceNameUseCase>(
    () => LoadDeviceNameUseCase(sl<IDeviceIdentityRepository>()),
  );
  sl.registerLazySingleton<ISetDeviceNameUseCase>(
    () => SetDeviceNameUseCase(sl<IDeviceIdentityRepository>()),
  );
  sl.registerLazySingleton<IDeviceIdentityMiddleware>(
    DeviceIdentityMiddleware.new,
  );
  sl.registerFactoryParam<
    DeviceIdentitySectionViewModel,
    Store<AppState>,
    void
  >(
    (Store<AppState> store, void _) =>
        DeviceIdentitySectionViewModel.fromStore(store),
  );
}
