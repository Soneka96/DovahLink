import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mock Store for [AppearanceSectionViewModel] consumer tests.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [AppearanceSectionViewModel.fromStore] projections.
void main() {
  late MockStore store;

  setUpAll(() {
    registerFallbackValue(
      const ThemePresetSelectedAction(DovahThemePreset.dovah),
    );
  });

  setUp(() {
    store = MockStore();
    when(() => store.state).thenReturn(AppState.initial());
  });

  group('Method fromStore behaves correctly', () {
    test('Method fromStore uses the default active preset', () {
      final AppearanceSectionViewModel viewModel =
          AppearanceSectionViewModel.fromStore(store);

      expect(viewModel.activePreset, defaultThemePreset);
    });

    test('Method fromStore reflects the store\'s active preset', () {
      when(() => store.state).thenReturn(
        AppState.initial(
          appearance: const AppearanceState(
            activePreset: DovahThemePreset.hearth,
          ),
        ),
      );

      final AppearanceSectionViewModel viewModel =
          AppearanceSectionViewModel.fromStore(store);

      expect(viewModel.activePreset, DovahThemePreset.hearth);
    });

    test(
      'Method fromStore creates an onSelectPreset callback that dispatches the selected action',
      () {
        when(() => store.dispatch(any())).thenAnswer((_) {});
        final AppearanceSectionViewModel viewModel =
            AppearanceSectionViewModel.fromStore(store);

        viewModel.onSelectPreset(DovahThemePreset.frostbound);

        verify(
          () => store.dispatch(
            const ThemePresetSelectedAction(DovahThemePreset.frostbound),
          ),
        ).called(1);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test(
      'Behavior equality holds for ViewModels built with the same activePreset',
      () {
        final AppearanceSectionViewModel first =
            AppearanceSectionViewModel.fromStore(store);
        final AppearanceSectionViewModel second =
            AppearanceSectionViewModel.fromStore(store);

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      },
    );

    test('Behavior equality fails when activePreset differs', () {
      final AppearanceSectionViewModel first =
          AppearanceSectionViewModel.fromStore(store);
      when(() => store.state).thenReturn(
        AppState.initial(
          appearance: const AppearanceState(
            activePreset: DovahThemePreset.hearth,
          ),
        ),
      );
      final AppearanceSectionViewModel second =
          AppearanceSectionViewModel.fromStore(store);

      expect(first, isNot(second));
    });
  });
}
