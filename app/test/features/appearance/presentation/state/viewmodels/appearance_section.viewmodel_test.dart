import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Exercises [AppearanceSectionViewModel.fromStore] projections.
void main() {
  group('AppearanceSectionViewModel fromStore()', () {
    test('fromStore constructs a ViewModel with the default active preset', () {
      final AppearanceSectionViewModel viewModel =
          AppearanceSectionViewModel.fromStore(const CreateStore()());

      expect(viewModel.activePreset, defaultThemePreset);
    });

    test('fromStore reflects the store\'s currently active preset', () {
      final Store<AppState> store = const CreateStore()();
      store.dispatch(const ThemePresetSelectedAction(DovahThemePreset.hearth));

      final AppearanceSectionViewModel viewModel =
          AppearanceSectionViewModel.fromStore(store);

      expect(viewModel.activePreset, DovahThemePreset.hearth);
    });

    test(
      'fromStore\'s onSelectPreset dispatches ThemePresetSelectedAction',
      () {
        final Store<AppState> store = const CreateStore()();
        final AppearanceSectionViewModel viewModel =
            AppearanceSectionViewModel.fromStore(store);

        viewModel.onSelectPreset(DovahThemePreset.frostbound);

        expect(
          store.state.appearance.activePreset,
          DovahThemePreset.frostbound,
        );
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test(
      'Behavior equality holds for ViewModels built with the same activePreset',
      () {
        final AppearanceSectionViewModel first =
            AppearanceSectionViewModel.fromStore(const CreateStore()());
        final AppearanceSectionViewModel second =
            AppearanceSectionViewModel.fromStore(const CreateStore()());

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      },
    );

    test('Behavior equality fails when activePreset differs', () {
      final Store<AppState> otherStore = const CreateStore()();
      otherStore.dispatch(
        const ThemePresetSelectedAction(DovahThemePreset.hearth),
      );

      final AppearanceSectionViewModel first =
          AppearanceSectionViewModel.fromStore(const CreateStore()());
      final AppearanceSectionViewModel second =
          AppearanceSectionViewModel.fromStore(otherStore);

      expect(first, isNot(second));
    });
  });
}
