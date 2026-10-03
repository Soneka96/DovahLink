import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/device_identity/data/datasources/device_identity_local.datasource.dart';
import 'package:dovahlink_client/features/device_identity/data/repositories/device_identity.repository.dart';
import 'package:dovahlink_client/features/device_identity/device_identity.injection_container.dart';
import 'package:dovahlink_client/features/device_identity/domain/repositories/device_identity_repository.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/load_device_name.usecase.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/set_device_name.usecase.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';

/// Mocks the preferences dependency registered by the feature.
class MockDeviceIdentityInjectionPreferences extends Mock
    implements SharedPreferencesAsync {}

/// Exercises device-identity dependency registration.
void main() {
  setUp(() async {
    await sl.reset();
    sl.registerSingleton<SharedPreferencesAsync>(
      MockDeviceIdentityInjectionPreferences(),
    );
  });

  tearDown(() async {
    await sl.reset();
  });

  group('Method initDeviceIdentityDependencies behaves correctly', () {
    test('Method initDeviceIdentityDependencies registers its app graph', () {
      initDeviceIdentityDependencies();

      expect(
        sl<IDeviceIdentityLocalDataSource>(),
        isA<DeviceIdentityLocalDataSource>(),
      );
      expect(sl<IDeviceIdentityRepository>(), isA<DeviceIdentityRepository>());
      expect(sl<ILoadDeviceNameUseCase>(), isA<LoadDeviceNameUseCase>());
      expect(sl<ISetDeviceNameUseCase>(), isA<SetDeviceNameUseCase>());
      expect(sl<IDeviceIdentityMiddleware>(), isA<DeviceIdentityMiddleware>());
    });
  });
}
