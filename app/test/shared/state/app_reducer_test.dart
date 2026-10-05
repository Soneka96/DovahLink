import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_reducer.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../../fixtures/fixtures.dart';

/// Exercises root Redux reducer pass-through and delegation.
void main() {
  group('Action Object behaves correctly', () {
    test('Object returns the same state for an unhandled action', () {
      final AppState state = AppState.initial();

      expect(identical(appReducer(state, Object()), state), isTrue);
    });

    test('Object preserves the live-state slice by identity', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(state, Object());

      expect(identical(result.liveState, state.liveState), isTrue);
    });
  });

  group('Action PairingStartedAction behaves correctly', () {
    test('PairingStartedAction delegates to the pairing reducer', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(state, const PairingStartedAction());

      expect(result, isA<AppState>());
      expect(result.pairing.phase, PairingPhase.connecting);
      expect(result, isNot(same(state)));
    });

    test('PairingStartedAction leaves AppState.connection unchanged', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(state, const PairingStartedAction());

      expect(identical(result.connection, state.connection), isTrue);
    });

    test('PairingStartedAction leaves AppState.appearance unchanged', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(state, const PairingStartedAction());

      expect(identical(result.appearance, state.appearance), isTrue);
    });
  });

  group('Action ConnectionHostSelectedAction behaves correctly', () {
    test(
      'ConnectionHostSelectedAction delegates to the connection reducer',
      () {
        final AppState state = AppState.initial();

        final AppState result = appReducer(
          state,
          ConnectionHostSelectedAction(Fixtures.buildHost()),
        );

        expect(result.connection.selectedHost, Fixtures.buildHost());
        expect(result, isNot(same(state)));
      },
    );

    test('ConnectionHostSelectedAction leaves AppState.pairing unchanged', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(
        state,
        ConnectionHostSelectedAction(Fixtures.buildHost()),
      );

      expect(identical(result.pairing, state.pairing), isTrue);
    });

    test(
      'ConnectionHostSelectedAction leaves AppState.appearance unchanged',
      () {
        final AppState state = AppState.initial();

        final AppState result = appReducer(
          state,
          ConnectionHostSelectedAction(Fixtures.buildHost()),
        );

        expect(identical(result.appearance, state.appearance), isTrue);
      },
    );
  });

  group('Action ThemePresetSelectedAction behaves correctly', () {
    test('ThemePresetSelectedAction delegates to the appearance reducer', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(
        state,
        const ThemePresetSelectedAction(DovahThemePreset.hearth),
      );

      expect(result, isA<AppState>());
      expect(result.appearance.activePreset, DovahThemePreset.hearth);
      expect(result, isNot(same(state)));
    });

    test('ThemePresetSelectedAction leaves AppState.pairing unchanged', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(
        state,
        const ThemePresetSelectedAction(DovahThemePreset.hearth),
      );

      expect(identical(result.pairing, state.pairing), isTrue);
    });

    test('ThemePresetSelectedAction leaves AppState.connection unchanged', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(
        state,
        const ThemePresetSelectedAction(DovahThemePreset.hearth),
      );

      expect(identical(result.connection, state.connection), isTrue);
    });
  });

  group('Action DeviceNameSavedAction behaves correctly', () {
    test('DeviceNameSavedAction delegates to the identity reducer', () {
      final AppState state = AppState.initial(
        deviceIdentity: const DeviceIdentityState(
          displayName: null,
          loadFailure: 'Preferences unavailable.',
        ),
      );

      final AppState result = appReducer(
        state,
        const DeviceNameSavedAction(displayName: 'Saved Device'),
      );

      expect(result.deviceIdentity.displayName, 'Saved Device');
      expect(result.deviceIdentity.loadFailure, isNull);
    });

    test(
      'DeviceNameSavedAction leaves connection and pairing state unchanged',
      () {
        final AppState state = AppState.initial();

        final AppState result = appReducer(
          state,
          const DeviceNameSavedAction(displayName: 'Saved Device'),
        );

        expect(identical(result.connection, state.connection), isTrue);
        expect(identical(result.pairing, state.pairing), isTrue);
        expect(identical(result.appearance, state.appearance), isTrue);
      },
    );
  });

  group('Action CharacterXpSynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterXpSynchronizationChangedAction updates the live-state slice',
      () {
        final AppState state = AppState.initial();
        const CharacterXpSynchronizationChangedAction action =
            CharacterXpSynchronizationChangedAction(
              LiveDomainState<double?>(
                status: LiveStateStatus.unavailable,
                value: null,
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 1,
              ),
            );

        final AppState result = appReducer(state, action);

        expect(
          result.liveState.characterXp.status,
          LiveStateStatus.unavailable,
        );
        expect(result.liveState.characterXp.value, isNull);
        expect(identical(result.connection, state.connection), isTrue);
        expect(identical(result.pairing, state.pairing), isTrue);
        expect(identical(result.appearance, state.appearance), isTrue);
        expect(identical(result.deviceIdentity, state.deviceIdentity), isTrue);
      },
    );
  });
}
