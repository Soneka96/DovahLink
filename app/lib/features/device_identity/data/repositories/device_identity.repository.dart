import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/device_identity/data/datasources/device_identity_local.datasource.dart';
import 'package:dovahlink_client/features/device_identity/domain/repositories/device_identity_repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Implements [IDeviceIdentityRepository] over local device-name preferences.
class DeviceIdentityRepository implements IDeviceIdentityRepository {
  /// The local data source used for device-name persistence.
  final IDeviceIdentityLocalDataSource _localDataSource;

  /// Creates a repository backed by [_localDataSource].
  /// @param localDataSource The local source of display-name values.
  DeviceIdentityRepository(IDeviceIdentityLocalDataSource localDataSource)
    : _localDataSource = localDataSource;

  /// Implements [IDeviceIdentityRepository.loadDisplayName].
  @override
  Future<Either<Failure, String>> loadDisplayName() =>
      _localDataSource.loadDisplayName();

  /// Implements [IDeviceIdentityRepository.saveDisplayNameOverride].
  @override
  Future<Either<Failure, Unit>> saveDisplayNameOverride(String displayName) =>
      _localDataSource.saveDisplayNameOverride(displayName);
}
