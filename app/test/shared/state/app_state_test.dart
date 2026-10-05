import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises root application-state initialization.
void main() {
  group('Method initial behaves correctly', () {
    test('Method initial creates the connection state', () {
      final AppState state = AppState.initial();

      expect(state, isA<AppState>());
      expect(state.connection, isA<ConnectionState>());
      expect(state.connection.hosts, isEmpty);
      expect(state.connection.discoveryStatus, ConnectionDiscoveryStatus.idle);
    });

    test('Method initial creates the pairing state', () {
      final AppState state = AppState.initial();

      expect(state.pairing, isA<PairingState>());
      expect(state.pairing.phase, PairingPhase.none);
      expect(state.pairing.hostVersion, isNull);
      expect(state.pairing.error, isNull);
    });

    test(
      'Method initial creates the default appearance state when omitted',
      () {
        final AppState state = AppState.initial();

        expect(state.appearance, isA<AppearanceState>());
        expect(state.appearance.activePreset, defaultThemePreset);
      },
    );

    test('Method initial uses the given appearance state when provided', () {
      const AppearanceState appearance = AppearanceState(
        activePreset: DovahThemePreset.hearth,
      );

      final AppState state = AppState.initial(appearance: appearance);

      expect(state.appearance, appearance);
    });
  });

  group('Property appearance behaves correctly', () {
    test(
      'Property appearance defaults to defaultThemePreset when the constructor omits it',
      () {
        const AppState state = AppState(
          connection: ConnectionState(),
          pairing: PairingState(
            phase: PairingPhase.none,
            hostVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: null,
          ),
        );

        expect(state.appearance.activePreset, defaultThemePreset);
      },
    );
  });

  group('Property deviceIdentity behaves correctly', () {
    test('Property deviceIdentity defaults to the initial local name', () {
      final AppState state = AppState.initial();

      expect(state.deviceIdentity, DeviceIdentityState.initial());
    });

    test('Property deviceIdentity uses the loaded value when supplied', () {
      const DeviceIdentityState deviceIdentity = DeviceIdentityState(
        displayName: 'Saved Device',
      );

      final AppState state = AppState.initial(deviceIdentity: deviceIdentity);

      expect(state.deviceIdentity, deviceIdentity);
    });
  });

  group('Property liveState behaves correctly', () {
    test('AppState initial creates a fresh SessionLiveState projection', () {
      final AppState state = AppState.initial();

      expect(state.liveState, isA<SessionLiveState>());
      expect(state.liveState, const SessionLiveState.initial());
    });

    test(
      'AppState initial uses an explicitly supplied live-state projection',
      () {
        const SessionLiveState liveState = SessionLiveState.initial();

        final AppState state = AppState.initial(liveState: liveState);

        expect(identical(state.liveState, liveState), isTrue);
      },
    );
  });
}
