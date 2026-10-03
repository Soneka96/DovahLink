import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/device_identity/domain/usecases/params/set_device_name.params.dart';

/// Exercises equality for [SetDeviceNameParams].
void main() {
  group('Behavior equality behaves correctly', () {
    test('Behavior equality includes the proposed display name', () {
      const SetDeviceNameParams first = SetDeviceNameParams(
        displayName: 'Device A',
      );
      const SetDeviceNameParams second = SetDeviceNameParams(
        displayName: 'Device B',
      );

      expect(first, isNot(second));
    });

    test('Behavior equality gives matching hashes for equal display names', () {
      const SetDeviceNameParams first = SetDeviceNameParams(
        displayName: 'Device A',
      );
      const SetDeviceNameParams second = SetDeviceNameParams(
        displayName: 'Device A',
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });
  });
}
