import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.reducer.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';

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
      },
    );
  });

  group('Action Object behaves correctly', () {
    test('Object returns the same state for an unhandled action', () {
      final DeviceIdentityState state = DeviceIdentityState.initial();

      expect(identical(deviceIdentityReducer(state, Object()), state), isTrue);
    });
  });
}
