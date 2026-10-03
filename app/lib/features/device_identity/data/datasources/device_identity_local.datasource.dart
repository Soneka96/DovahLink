import 'dart:convert';

import 'package:flutter/services.dart' show PlatformException;

import 'package:fpdart/fpdart.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Reads and writes the device-name override and resolves the OS-name fallback.
abstract interface class IDeviceIdentityLocalDataSource {
  /// Loads the saved name or the available OS name, falling back to [defaultDeviceName].
  /// @return The resolved display name, or a persistence [Failure].
  Future<Either<Failure, String>> loadDisplayName();

  /// Persists [displayName] as the user's device-name override.
  /// @param displayName The validated non-empty display name to persist.
  /// @return `Right` when persisted, or a persistence [Failure].
  Future<Either<Failure, Unit>> saveDisplayNameOverride(String displayName);
}

/// The Shared Preferences key for the device-name override.
const String _deviceNamePreferenceKey = 'dovahlink.identity.deviceName';

/// The control-character range rejected by Host display-name validation.
final RegExp _deviceNameControlCharacterPattern = RegExp(
  r'[\x00-\x1F\x7F-\x9F]',
);

/// A safe generic failure for unexpected device-name persistence errors.
const DatabaseFailure _unexpectedDeviceNameFailure = DatabaseFailure(
  'The device name could not be read or saved.',
);

/// Persists the device-name override in [SharedPreferencesAsync].
class DeviceIdentityLocalDataSource implements IDeviceIdentityLocalDataSource {
  /// The asynchronous preferences store.
  final SharedPreferencesAsync _preferences;

  /// Reads the current OS device name.
  final String Function() _platformDeviceName;

  /// Creates a device-identity data source.
  /// @param preferences The local preferences store.
  /// @param platformDeviceName Reads the OS device name when no override is stored.
  DeviceIdentityLocalDataSource({
    required SharedPreferencesAsync preferences,
    required String Function() platformDeviceName,
  }) : _preferences = preferences,
       _platformDeviceName = platformDeviceName;

  /// Implements [IDeviceIdentityLocalDataSource.loadDisplayName].
  @override
  Future<Either<Failure, String>> loadDisplayName() async {
    try {
      final String? stored = await _preferences.getString(
        _deviceNamePreferenceKey,
      );
      if (stored != null && stored.trim().isNotEmpty) {
        return Right(stored.trim());
      }
    } on PlatformException catch (error) {
      return Left(
        DatabaseFailure(
          error.message ?? 'Could not read the saved device name.',
        ),
      );
    } on Object {
      return const Left(_unexpectedDeviceNameFailure);
    }

    try {
      final String platformName = _platformDeviceName().trim();
      return Right(
        _isValidPlatformDeviceName(platformName)
            ? platformName
            : defaultDeviceName,
      );
    } on Object {
      return const Right(defaultDeviceName);
    }
  }

  /// Implements [IDeviceIdentityLocalDataSource.saveDisplayNameOverride].
  @override
  Future<Either<Failure, Unit>> saveDisplayNameOverride(
    String displayName,
  ) async {
    try {
      await _preferences.setString(_deviceNamePreferenceKey, displayName);
      return const Right(unit);
    } on PlatformException catch (error) {
      return Left(
        DatabaseFailure(error.message ?? 'Could not save the device name.'),
      );
    } on Object {
      return const Left(_unexpectedDeviceNameFailure);
    }
  }

  /// Checks that [platformName] fits the Host's device-name bounds.
  /// @param platformName The trimmed operating-system device name.
  /// @return `true` when the name is non-empty, within the UTF-8 byte limit,
  /// and contains no control characters.
  bool _isValidPlatformDeviceName(String platformName) =>
      platformName.isNotEmpty &&
      utf8.encode(platformName).length <= maxDeviceNameLengthBytes &&
      !_deviceNameControlCharacterPattern.hasMatch(platformName);
}
