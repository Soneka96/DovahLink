import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';

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
  });
}
