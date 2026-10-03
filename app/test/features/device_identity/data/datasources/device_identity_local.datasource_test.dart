import 'package:flutter/services.dart' show PlatformException;

import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/device_identity/data/datasources/device_identity_local.datasource.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Mocks asynchronous preference reads and writes.
class MockDeviceIdentityPreferences extends Mock
    implements SharedPreferencesAsync {}

/// The preference key used by the production data source.
const String _deviceNamePreferenceKey = 'dovahlink.identity.deviceName';

/// The generic data-source failure shared by unexpected read and write errors.
const DatabaseFailure _unexpectedDeviceNameFailure = DatabaseFailure(
  'The device name could not be read or saved.',
);

/// Builds a data source backed by an isolated preference mock.
/// @param storedOverride The initial saved override, or `null` when absent.
/// @param platformName The representative operating-system device name.
/// @return A data source with the supplied local values.
DeviceIdentityLocalDataSource _buildDataSource({
  String? storedOverride,
  String platformName = 'TEST-DESKTOP',
}) {
  final MockDeviceIdentityPreferences preferences =
      MockDeviceIdentityPreferences();
  when(
    () => preferences.getString(_deviceNamePreferenceKey),
  ).thenAnswer((_) async => storedOverride);
  when(
    () => preferences.setString(_deviceNamePreferenceKey, any()),
  ).thenAnswer((_) async {});
  return DeviceIdentityLocalDataSource(
    preferences: preferences,
    platformDeviceName: () => platformName,
  );
}

/// Exercises local device-name preference and platform fallback behavior.
void main() {
  group('Method loadDisplayName behaves correctly', () {
    test(
      'Method loadDisplayName resolves the OS name when no override is stored',
      () async {
        final DeviceIdentityLocalDataSource dataSource = _buildDataSource();

        expect(await dataSource.loadDisplayName(), const Right('TEST-DESKTOP'));
      },
    );

    test(
      'Method loadDisplayName accepts an OS name at the 64-byte limit',
      () async {
        final String platformName = List<String>.filled(32, 'é').join();

        expect(
          await _buildDataSource(platformName: platformName).loadDisplayName(),
          Right(platformName),
        );
      },
    );

    test(
      'Method loadDisplayName uses the default when the OS name exceeds the UTF-8 byte limit',
      () async {
        final String platformName = List<String>.filled(33, 'é').join();

        expect(
          await _buildDataSource(platformName: platformName).loadDisplayName(),
          const Right(defaultDeviceName),
        );
      },
    );

    test(
      'Method loadDisplayName uses the default when the OS name contains control characters',
      () async {
        for (final String controlCharacter in ['\u0001', '\u0085']) {
          expect(
            await _buildDataSource(
              platformName: 'BAD${controlCharacter}NAME',
            ).loadDisplayName(),
            const Right(defaultDeviceName),
          );
        }
      },
    );

    test(
      'Method loadDisplayName trims and returns the stored override',
      () async {
        final DeviceIdentityLocalDataSource dataSource = _buildDataSource(
          storedOverride: '  Living Room PC  ',
        );

        expect(
          await dataSource.loadDisplayName(),
          const Right('Living Room PC'),
        );
      },
    );

    test(
      'Method loadDisplayName keeps an over-limit stored override unchanged',
      () async {
        final String storedOverride = List<String>.filled(33, 'é').join();

        expect(
          await _buildDataSource(
            storedOverride: storedOverride,
          ).loadDisplayName(),
          Right(storedOverride),
        );
      },
    );

    test(
      'Method loadDisplayName uses the OS fallback for a blank saved override',
      () async {
        final DeviceIdentityLocalDataSource dataSource = _buildDataSource(
          storedOverride: '  ',
        );

        expect(await dataSource.loadDisplayName(), const Right('TEST-DESKTOP'));
      },
    );

    test(
      'Method loadDisplayName uses defaultDeviceName when OS name is empty or unavailable',
      () async {
        expect(
          await _buildDataSource(platformName: '').loadDisplayName(),
          const Right(defaultDeviceName),
        );
        final MockDeviceIdentityPreferences preferences =
            MockDeviceIdentityPreferences();
        when(
          () => preferences.getString(_deviceNamePreferenceKey),
        ).thenAnswer((_) async => null);
        final DeviceIdentityLocalDataSource unavailable =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => throw StateError('OS name unavailable'),
            );

        expect(
          await unavailable.loadDisplayName(),
          const Right(defaultDeviceName),
        );
      },
    );

    test(
      'Method loadDisplayName returns DatabaseFailure when preference reads fail',
      () async {
        final MockDeviceIdentityPreferences preferences =
            MockDeviceIdentityPreferences();
        when(() => preferences.getString(_deviceNamePreferenceKey)).thenAnswer(
          (_) => Future<String?>.error(
            PlatformException(
              code: 'read-failed',
              message: 'Storage unavailable.',
            ),
          ),
        );
        final DeviceIdentityLocalDataSource dataSource =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => 'TEST-DESKTOP',
            );

        expect(
          await dataSource.loadDisplayName(),
          const Left(DatabaseFailure('Storage unavailable.')),
        );
        verifyNever(
          () => preferences.setString(_deviceNamePreferenceKey, any()),
        );
      },
    );

    test(
      'Method loadDisplayName uses fallback copy for an unmessaged PlatformException',
      () async {
        final MockDeviceIdentityPreferences preferences =
            MockDeviceIdentityPreferences();
        when(() => preferences.getString(_deviceNamePreferenceKey)).thenAnswer(
          (_) => Future<String?>.error(PlatformException(code: 'read-failed')),
        );
        final DeviceIdentityLocalDataSource dataSource =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => 'TEST-DESKTOP',
            );

        expect(
          await dataSource.loadDisplayName(),
          const Left(DatabaseFailure('Could not read the saved device name.')),
        );
      },
    );

    test(
      'Method loadDisplayName translates unexpected preference errors safely',
      () async {
        final MockDeviceIdentityPreferences preferences =
            MockDeviceIdentityPreferences();
        when(
          () => preferences.getString(_deviceNamePreferenceKey),
        ).thenAnswer((_) => Future<String?>.error(StateError('unexpected')));
        final DeviceIdentityLocalDataSource dataSource =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => 'TEST-DESKTOP',
            );

        expect(
          await dataSource.loadDisplayName(),
          const Left(_unexpectedDeviceNameFailure),
        );
      },
    );
  });

  group('Method saveDisplayNameOverride behaves correctly', () {
    test(
      'Method saveDisplayNameOverride persists a name that loads after recreation',
      () async {
        final MockDeviceIdentityPreferences preferences =
            MockDeviceIdentityPreferences();
        String? storedOverride;
        when(
          () => preferences.getString(_deviceNamePreferenceKey),
        ).thenAnswer((_) async => storedOverride);
        when(
          () => preferences.setString(_deviceNamePreferenceKey, any()),
        ).thenAnswer((Invocation invocation) async {
          storedOverride = invocation.positionalArguments[1] as String;
        });
        final DeviceIdentityLocalDataSource first =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => 'OS-NAME',
            );

        expect(
          await first.saveDisplayNameOverride('Saved Device'),
          const Right(unit),
        );
        final DeviceIdentityLocalDataSource restarted =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => 'OS-NAME',
            );

        expect(await restarted.loadDisplayName(), const Right('Saved Device'));
        verify(
          () => preferences.setString(_deviceNamePreferenceKey, 'Saved Device'),
        ).called(1);
      },
    );

    test(
      'Method saveDisplayNameOverride returns DatabaseFailure when writes fail',
      () async {
        final MockDeviceIdentityPreferences preferences =
            MockDeviceIdentityPreferences();
        when(
          () => preferences.setString(_deviceNamePreferenceKey, 'Saved Device'),
        ).thenAnswer(
          (_) => Future<void>.error(
            PlatformException(
              code: 'write-failed',
              message: 'Storage unavailable.',
            ),
          ),
        );
        final DeviceIdentityLocalDataSource dataSource =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => 'OS-NAME',
            );

        expect(
          await dataSource.saveDisplayNameOverride('Saved Device'),
          const Left(DatabaseFailure('Storage unavailable.')),
        );
      },
    );

    test(
      'Method saveDisplayNameOverride uses fallback copy for an unmessaged PlatformException',
      () async {
        final MockDeviceIdentityPreferences preferences =
            MockDeviceIdentityPreferences();
        when(
          () => preferences.setString(_deviceNamePreferenceKey, 'Saved Device'),
        ).thenAnswer(
          (_) => Future<void>.error(PlatformException(code: 'write-failed')),
        );
        final DeviceIdentityLocalDataSource dataSource =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => 'OS-NAME',
            );

        expect(
          await dataSource.saveDisplayNameOverride('Saved Device'),
          const Left(DatabaseFailure('Could not save the device name.')),
        );
      },
    );

    test(
      'Method saveDisplayNameOverride translates unexpected preference errors safely',
      () async {
        final MockDeviceIdentityPreferences preferences =
            MockDeviceIdentityPreferences();
        when(
          () => preferences.setString(_deviceNamePreferenceKey, 'Saved Device'),
        ).thenAnswer((_) => Future<void>.error(StateError('unexpected')));
        final DeviceIdentityLocalDataSource dataSource =
            DeviceIdentityLocalDataSource(
              preferences: preferences,
              platformDeviceName: () => 'OS-NAME',
            );

        expect(
          await dataSource.saveDisplayNameOverride('Saved Device'),
          const Left(_unexpectedDeviceNameFailure),
        );
      },
    );
  });
}
