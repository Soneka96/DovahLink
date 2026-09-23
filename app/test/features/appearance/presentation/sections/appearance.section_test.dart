import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/appearance.injection_container.dart';
import 'package:dovahlink_client/features/appearance/presentation/sections/appearance.section.dart';
import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises [AppearanceSection] rendering and selection. Built through the real DI graph and a
/// real Redux store, without middleware: this section's own reducer path is already synchronous
/// and side-effect-free, and [AppearanceMiddleware]'s persistence side effect has its own
/// dedicated test file, so a real store here proves the same thing a mocked ViewModel would
/// without the extra setup.
void main() {
  setUp(() async {
    await sl.reset();
    initAppearanceDependencies();
  });

  group('AppearanceSection contains widgets', () {
    testWidgets('AppearanceSection contains one card per DovahThemePreset', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: dovahThemeDataFor(DovahThemePreset.dovah),
          home: StoreProvider<AppState>(
            store: const CreateStore()(),
            child: const Material(child: AppearanceSection()),
          ),
        ),
      );

      expect(find.byType(AppearancePresetCard), findsNWidgets(3));
      for (final DovahThemePreset preset in DovahThemePreset.values) {
        expect(find.text(preset.label), findsOneWidget);
      }
    });

    testWidgets('AppearanceSection marks only the active preset as selected', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: dovahThemeDataFor(DovahThemePreset.dovah),
          home: StoreProvider<AppState>(
            store: const CreateStore()(),
            child: const Material(child: AppearanceSection()),
          ),
        ),
      );

      final AppearancePresetCard dovahCard = tester.widget(
        find.byKey(const Key('appearance-preset-card-dovah')),
      );
      final AppearancePresetCard frostboundCard = tester.widget(
        find.byKey(const Key('appearance-preset-card-frostbound')),
      );

      expect(dovahCard.selected, isTrue);
      expect(frostboundCard.selected, isFalse);
    });
  });

  group('AppearanceSection selection', () {
    testWidgets(
      'AppearanceSection tapping a card updates the store\'s active preset',
      (WidgetTester tester) async {
        final Store<AppState> store = const CreateStore()();
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: StoreProvider<AppState>(
              store: store,
              child: const Material(child: AppearanceSection()),
            ),
          ),
        );

        await tester.tap(find.text(DovahThemePreset.hearth.label));
        await tester.pump();

        expect(store.state.appearance.activePreset, DovahThemePreset.hearth);
      },
    );
  });
}
