import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

/// Exercises [DeviceIdentityState] values.
void main() {
  group('Method initial behaves correctly', () {
    test('Method initial uses defaultDeviceName when no name is loaded', () {
      expect(
        DeviceIdentityState.initial(),
        const DeviceIdentityState(displayName: defaultDeviceName),
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality includes the resolved name and load failure', () {
      const DeviceIdentityState loaded = DeviceIdentityState(
        displayName: 'Desktop',
      );
      const DeviceIdentityState unavailable = DeviceIdentityState(
        displayName: null,
        loadFailure: 'Preferences unavailable.',
      );

      expect(loaded, isNot(unavailable));
    });
  });
}
