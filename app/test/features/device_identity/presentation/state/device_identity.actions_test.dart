import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises equality for [DeviceNameSavedAction].
void main() {
  group('Behavior equality behaves correctly', () {
    test('Behavior equality includes the persisted display name', () {
      const DeviceNameSavedAction first = DeviceNameSavedAction(
        displayName: 'Device A',
      );
      const DeviceNameSavedAction second = DeviceNameSavedAction(
        displayName: 'Device B',
      );

      expect(first, isNot(second));
    });

    test('Behavior equality gives matching hashes for equal names', () {
      const DeviceNameSavedAction first = DeviceNameSavedAction(
        displayName: 'Device A',
      );
      const DeviceNameSavedAction second = DeviceNameSavedAction(
        displayName: 'Device A',
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality includes a save request name', () {
      const DeviceNameSaveRequestedAction first = DeviceNameSaveRequestedAction(
        displayName: 'A',
      );
      const DeviceNameSaveRequestedAction second =
          DeviceNameSaveRequestedAction(displayName: 'B');

      expect(first, isNot(second));
    });

    test('Behavior equality includes a local save failure', () {
      const DeviceNameSaveFailedAction first = DeviceNameSaveFailedAction(
        message: 'A',
      );
      const DeviceNameSaveFailedAction second = DeviceNameSaveFailedAction(
        message: 'B',
      );

      expect(first, isNot(second));
    });

    test('Behavior equality preserves the Host rename status', () {
      const DeviceNameSaveFinishedAction first = DeviceNameSaveFinishedAction(
        remoteRenameStatus: DeviceNameRenameStatus.renamed,
        remoteHostId: 'host-a',
      );
      const DeviceNameSaveFinishedAction differentStatus =
          DeviceNameSaveFinishedAction(
            remoteRenameStatus: DeviceNameRenameStatus.notTrusted,
            remoteHostId: 'host-a',
          );
      const DeviceNameSaveFinishedAction differentHost =
          DeviceNameSaveFinishedAction(
            remoteRenameStatus: DeviceNameRenameStatus.renamed,
            remoteHostId: 'host-b',
          );

      expect(first, isNot(differentStatus));
      expect(first, isNot(differentHost));
    });
  });
}
