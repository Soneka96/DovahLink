import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.selectors.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises [DeviceIdentitySelectors] over [AppState].
void main() {
  group('Property displayNameSelector behaves correctly', () {
    test('Property displayNameSelector returns the resolved name', () {
      final AppState state = AppState.initial(
        deviceIdentity: const DeviceIdentityState(displayName: 'Desktop'),
      );

      expect(DeviceIdentitySelectors.displayNameSelector(state), 'Desktop');
    });

    test('Property displayNameSelector preserves an unavailable name', () {
      final AppState state = AppState.initial(
        deviceIdentity: const DeviceIdentityState(
          displayName: null,
          loadFailure: 'Preferences unavailable.',
        ),
      );

      expect(DeviceIdentitySelectors.displayNameSelector(state), isNull);
    });
  });

  group('Property loadFailureSelector behaves correctly', () {
    test('Property loadFailureSelector returns the load error', () {
      final AppState state = AppState.initial(
        deviceIdentity: const DeviceIdentityState(
          displayName: null,
          loadFailure: 'Preferences unavailable.',
        ),
      );

      expect(
        DeviceIdentitySelectors.loadFailureSelector(state),
        'Preferences unavailable.',
      );
    });
  });

  group('Property isSavingSelector behaves correctly', () {
    test('Property isSavingSelector returns the pending save state', () {
      final AppState state = AppState.initial(
        deviceIdentity: const DeviceIdentityState(
          displayName: 'Desktop',
          isSaving: true,
        ),
      );

      expect(DeviceIdentitySelectors.isSavingSelector(state), isTrue);
    });
  });

  group('Property saveFailureSelector behaves correctly', () {
    test('Property saveFailureSelector returns the local save error', () {
      final AppState state = AppState.initial(
        deviceIdentity: const DeviceIdentityState(
          displayName: 'Desktop',
          saveFailure: 'Preferences unavailable.',
        ),
      );

      expect(
        DeviceIdentitySelectors.saveFailureSelector(state),
        'Preferences unavailable.',
      );
    });
  });

  group('Property remoteRenameStatusSelector behaves correctly', () {
    test('Property remoteRenameStatusSelector preserves the Host outcome', () {
      final AppState state = AppState.initial(
        deviceIdentity: const DeviceIdentityState(
          displayName: 'Desktop',
          remoteRenameStatus: DeviceNameRenameStatus.invalidDisplayName,
          remoteHostId: 'host-1',
        ),
      );

      expect(
        DeviceIdentitySelectors.remoteRenameStatusSelector(state),
        DeviceNameRenameStatus.invalidDisplayName,
      );
      expect(DeviceIdentitySelectors.remoteHostIdSelector(state), 'host-1');
    });
  });
}
