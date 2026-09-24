import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
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
}
