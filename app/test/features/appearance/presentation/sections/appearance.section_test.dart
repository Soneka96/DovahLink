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
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';

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
        await tester.binding.setSurfaceSize(const Size(320, 2400));
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
          expect(cardRect.bottom, lessThanOrEqualTo(2400));
        }
      },
    );
  });

  group('AppearanceSection stays usable inside DovahDialog', () {
    for (final Size size in const [
      Size(720, 480),
      Size(900, 560),
      Size(1280, 720),
      Size(1600, 900),
    ]) {
      testWidgets(
        'AppearanceSection shows every preset without overflow and selects one inside DovahDialog at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          await tester.pumpWidget(
            StoreProvider<AppState>(
              store: store,
              child: MaterialApp(
                theme: dovahThemeDataFor(DovahThemePreset.dovah),
                home: Builder(
                  builder: (BuildContext context) => TextButton(
                    onPressed: () => DovahDialog.show<void>(
                      context,
                      title: 'Appearance',
                      child: const AppearanceSection(),
                    ),
                    child: const Text('Open'),
                  ),
                ),
              ),
            ),
          );

          await tester.tap(find.text('Open'));
          await tester.pumpAndSettle();

          expect(tester.takeException(), isNull);
          expect(find.text('Appearance'), findsOneWidget);
          expect(
            tester.getSize(find.byType(DovahDialog)).height,
            lessThanOrEqualTo(size.height * 0.92),
          );

          await tester.ensureVisible(find.text(DovahThemePreset.hearth.label));
          await tester.tap(find.text(DovahThemePreset.hearth.label));

          expect(selectedPresets, [DovahThemePreset.hearth]);
        },
      );
    }
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

  group('AppearanceSection lays out the prototype grid', () {
    Rect cardRect(WidgetTester tester, DovahThemePreset preset) => tester
        .getRect(find.byKey(Key('appearance-preset-card-${preset.name}')));

    for (final (Size size, double gap) in [
      (const Size(1280, 720), 11.0),
      (const Size(900, 621), 11.0),
      (const Size(900, 620), 8.0),
      (const Size(720, 480), 8.0),
    ]) {
      testWidgets(
        'AppearanceSection puts the three cards in one row $gap apart at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          await tester.pumpWidget(buildWidget());
          final Rect frostbound = cardRect(tester, DovahThemePreset.frostbound);
          final Rect dovah = cardRect(tester, DovahThemePreset.dovah);
          final Rect hearth = cardRect(tester, DovahThemePreset.hearth);

          expect(frostbound.top, dovah.top);
          expect(dovah.top, hearth.top);
          expect(dovah.left - frostbound.right, closeTo(gap, 0.01));
          expect(hearth.left - dovah.right, closeTo(gap, 0.01));
          expect(frostbound.width, closeTo(dovah.width, 0.01));
          expect(dovah.width, closeTo(hearth.width, 0.01));
        },
      );
    }

    testWidgets('AppearanceSection gives every card in a row the same height', (
      WidgetTester tester,
    ) async {
      tester.view.physicalSize =
          const Size(700, 900) * tester.view.devicePixelRatio;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(buildWidget());

      expect(
        cardRect(tester, DovahThemePreset.frostbound).height,
        cardRect(tester, DovahThemePreset.hearth).height,
      );
      expect(
        cardRect(tester, DovahThemePreset.dovah).height,
        cardRect(tester, DovahThemePreset.hearth).height,
      );
    });

    testWidgets('AppearanceSection sizes its introduction like the prototype', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());
      final Text title = tester.widget(
        find.text('Choose your Skyrim atmosphere'),
      );
      final Text body = tester.widget(
        find.text(
          'The interface stays familiar, but its material, shape, density and motion change.',
        ),
      );

      expect(title.style?.fontSize, 16);
      expect(title.style?.fontWeight, FontWeight.w700);
      expect(body.style?.fontSize, 13);
      expect(body.style?.height, 1.4);
    });

    for (final (Size size, double gap) in [
      (const Size(1280, 720), 16.0),
      (const Size(1280, 560), 10.0),
    ]) {
      testWidgets(
        'AppearanceSection leaves $gap between its introduction and the cards at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          await tester.pumpWidget(buildWidget());

          expect(
            cardRect(tester, DovahThemePreset.frostbound).top -
                tester
                    .getRect(
                      find.text(
                        'The interface stays familiar, but its material, shape, density and motion change.',
                      ),
                    )
                    .bottom,
            closeTo(gap, 0.5),
          );
        },
      );
    }

    testWidgets(
      'AppearanceSection drops to two columns and a wrapped row when narrow',
      (WidgetTester tester) async {
        tester.view.physicalSize =
            const Size(420, 1600) * tester.view.devicePixelRatio;
        addTearDown(tester.view.reset);
        await tester.pumpWidget(buildWidget());

        final Rect frostbound = cardRect(tester, DovahThemePreset.frostbound);
        final Rect dovah = cardRect(tester, DovahThemePreset.dovah);
        final Rect hearth = cardRect(tester, DovahThemePreset.hearth);
        expect(frostbound.top, dovah.top);
        expect(hearth.top, greaterThan(frostbound.bottom));
        expect(hearth.left, frostbound.left);
        expect(hearth.width, closeTo(frostbound.width, 0.01));
      },
    );

    testWidgets(
      'AppearanceSection stacks the cards in one column when very narrow',
      (WidgetTester tester) async {
        tester.view.physicalSize =
            const Size(320, 2400) * tester.view.devicePixelRatio;
        addTearDown(tester.view.reset);
        await tester.pumpWidget(buildWidget());

        final Rect frostbound = cardRect(tester, DovahThemePreset.frostbound);
        final Rect dovah = cardRect(tester, DovahThemePreset.dovah);
        expect(dovah.top, greaterThan(frostbound.bottom));
        expect(dovah.left, frostbound.left);
      },
    );
  });
}
