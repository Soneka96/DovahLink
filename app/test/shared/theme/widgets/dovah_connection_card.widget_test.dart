import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
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
            expect(
              tester.getSize(find.byType(DovahConnectionCard)).height,
              greaterThanOrEqualTo(
                DovahConnectionCardMetrics.forWindow(
                  themeMetrics: dovahThemeDataFor(
                    preset,
                  ).extension<DovahConnectionCardThemeMetrics>()!,
                  window: dovahTestSizes.first,
                ).minHeight,
              ),
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
          size: dovahTestSizes.last,
        );
        final Text title = tester.widget(find.text('Gaming PC'));
        final Text subtitle = tester.widget(
          find.text('Skyrim Special Edition'),
        );
        final Text detail = tester.widget(find.text('Level 43 · Whiterun'));
        final Text state = tester.widget(find.text('Connected'));

        expect(title.style?.fontSize, isA<double>());
        expect(title.style?.fontSize, DovahConnectionCardMetrics.titleFontSize);
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

    testWidgets('DovahConnectionCard maps each state to its theme color', (
      WidgetTester tester,
    ) async {
      final DovahThemeTokens tokens = dovahThemeDataFor(
        DovahThemePreset.dovah,
      ).extension<DovahThemeTokens>()!;
      const Map<DovahConnectionCardState, String> labels = {
        DovahConnectionCardState.available: 'Connected',
        DovahConnectionCardState.unknown: 'Not connected',
        DovahConnectionCardState.offline: 'Offline',
        DovahConnectionCardState.repair: 'Pair again',
      };
      final Map<DovahConnectionCardState, Color> colors = {
        DovahConnectionCardState.available: tokens.success,
        DovahConnectionCardState.unknown: tokens.textMuted,
        DovahConnectionCardState.offline: tokens.statusOffline,
        DovahConnectionCardState.repair: tokens.warning,
      };

      for (final DovahConnectionCardState state
          in DovahConnectionCardState.values) {
        await pumpDovahThemedWidget(
          tester,
          DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'DovahLink Host',
            detail: '127.0.0.1:58231',
            state: state,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final Icon marker = tester.widget(find.byIcon(Icons.circle).first);
        final Text label = tester.widget(find.text(labels[state]!));

        expect(marker.color, colors[state]);
        expect(label.style?.color, colors[state]);
      }
    });

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
          final Icon icon = tester.widget(
            find.byIcon(Icons.desktop_windows_outlined),
          );
          final Text subtitle = tester.widget(
            find.text('Skyrim Special Edition'),
          );
          final Icon marker = tester.widget(find.byIcon(Icons.circle).first);

          expect(icon.size, DovahConnectionCardMetrics.iconSize);
          expect(subtitle.style?.fontSize, DovahThemeTokens.compactFontSize);
          expect(marker.size, DovahConnectionCardMetrics.stateMarkerSize);
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

  group('DovahConnectionCard follows the prototype responsive geometry', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'DovahConnectionCard resolves its padding, bevel, and icon tile for $preset at $size',
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
              size: size,
            );
            final DovahConnectionCardMetrics metrics =
                DovahConnectionCardMetrics.forWindow(
                  themeMetrics: dovahThemeDataFor(
                    preset,
                  ).extension<DovahConnectionCardThemeMetrics>()!,
                  window: size,
                );
            final DovahSurface surface = tester.widget(
              find.byType(DovahSurface),
            );
            final Container tile = tester.widget<Container>(
              find.byWidgetPredicate(
                (Widget widget) =>
                    widget is Container &&
                    widget.constraints?.maxWidth == metrics.iconTileSize &&
                    widget.decoration is BoxDecoration,
              ),
            );
            final BoxDecoration tileDecoration =
                tile.decoration! as BoxDecoration;

            expect(tester.takeException(), isNull);
            expect(surface.padding, metrics.padding);
            expect(surface.cornerCutSize, metrics.cornerCutSize);
            expect(surface.cornerRadius, metrics.cornerRadius);
            expect(
              tileDecoration.borderRadius,
              BorderRadius.circular(metrics.iconTileRadius),
            );
            expect(
              tester.getSize(find.byType(DovahConnectionCard)).height,
              greaterThanOrEqualTo(metrics.minHeight),
            );
          },
        );
      }
    }

    for (final (Size size, bool shown) in [
      (const Size(720, 480), false),
      (const Size(900, 560), false),
      (const Size(1280, 720), true),
      (const Size(1600, 900), true),
    ]) {
      testWidgets(
        'DovahConnectionCard ${shown ? 'shows' : 'hides'} its detail column at $size',
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
              preset: DovahThemePreset.dovah,
              size: size,
            );

            expect(
              find.text('Level 43 · Whiterun'),
              shown ? findsOneWidget : findsNothing,
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
      'DovahConnectionCard gives the status label its 112 minimum width and no arrow when offline',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahConnectionCard(
            title: 'Living Room PC',
            subtitle: 'Skyrim Anniversary Edition',
            detail: 'Last connected yesterday',
            state: DovahConnectionCardState.offline,
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(find.byIcon(Icons.chevron_right), findsNothing);
        expect(
          tester
              .getSize(
                find
                    .ancestor(
                      of: find.text('Offline'),
                      matching: find.byType(ConstrainedBox),
                    )
                    .first,
              )
              .width,
          greaterThanOrEqualTo(DovahConnectionCardMetrics.statusMinWidth),
        );
      },
    );
  });
}
