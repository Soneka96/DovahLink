import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';

import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahConnectionCard] across every theme, supplied visual state, and interaction.
void main() {
  group('DovahConnectionCard renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final DovahConnectionCardState state
          in DovahConnectionCardState.values) {
        for (final Size size in dovahTestSizes) {
          testWidgets(
            'DovahConnectionCard renders $state under $preset at $size without overflow',
            (WidgetTester tester) async {
              await pumpDovahThemedWidget(
                tester,
                DovahConnectionCard(
                  title: 'Gaming PC',
                  subtitle: 'Skyrim Special Edition',
                  detail: 'Level 43 · Whiterun',
                  state: state,
                ),
                preset: preset,
                size: size,
              );

              final bool uppercase = preset == DovahThemePreset.frostbound;
              expect(tester.takeException(), isNull);
              expect(
                find.text(uppercase ? 'GAMING PC' : 'Gaming PC'),
                findsOneWidget,
              );
              expect(
                find.text(uppercase ? state.label.toUpperCase() : state.label),
                findsOneWidget,
              );
            },
          );
        }
      }
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahConnectionCard keeps its minimum height and unabridged label under $preset',
        (WidgetTester tester) async {
          final SemanticsHandle semantics = tester.ensureSemantics();
          try {
            await pumpDovahThemedWidget(
              tester,
              const DovahConnectionCard(
                title: 'Gaming PC',
                subtitle: 'Skyrim Special Edition',
                detail: 'Level 43 · Whiterun',
                state: DovahConnectionCardState.available,
              ),
              preset: preset,
              size: dovahTestSizes.first,
            );
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;

            expect(
              tester.getSize(find.byType(DovahConnectionCard)).height,
              greaterThanOrEqualTo(tokens.connectionCardMinHeight),
            );
            expect(
              find.bySemanticsLabel(
                'Gaming PC, Skyrim Special Edition, Level 43 · Whiterun, '
                'Connected',
              ),
              findsOneWidget,
            );
          } finally {
            semantics.dispose();
          }
        },
      );
    }

    testWidgets(
      'DovahConnectionCard sizes its lines with the shared card typography',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Level 43 · Whiterun',
            state: DovahConnectionCardState.available,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );
        final Text title = tester.widget(find.text('Gaming PC'));
        final Text subtitle = tester.widget(
          find.text('Skyrim Special Edition'),
        );
        final Text detail = tester.widget(find.text('Level 43 · Whiterun'));
        final Text state = tester.widget(find.text('Connected'));

        expect(title.style?.fontSize, isA<double>());
        expect(title.style?.fontSize, DovahThemeTokens.connectionTitleFontSize);
        expect(title.style?.fontWeight, FontWeight.w700);
        for (final Text line in [title, subtitle, detail, state]) {
          expect(line.style?.height, isA<double>());
          expect(line.style?.height, DovahThemeTokens.bodyLineHeight);
        }
      },
    );

    testWidgets('DovahConnectionCard leaves casing alone outside Frostbound', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
        ),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );
      final Text title = tester.widget(find.text('Gaming PC'));
      final Text state = tester.widget(find.text('Connected'));

      expect(title.style?.letterSpacing, isNull);
      expect(state.style?.letterSpacing, isNull);
    });

    testWidgets(
      'DovahConnectionCard spaces uppercase labels under Frostbound',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Level 43 · Whiterun',
            state: DovahConnectionCardState.available,
          ),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );
        final Text title = tester.widget(find.text('GAMING PC'));
        final Text state = tester.widget(find.text('CONNECTED'));
        final Text subtitle = tester.widget(
          find.text('Skyrim Special Edition'),
        );
        const double spacing =
            DovahThemeTokens.uppercaseLetterSpacingEm *
            DovahThemeTokens.compactFontSize;

        expect(title.style?.letterSpacing, isA<double>());
        expect(title.style?.letterSpacing, spacing);
        expect(state.style?.letterSpacing, isA<double>());
        expect(state.style?.letterSpacing, spacing);
        expect(subtitle.style?.letterSpacing, isNull);
      },
    );

    testWidgets(
      'DovahConnectionCard shows a muted marker and chevron when its state is unknown',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahConnectionCard(
            title: 'Local Host',
            subtitle: 'DovahLink Host',
            detail: '127.0.0.1:47800',
            state: DovahConnectionCardState.unknown,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );
        final DovahThemeTokens tokens = dovahThemeDataFor(
          DovahThemePreset.dovah,
        ).extension<DovahThemeTokens>()!;
        final Icon marker = tester.widget(find.byIcon(Icons.circle).first);
        final Text label = tester.widget(find.text('Not connected'));

        expect(marker.color, tokens.textMuted);
        expect(label.style?.color, tokens.textMuted);
        expect(find.byIcon(Icons.chevron_right), findsOneWidget);
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahConnectionCard uses shared icon and caption metrics under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahConnectionCard(
              title: 'Gaming PC',
              subtitle: 'Skyrim Special Edition',
              detail: 'Level 43 · Whiterun',
              state: DovahConnectionCardState.available,
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahThemeTokens tokens = Theme.of(
            tester.element(find.byType(DovahConnectionCard)),
          ).extension<DovahThemeTokens>()!;
          final Icon icon = tester.widget(
            find.byIcon(Icons.desktop_windows_outlined),
          );
          final Text subtitle = tester.widget(
            find.text('Skyrim Special Edition'),
          );
          final Icon marker = tester.widget(find.byIcon(Icons.circle).first);

          expect(
            icon.size,
            DovahThemeTokens.connectionIconSize * tokens.densityScale,
          );
          expect(subtitle.style?.fontSize, DovahThemeTokens.compactFontSize);
          expect(marker.size, DovahThemeTokens.connectionStateMarkerSize);
        },
      );
    }

    testWidgets('DovahConnectionCard contains a chevron when available', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byIcon(Icons.chevron_right), findsOneWidget);
    });

    testWidgets('DovahConnectionCard contains a chevron when it needs repair', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Laptop',
          subtitle: 'Skyrim Special Edition',
          detail: 'Trust changed in Skyrim',
          state: DovahConnectionCardState.repair,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byIcon(Icons.chevron_right), findsOneWidget);
    });

    testWidgets('DovahConnectionCard does not contain a chevron when offline', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Living Room PC',
          subtitle: 'Skyrim Anniversary Edition',
          detail: 'Last connected yesterday',
          state: DovahConnectionCardState.offline,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byIcon(Icons.chevron_right), findsNothing);
    });
  });

  group('DovahConnectionCard calls onTap', () {
    testWidgets('DovahConnectionCard calls onTap when tapped', (
      WidgetTester tester,
    ) async {
      int tapCount = 0;

      await pumpDovahThemedWidget(
        tester,
        DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
          onTap: () => tapCount++,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text('Gaming PC'));
      await tester.pump();

      expect(tapCount, 1);
    });

    testWidgets('DovahConnectionCard does not call onTap when it has none', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text('Gaming PC'), warnIfMissed: false);
      await tester.pump();

      expect(tester.takeException(), isNull);
    });
  });

  group('DovahConnectionCard supports keyboard activation', () {
    for (final LogicalKeyboardKey key in <LogicalKeyboardKey>[
      LogicalKeyboardKey.enter,
      LogicalKeyboardKey.space,
    ]) {
      testWidgets('DovahConnectionCard activates on $key when focused', (
        WidgetTester tester,
      ) async {
        int activationCount = 0;
        await pumpDovahThemedWidget(
          tester,
          DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Level 43 · Whiterun',
            state: DovahConnectionCardState.available,
            onTap: () => activationCount++,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.sendKeyEvent(key);
        await tester.pump();

        expect(activationCount, 1);
      });
    }

    testWidgets('DovahConnectionCard displays its focus outline after Tab', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        Column(
          children: [
            DovahConnectionCard(
              title: 'Gaming PC',
              subtitle: 'Skyrim Special Edition',
              detail: 'Level 43 · Whiterun',
              state: DovahConnectionCardState.available,
              onTap: () {},
            ),
            TextButton(onPressed: () {}, child: const Text('Next')),
          ],
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(
        find.byKey(const Key('dovah-connection-card-focus-outline')),
        findsOneWidget,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(
        find.byKey(const Key('dovah-connection-card-focus-outline')),
        findsNothing,
      );
    });
  });

  group('DovahConnectionCard exposes button semantics', () {
    testWidgets('DovahConnectionCard exposes enabled state when tappable', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        int tapCount = 0;
        await pumpDovahThemedWidget(
          tester,
          DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Level 43 · Whiterun',
            state: DovahConnectionCardState.available,
            onTap: () => tapCount++,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final String label =
            'Gaming PC, Skyrim Special Edition, Level 43 · Whiterun, '
            '${DovahConnectionCardState.available.label}';
        final SemanticsNode node = tester.getSemantics(
          find.bySemanticsLabel(label),
        );
        final SemanticsData data = node.getSemanticsData();
        expect(data.label, label);
        expect(data.flagsCollection.isButton, isTrue);
        expect(data.flagsCollection.isEnabled, Tristate.isTrue);
        expect(data.hasAction(SemanticsAction.tap), isTrue);
        tester.semantics.performAction(
          find.semantics.byLabel(label),
          SemanticsAction.tap,
        );
        expect(tapCount, 1);
      } finally {
        semantics.dispose();
      }
    });

    testWidgets('DovahConnectionCard exposes its label and enabled state', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        await pumpDovahThemedWidget(
          tester,
          const DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Level 43 · Whiterun',
            state: DovahConnectionCardState.available,
            onTap: null,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final String label =
            'Gaming PC, Skyrim Special Edition, Level 43 · Whiterun, '
            '${DovahConnectionCardState.available.label}';
        final SemanticsNode node = tester.getSemantics(
          find.bySemanticsLabel(label),
        );
        final SemanticsData data = node.getSemanticsData();
        expect(data.label, label);
        expect(data.flagsCollection.isButton, isTrue);
        expect(data.flagsCollection.isEnabled, Tristate.isFalse);
        expect(data.hasAction(SemanticsAction.tap), isFalse);

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.sendKeyEvent(LogicalKeyboardKey.enter);
        await tester.sendKeyEvent(LogicalKeyboardKey.space);
        expect(
          find.byKey(const Key('dovah-connection-card-focus-outline')),
          findsNothing,
        );
      } finally {
        semantics.dispose();
      }
    });
  });
}
