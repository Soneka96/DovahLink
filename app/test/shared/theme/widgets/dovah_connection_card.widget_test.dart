import 'dart:math' as math;
import 'dart:ui' show Tristate;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/frostbound_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_accent_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_tile.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
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

  group('DovahConnectionCard has no Material press overlay', () {
    testWidgets(
      'DovahConnectionCard keeps its surface free of splash effects',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Level 43 · Whiterun',
            state: DovahConnectionCardState.available,
            onTap: () {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expectNoMaterialOverlay(tester, mouseCursor: SystemMouseCursors.click);
      },
    );
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
      expectNoMaterialOverlay(tester, mouseCursor: SystemMouseCursors.basic);
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
      expect(find.byKey(DovahFocusRing.ringKey), findsOneWidget);

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
    });

    for (final (DovahThemePreset preset, double radius) in [
      (DovahThemePreset.frostbound, 0.0),
      (DovahThemePreset.dovah, 0.0),
      (DovahThemePreset.hearth, 12.0),
    ]) {
      testWidgets(
        'DovahConnectionCard outlines its focus ring with radius $radius under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahConnectionCard(
              title: 'Gaming PC',
              subtitle: 'Skyrim Special Edition',
              detail: 'Level 43 · Whiterun',
              state: DovahConnectionCardState.available,
              onTap: () {},
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );

          await tester.sendKeyEvent(LogicalKeyboardKey.tab);
          await tester.pump();

          final DovahFocusRingPainter painter =
              tester
                      .widget<CustomPaint>(find.byKey(DovahFocusRing.ringKey))
                      .foregroundPainter!
                  as DovahFocusRingPainter;
          expect(painter.cornerRadius, radius);
        },
      );
    }
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
        expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
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
              find.byWidgetPredicate(
                (Widget widget) =>
                    widget is DovahSurface &&
                    widget.role == DovahMaterialRole.surface,
              ),
            );
            final Finder tileFinder = find.byWidgetPredicate(
              (Widget widget) =>
                  widget is DovahSurface &&
                  widget.role == DovahMaterialRole.icon,
            );
            final DovahSurface tile = tester.widget(tileFinder);

            expect(tester.takeException(), isNull);
            expect(surface.padding, metrics.padding);
            expect(surface.cornerStyle, isNull);
            expect(surface.cornerCutSize, metrics.cornerCutSize);
            expect(surface.cornerRadius, metrics.cornerRadius);
            expect(tile.cornerStyle, isNull);
            expect(tile.cornerRadius, metrics.iconTileRadius);
            final DovahIconTile iconTile = tester.widget(
              find.byType(DovahIconTile),
            );
            expect(iconTile.size, metrics.iconTileSize);
            expect(iconTile.cornerRadius, metrics.iconTileRadius);
            expect(iconTile.rotation, metrics.iconTileRotation);
            expect(
              tester.getSize(tileFinder),
              Size.square(metrics.iconTileSize),
            );
            expect(
              tester.getSize(find.byType(DovahConnectionCard)).height,
              greaterThanOrEqualTo(metrics.minHeight),
            );
          },
        );
      }
    }

    for (final (DovahThemePreset preset, double rotation, Color glyphColor) in [
      (DovahThemePreset.frostbound, 0.0, const Color(0xFFA9C7D1)),
      (DovahThemePreset.dovah, math.pi / 4, const Color(0xFF8ED6FF)),
      (DovahThemePreset.hearth, 0.0, const Color(0xFF60462D)),
    ]) {
      testWidgets(
        'DovahConnectionCard turns its $preset icon tile by $rotation and colors the glyph',
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
            size: dovahTestSizes.last,
          );

          final DovahIconTile tile = tester.widget(find.byType(DovahIconTile));
          final Icon glyph = tester.widget(
            find.descendant(
              of: find.byType(DovahIconTile),
              matching: find.byType(Icon),
            ),
          );

          expect(tile.rotation, isA<double>());
          expect(tile.rotation, rotation);
          expect(glyph.color, glyphColor);
        },
      );
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

  group('DovahConnectionCard paints its icon tile with the icon material', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahConnectionCard paints its icon tile with the $preset icon material',
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
          final Finder tileFinder = find.byWidgetPredicate(
            (Widget widget) =>
                widget is DovahSurface && widget.role == DovahMaterialRole.icon,
          );
          final DovahMaterialPainter painter =
              tester
                      .widget<CustomPaint>(
                        find
                            .descendant(
                              of: tileFinder,
                              matching: find.byType(CustomPaint),
                            )
                            .first,
                      )
                      .painter!
                  as DovahMaterialPainter;
          final Icon icon = tester.widget(
            find.descendant(of: tileFinder, matching: find.byType(Icon)),
          );

          expect(
            painter.material,
            dovahThemeDataFor(preset).extension<DovahThemeMaterials>()!.icon,
          );
          expect(painter.cornerStyle, DovahPanelCornerStyle.rounded);
          expect(
            icon.color,
            dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!.iconTileForeground,
          );
        },
      );
    }
  });

  group('DovahConnectionCard follows the prototype hover treatment', () {
    /// The card's outer surface: the one that is not the icon tile.
    Finder outerSurface() => find.byWidgetPredicate(
      (Widget widget) =>
          widget is DovahSurface && widget.role != DovahMaterialRole.icon,
    );

    Future<TestGesture> pumpCard(
      WidgetTester tester, {
      required DovahThemePreset preset,
      VoidCallback? onTap,
      bool disableAnimations = false,
    }) async {
      setDovahTestWindow(tester, dovahTestSizes.last);
      await tester.pumpWidget(
        MaterialApp(
          theme: dovahThemeDataFor(preset),
          home: MediaQuery(
            data: MediaQueryData(disableAnimations: disableAnimations),
            child: Scaffold(
              body: Align(
                alignment: Alignment.topLeft,
                child: Padding(
                  padding: const EdgeInsets.all(40),
                  child: SizedBox(
                    width: 700,
                    child: DovahConnectionCard(
                      title: 'Gaming PC',
                      subtitle: 'Skyrim Special Edition',
                      detail: 'Level 43 · Whiterun',
                      state: DovahConnectionCardState.available,
                      onTap: onTap,
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      );
      final TestGesture pointer = await tester.createGesture(
        kind: PointerDeviceKind.mouse,
      );
      await pointer.addPointer(location: const Offset(1200, 700));
      await tester.pump();
      return pointer;
    }

    for (final (DovahThemePreset preset, Offset offset) in [
      (DovahThemePreset.frostbound, const Offset(2, 0)),
      (DovahThemePreset.dovah, const Offset(5, 0)),
      (DovahThemePreset.hearth, const Offset(0, -2)),
    ]) {
      testWidgets(
        'DovahConnectionCard rests on the surface material in its place under $preset',
        (WidgetTester tester) async {
          final TestGesture pointer = await pumpCard(
            tester,
            preset: preset,
            onTap: () {},
          );

          expect(
            tester.widget<DovahSurface>(outerSurface()).role,
            DovahMaterialRole.surface,
          );
          expect(tester.getTopLeft(outerSurface()), const Offset(40, 40));
          await pointer.removePointer();
        },
      );

      testWidgets(
        'DovahConnectionCard takes the raised material and slides by $offset when hovered under $preset',
        (WidgetTester tester) async {
          final TestGesture pointer = await pumpCard(
            tester,
            preset: preset,
            onTap: () {},
          );

          await pointer.moveTo(tester.getCenter(outerSurface()));
          await tester.pump();
          await tester.pump(DovahControlMetrics.liftDuration);

          final DovahMaterialPainter painter =
              tester
                      .widget<CustomPaint>(
                        find
                            .descendant(
                              of: outerSurface(),
                              matching: find.byType(CustomPaint),
                            )
                            .first,
                      )
                      .painter!
                  as DovahMaterialPainter;
          expect(
            tester.widget<DovahSurface>(outerSurface()).role,
            DovahMaterialRole.raised,
          );
          expect(
            painter.material,
            dovahThemeDataFor(preset).extension<DovahThemeMaterials>()!.raised,
          );
          expect(
            tester.getTopLeft(outerSurface()),
            const Offset(40, 40) + offset,
          );
          await pointer.removePointer();
        },
      );

      testWidgets(
        'DovahConnectionCard returns to rest when the pointer leaves under $preset',
        (WidgetTester tester) async {
          final TestGesture pointer = await pumpCard(
            tester,
            preset: preset,
            onTap: () {},
          );
          await pointer.moveTo(tester.getCenter(outerSurface()));
          await tester.pump(DovahControlMetrics.liftDuration);

          await pointer.moveTo(const Offset(1200, 700));
          await tester.pump();
          await tester.pump(DovahControlMetrics.liftDuration);

          expect(
            tester.widget<DovahSurface>(outerSurface()).role,
            DovahMaterialRole.surface,
          );
          expect(tester.getTopLeft(outerSurface()), const Offset(40, 40));
          await pointer.removePointer();
        },
      );
    }

    testWidgets(
      'DovahConnectionCard does not react to hover when not tappable',
      (WidgetTester tester) async {
        final TestGesture pointer = await pumpCard(
          tester,
          preset: DovahThemePreset.dovah,
        );

        await pointer.moveTo(tester.getCenter(outerSurface()));
        await tester.pump(DovahControlMetrics.liftDuration);

        expect(
          tester.widget<DovahSurface>(outerSurface()).role,
          DovahMaterialRole.surface,
        );
        expect(tester.getTopLeft(outerSurface()), const Offset(40, 40));
        await pointer.removePointer();
      },
    );

    testWidgets(
      'DovahConnectionCard applies its hover at once with reduced motion',
      (WidgetTester tester) async {
        final TestGesture pointer = await pumpCard(
          tester,
          preset: DovahThemePreset.dovah,
          onTap: () {},
          disableAnimations: true,
        );

        await pointer.moveTo(tester.getCenter(outerSurface()));
        await tester.pump();
        await tester.pump();

        expect(tester.getTopLeft(outerSurface()), const Offset(45, 40));
        await pointer.removePointer();
      },
    );
  });

  group('DovahConnectionCard paints the theme connection decoration', () {
    Future<DovahSurface> pumpSurface(
      WidgetTester tester, {
      required DovahThemePreset preset,
      Size size = const Size(1280, 720),
      DovahConnectionCardState state = DovahConnectionCardState.available,
    }) async {
      await pumpDovahThemedWidget(
        tester,
        DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: state,
          onTap: () {},
        ),
        preset: preset,
        size: size,
      );
      return tester.widget(
        find.byWidgetPredicate(
          (Widget widget) =>
              widget is DovahSurface && widget.role != DovahMaterialRole.icon,
        ),
      );
    }

    testWidgets(
      'DovahConnectionCard paints Frostbound decoration beneath and above its content',
      (WidgetTester tester) async {
        final DovahSurface surface = await pumpSurface(
          tester,
          preset: DovahThemePreset.frostbound,
        );
        final DovahConnectionAccentPainter under =
            surface.underlay! as DovahConnectionAccentPainter;
        final DovahConnectionAccentPainter over =
            surface.overlay! as DovahConnectionAccentPainter;

        expect(under.aboveContent, isFalse);
        expect(over.aboveContent, isTrue);
        expect(under.accent, frostboundMaterials.connectionAccent);
        expect(under.available, isTrue);
        expect(surface.borderColor, isNull);
      },
    );

    testWidgets(
      'DovahConnectionCard paints Dovah decoration only beneath its content',
      (WidgetTester tester) async {
        final DovahSurface surface = await pumpSurface(
          tester,
          preset: DovahThemePreset.dovah,
        );

        expect(surface.underlay, isA<DovahConnectionAccentPainter>());
        expect(surface.overlay, isNull);
        expect(
          (surface.underlay! as DovahConnectionAccentPainter).showLinkLine,
          isTrue,
        );
      },
    );

    testWidgets(
      'DovahConnectionCard hides the Dovah link line at a narrow window',
      (WidgetTester tester) async {
        final DovahSurface surface = await pumpSurface(
          tester,
          preset: DovahThemePreset.dovah,
          size: const Size(800, 560),
        );

        expect(
          (surface.underlay! as DovahConnectionAccentPainter).showLinkLine,
          isFalse,
        );
      },
    );

    testWidgets('DovahConnectionCard paints no drawn decoration for Hearth', (
      WidgetTester tester,
    ) async {
      final DovahSurface surface = await pumpSurface(
        tester,
        preset: DovahThemePreset.hearth,
      );

      expect(surface.underlay, isNull);
      expect(surface.overlay, isNull);
    });

    testWidgets('DovahConnectionCard pins the darker Hearth border at rest', (
      WidgetTester tester,
    ) async {
      final DovahSurface surface = await pumpSurface(
        tester,
        preset: DovahThemePreset.hearth,
      );

      expect(surface.borderColor, const Color(0xFF79542F));
    });

    testWidgets(
      'DovahConnectionCard keeps the raised border while Hearth is hovered',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Level 43 · Whiterun',
            state: DovahConnectionCardState.available,
            onTap: () {},
          ),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.last,
        );
        final TestGesture pointer = await tester.createGesture(
          kind: PointerDeviceKind.mouse,
        );
        await pointer.addPointer(location: const Offset(1200, 700));
        await tester.pump();
        await pointer.moveTo(
          tester.getCenter(find.byType(DovahConnectionCard)),
        );
        await tester.pump();
        await tester.pump(DovahControlMetrics.liftDuration);

        final DovahSurface surface = tester.widget(
          find.byWidgetPredicate(
            (Widget widget) =>
                widget is DovahSurface && widget.role != DovahMaterialRole.icon,
          ),
        );
        expect(surface.role, DovahMaterialRole.raised);
        expect(surface.borderColor, isNull);
        await pointer.removePointer();
      },
    );

    testWidgets(
      'DovahConnectionCard marks only an available card for the edge',
      (WidgetTester tester) async {
        final DovahSurface surface = await pumpSurface(
          tester,
          preset: DovahThemePreset.frostbound,
          state: DovahConnectionCardState.offline,
        );

        expect(
          (surface.underlay! as DovahConnectionAccentPainter).available,
          isFalse,
        );
      },
    );
  });
}
