import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.reducer.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises device-identity state transitions.
void main() {
  group('Action DeviceNameSavedAction behaves correctly', () {
    test(
      'DeviceNameSavedAction stores the persisted name and clears load failure',
      () {
        const DeviceIdentityState state = DeviceIdentityState(
          displayName: null,
          loadFailure: 'Preferences unavailable.',
        );

        final DeviceIdentityState result = deviceIdentityReducer(
          state,
          const DeviceNameSavedAction(displayName: 'Desktop'),
        );

        expect(result.displayName, 'Desktop');
        expect(result.loadFailure, isNull);
        expect(result.isSaving, isTrue);
      },
    );
  });

  group('Action DeviceNameSaveRequestedAction behaves correctly', () {
    test(
      'DeviceNameSaveRequestedAction clears stale feedback and marks saving',
      () {
        const DeviceIdentityState state = DeviceIdentityState(
          displayName: 'Old Device',
          saveFailure: 'Old error.',
          remoteRenameStatus: DeviceNameRenameStatus.notTrusted,
          remoteHostId: 'host-1',
        );

        final DeviceIdentityState result = deviceIdentityReducer(
          state,
          const DeviceNameSaveRequestedAction(displayName: 'New Device'),
        );

        expect(result.displayName, 'Old Device');
        expect(result.isSaving, isTrue);
        expect(result.saveFailure, isNull);
        expect(result.remoteRenameStatus, DeviceNameRenameStatus.notAttempted);
        expect(result.remoteHostId, isNull);
      },
    );
  });

  group('Action DeviceNameSaveFailedAction behaves correctly', () {
    test(
      'DeviceNameSaveFailedAction keeps the local name and stops saving',
      () {
        const DeviceIdentityState state = DeviceIdentityState(
          displayName: 'Old Device',
          isSaving: true,
        );

        final DeviceIdentityState result = deviceIdentityReducer(
          state,
          const DeviceNameSaveFailedAction(message: 'Storage unavailable.'),
        );

        expect(result.displayName, 'Old Device');
        expect(result.isSaving, isFalse);
        expect(result.saveFailure, 'Storage unavailable.');
        expect(result.remoteRenameStatus, DeviceNameRenameStatus.notAttempted);
      },
    );
  });

  group('Action DeviceNameSaveFinishedAction behaves correctly', () {
    test('DeviceNameSaveFinishedAction records the Host and typed result', () {
      const DeviceIdentityState state = DeviceIdentityState(
        displayName: 'Saved Device',
        isSaving: true,
      );

      final DeviceIdentityState result = deviceIdentityReducer(
        state,
        const DeviceNameSaveFinishedAction(
          remoteRenameStatus: DeviceNameRenameStatus.unconfirmed,
          remoteHostId: 'host-1',
        ),
      );

      expect(result.displayName, 'Saved Device');
      expect(result.isSaving, isFalse);
      expect(result.remoteRenameStatus, DeviceNameRenameStatus.unconfirmed);
      expect(result.remoteHostId, 'host-1');
    });

    test('DeviceNameSaveFailedAction preserves a failed initial name load', () {
      const DeviceIdentityState state = DeviceIdentityState(
        displayName: null,
        loadFailure: 'Preferences unavailable.',
        isSaving: true,
      );

      final DeviceIdentityState result = deviceIdentityReducer(
        state,
        const DeviceNameSaveFailedAction(message: 'Write failed.'),
      );

      expect(result.displayName, isNull);
      expect(result.loadFailure, 'Preferences unavailable.');
      expect(result.isSaving, isFalse);
      expect(result.saveFailure, 'Write failed.');
    });
  });

  group('Action Object behaves correctly', () {
    test('Object returns the same state for an unhandled action', () {
      final DeviceIdentityState state = DeviceIdentityState.initial();

      expect(identical(deviceIdentityReducer(state, Object()), state), isTrue);
    });
  });
}
