import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/app/app.viewmodel.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Exercises [DovahLinkAppViewModel.fromStore] and value equality.
void main() {
  group('Method fromStore behaves correctly', () {
    test('Method fromStore maps the active theme preset', () {
      final Store<AppState> store = const CreateStore()(
        initialState: AppState.initial(
          appearance: const AppearanceState(
            activePreset: DovahThemePreset.hearth,
          ),
        ),
      );

      final DovahLinkAppViewModel viewModel = DovahLinkAppViewModel.fromStore(
        store,
      );

      expect(viewModel.activePreset, DovahThemePreset.hearth);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds when the active preset matches', () {
      final DovahThemePreset activePreset = DovahThemePreset.values.last;
      final DovahLinkAppViewModel first = DovahLinkAppViewModel(
        activePreset: activePreset,
      );
      final DovahLinkAppViewModel second = DovahLinkAppViewModel(
        activePreset: activePreset,
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality differs when the active preset changes', () {
      const DovahLinkAppViewModel first = DovahLinkAppViewModel(
        activePreset: DovahThemePreset.dovah,
      );
      const DovahLinkAppViewModel second = DovahLinkAppViewModel(
        activePreset: DovahThemePreset.hearth,
      );

      expect(first, isNot(second));
    });
  });
}
