import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/sections/appearance.section.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Mock ViewModel supplied to [AppearanceSection].
class MockAppearanceSectionViewModel extends Mock
    implements AppearanceSectionViewModel {}

/// Mock Store supplied to the section's [StoreConnector].
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [AppearanceSection] rendering and selection through its ViewModel contract.
void main() {
  late MockStore store;
  late MockAppearanceSectionViewModel viewModel;
  late List<DovahThemePreset> selectedPresets;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    viewModel = MockAppearanceSectionViewModel();
    selectedPresets = [];

    when(() => store.state).thenReturn(AppState.initial());
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => viewModel.activePreset).thenReturn(DovahThemePreset.dovah);
    when(
      () => viewModel.onSelectPreset,
    ).thenReturn((DovahThemePreset preset) => selectedPresets.add(preset));
    sl.registerFactoryParam<AppearanceSectionViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => viewModel,
    );
  });

  tearDown(() async {
    await sl.reset();
    reset(viewModel);
    reset(store);
  });

  /// Builds the section using the mocked Store and ViewModel.
  Widget buildWidget({TextScaler? textScaler}) => MaterialApp(
    theme: dovahThemeDataFor(DovahThemePreset.dovah),
    home: Builder(
      builder: (BuildContext context) {
        final MediaQueryData mediaQuery = MediaQuery.of(context);
        return StoreProvider<AppState>(
          store: store,
          child: MediaQuery(
            data: mediaQuery.copyWith(
              textScaler: textScaler ?? mediaQuery.textScaler,
            ),
            child: const Material(child: AppearanceSection()),
          ),
        );
      },
    ),
  );

  group('AppearanceSection contains widgets', () {
    testWidgets('AppearanceSection contains one card per DovahThemePreset', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(find.byType(AppearancePresetCard), findsNWidgets(3));
      for (final DovahThemePreset preset in DovahThemePreset.values) {
        expect(find.text(preset.label), findsOneWidget);
      }
      final Text description = tester.widget(
        find.text(
          'The interface stays familiar, but its material, shape, density and motion change.',
        ),
      );
      expect(description.style?.fontSize, DovahThemeTokens.compactFontSize);
    });

    testWidgets('AppearanceSection marks only the active preset as selected', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      final AppearancePresetCard dovahCard = tester.widget(
        find.byKey(const Key('appearance-preset-card-dovah')),
      );
      final AppearancePresetCard frostboundCard = tester.widget(
        find.byKey(const Key('appearance-preset-card-frostbound')),
      );

      expect(dovahCard.selected, isTrue);
      expect(frostboundCard.selected, isFalse);
    });

    testWidgets(
      'AppearanceSection lays out preset cards at large text scale on a narrow surface',
      (WidgetTester tester) async {
        await tester.binding.setSurfaceSize(const Size(320, 900));
        addTearDown(() => tester.binding.setSurfaceSize(null));
        await tester.pumpWidget(
          buildWidget(textScaler: const TextScaler.linear(2)),
        );

        expect(tester.takeException(), isNull);
        expect(find.byType(AppearancePresetCard), findsNWidgets(3));
        for (final DovahThemePreset preset in DovahThemePreset.values) {
          expect(find.text(preset.label), findsOneWidget);
          final Rect cardRect = tester.getRect(
            find.byKey(Key('appearance-preset-card-${preset.name}')),
          );
          expect(cardRect.left, greaterThanOrEqualTo(0));
          expect(cardRect.top, greaterThanOrEqualTo(0));
          expect(cardRect.right, lessThanOrEqualTo(320));
          expect(cardRect.bottom, lessThanOrEqualTo(900));
        }
      },
    );
  });

  group('AppearanceSection calls the selection callback', () {
    testWidgets(
      'AppearanceSection calls onSelectPreset with the tapped preset',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildWidget());

        await tester.tap(find.text(DovahThemePreset.hearth.label));

        expect(selectedPresets, [DovahThemePreset.hearth]);
      },
    );
  });
}
