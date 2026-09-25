import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_preset_theme.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/frostbound_theme.dart';
import 'package:dovahlink_client/shared/theme/hearth_theme.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';

/// Exercises the three DovahLink theme-preset builders and the [dovahThemeDataFor] mapping.
void main() {
  group('Method buildFrostboundTheme behaves correctly', () {
    test(
      'Method buildFrostboundTheme attaches DovahThemeTokens with singleBevel geometry',
      () {
        final ThemeData theme = buildFrostboundTheme();
        final DovahThemeTokens? tokens = theme.extension<DovahThemeTokens>();

        expect(tokens, isA<DovahThemeTokens>());
        expect(tokens!.cornerStyle, DovahPanelCornerStyle.singleBevel);
        expect(tokens.primaryActionForeground, const Color(0xFFE9F0F2));
        expect(theme.brightness, Brightness.dark);
      },
    );

    test('Method buildFrostboundTheme attaches its DovahRootThemeMetrics', () {
      expect(
        buildFrostboundTheme().extension<DovahRootThemeMetrics>(),
        DovahRootThemeMetrics.frostbound,
      );
    });

    test(
      'Method buildFrostboundTheme attaches its DovahConnectionCardThemeMetrics',
      () {
        expect(
          buildFrostboundTheme().extension<DovahConnectionCardThemeMetrics>(),
          DovahConnectionCardThemeMetrics.frostbound,
        );
      },
    );

    test('Method buildFrostboundTheme attaches its DovahPageThemeMetrics', () {
      expect(
        buildFrostboundTheme().extension<DovahPageThemeMetrics>(),
        DovahPageThemeMetrics.frostbound,
      );
    });

    test(
      'Method buildFrostboundTheme attaches its DovahSessionThemeMetrics',
      () {
        expect(
          buildFrostboundTheme().extension<DovahSessionThemeMetrics>(),
          DovahSessionThemeMetrics.frostbound,
        );
      },
    );

    test(
      'Method buildFrostboundTheme attaches its DovahOverviewThemeMetrics',
      () {
        expect(
          buildFrostboundTheme().extension<DovahOverviewThemeMetrics>(),
          DovahOverviewThemeMetrics.frostbound,
        );
      },
    );

    test('Method buildFrostboundTheme attaches its DovahThemeMaterials', () {
      expect(
        buildFrostboundTheme().extension<DovahThemeMaterials>(),
        DovahThemeMaterials.frostbound,
      );
    });
  });

  group('Behavior prototype color token mappings behave correctly', () {
    test('Behavior prototype color token mappings match Frostbound values', () {
      final DovahThemeTokens tokens = buildFrostboundTheme()
          .extension<DovahThemeTokens>()!;

      expect(
        <Color>[
          tokens.background,
          tokens.surface,
          tokens.surfaceRaised,
          tokens.surface3,
          tokens.lineSubtle,
          tokens.lineStrong,
          tokens.textPrimary,
          tokens.textMuted,
          tokens.textFaint,
          tokens.accentPrimary,
          tokens.accentSecondary,
          tokens.signal,
          tokens.ember,
          tokens.success,
          tokens.warning,
          tokens.danger,
          tokens.soft,
          tokens.health,
          tokens.magicka,
          tokens.stamina,
        ],
        <Color>[
          const Color(0xFF020405),
          const Color(0xFF070B0D),
          const Color(0xFF0B1115),
          const Color(0xFF11191E),
          const Color(0xFF344048),
          const Color(0xFF71808A),
          const Color(0xFFEDF1F2),
          const Color(0xFFB0B9BD),
          const Color(0xFF879399),
          const Color(0xFFA9C7D1),
          const Color(0xFF7FA5B3),
          const Color(0xFFA9C7D1),
          const Color(0xFFA43B40),
          const Color(0xFF9AC9DC),
          const Color(0xFFC0575B),
          const Color(0xFFD36A6E),
          const Color(0x1F9AC9DC),
          const Color(0xFFB65256),
          const Color(0xFF83B9D1),
          const Color(0xFF789779),
        ],
      );
    });

    test('Behavior prototype color token mappings match Dovah values', () {
      final DovahThemeTokens tokens = buildDovahPresetTheme()
          .extension<DovahThemeTokens>()!;

      expect(
        <Color>[
          tokens.background,
          tokens.surface,
          tokens.surfaceRaised,
          tokens.surface3,
          tokens.lineSubtle,
          tokens.lineStrong,
          tokens.textPrimary,
          tokens.textMuted,
          tokens.textFaint,
          tokens.accentPrimary,
          tokens.accentSecondary,
          tokens.signal,
          tokens.ember,
          tokens.success,
          tokens.warning,
          tokens.danger,
          tokens.soft,
          tokens.health,
          tokens.magicka,
          tokens.stamina,
        ],
        <Color>[
          const Color(0xFF05090E),
          const Color(0xFF0B141D),
          const Color(0xFF101D28),
          const Color(0xFF162735),
          const Color(0xFF294052),
          const Color(0xFF4A6B84),
          const Color(0xFFF1F6F9),
          const Color(0xFF9AABB7),
          const Color(0xFF667C8B),
          const Color(0xFF8ED6FF),
          const Color(0xFF54AEE0),
          const Color(0xFF74BDE8),
          const Color(0xFFE2A55E),
          const Color(0xFF8ED6FF),
          const Color(0xFFE2A55E),
          const Color(0xFFE18080),
          const Color(0x2174BDE8),
          const Color(0xFFD16F62),
          const Color(0xFF65B8E7),
          const Color(0xFF78A984),
        ],
      );
    });

    test('Behavior prototype color token mappings match Hearth values', () {
      final DovahThemeTokens tokens = buildHearthTheme()
          .extension<DovahThemeTokens>()!;

      expect(
        <Color>[
          tokens.background,
          tokens.surface,
          tokens.surfaceRaised,
          tokens.surface3,
          tokens.lineSubtle,
          tokens.lineStrong,
          tokens.textPrimary,
          tokens.textMuted,
          tokens.textFaint,
          tokens.accentPrimary,
          tokens.accentSecondary,
          tokens.signal,
          tokens.ember,
          tokens.success,
          tokens.warning,
          tokens.danger,
          tokens.soft,
          tokens.health,
          tokens.magicka,
          tokens.stamina,
        ],
        <Color>[
          const Color(0xFFD8C09A),
          const Color(0xFFEEDBBB),
          const Color(0xFFDFC399),
          const Color(0xFFCFAA76),
          const Color(0xFF9B7344),
          const Color(0xFF79542F),
          const Color(0xFF271B12),
          const Color(0xFF594431),
          const Color(0xFF765B3E),
          const Color(0xFF965923),
          const Color(0xFFB87230),
          const Color(0xFFA96328),
          const Color(0xFFA96328),
          const Color(0xFF35684C),
          const Color(0xFF99541F),
          const Color(0xFF913B34),
          const Color(0x24965923),
          const Color(0xFFA74F3E),
          const Color(0xFF557B98),
          const Color(0xFF58785B),
        ],
      );
    });
  });

  group('Behavior prototype root-screen token mappings behave correctly', () {
    test('Behavior prototype root-screen tokens match Frostbound values', () {
      final DovahThemeTokens tokens = buildFrostboundTheme()
          .extension<DovahThemeTokens>()!;

      expect(tokens.eyebrow, const Color(0xFFBD5559));
      expect(tokens.pageTitleLineHeight, 1.0);
      expect(tokens.uppercaseLabels, isTrue);
      expect(tokens.rootHeaderRuleFraction, 0.2);
    });

    test('Behavior prototype root-screen tokens match Dovah values', () {
      final DovahThemeTokens tokens = buildDovahPresetTheme()
          .extension<DovahThemeTokens>()!;

      expect(tokens.eyebrow, const Color(0xFFE2A55E));
      expect(tokens.pageTitleLineHeight, 1.14);
      expect(tokens.uppercaseLabels, isFalse);
      expect(tokens.rootHeaderRuleFraction, 0.36);
    });

    test('Behavior prototype root-screen tokens match Hearth values', () {
      final DovahThemeTokens tokens = buildHearthTheme()
          .extension<DovahThemeTokens>()!;

      expect(tokens.eyebrow, const Color(0xFF945720));
      expect(tokens.pageTitleLineHeight, 1.14);
      expect(tokens.uppercaseLabels, isFalse);
      expect(tokens.rootHeaderRuleFraction, 0.52);
    });
  });

  group('Behavior prototype identity token mappings behave correctly', () {
    for (final (
          DovahThemePreset preset,
          double panelRadius,
          double primaryRadius,
        )
        in [
          (DovahThemePreset.frostbound, 0.0, 0.0),
          (DovahThemePreset.dovah, 0.0, 0.0),
          (DovahThemePreset.hearth, 14.0, 9.0),
        ]) {
      test('Behavior ${preset.name} tokens match the prototype radii', () {
        final DovahThemeTokens tokens = dovahThemeDataFor(
          preset,
        ).extension<DovahThemeTokens>()!;

        expect(tokens.preset, preset);
        expect(tokens.panelCornerRadius, isA<double>());
        expect(tokens.panelCornerRadius, panelRadius);
        expect(tokens.primaryActionCornerRadius, isA<double>());
        expect(tokens.primaryActionCornerRadius, primaryRadius);
      });
    }
  });

  group(
    'Behavior prototype brand and status color mappings behave correctly',
    () {
      for (final (
            DovahThemePreset preset,
            Color offline,
            Color tagline,
            Color accent,
            Color mark,
            Color track,
          )
          in [
            (
              DovahThemePreset.frostbound,
              const Color(0xFF7C8993),
              const Color(0xFF82919A),
              const Color(0xFFA9C7D1),
              const Color(0xFFBD5559),
              const Color(0xFF1B2931),
            ),
            (
              DovahThemePreset.dovah,
              const Color(0xFF7C8993),
              const Color(0xFF72899A),
              const Color(0xFF74BDE8),
              const Color(0xFFE2A55E),
              const Color(0xFF202B34),
            ),
            (
              DovahThemePreset.hearth,
              const Color(0xFF7F725F),
              const Color(0xFF80674F),
              const Color(0xFFA45F27),
              const Color(0xFF965923),
              const Color(0xFFB89463),
            ),
          ]) {
        test(
          'Behavior ${preset.name} tokens match the prototype brand and status colors',
          () {
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;

            expect(tokens.statusOffline, offline);
            expect(tokens.brandTagline, tagline);
            expect(tokens.brandAccent, accent);
            expect(tokens.markIcon, mark);
            expect(tokens.barTrack, track);
          },
        );
      }
    },
  );

  group(
    'Behavior prototype hero scrim and panel note mappings behave correctly',
    () {
      for (final (
            DovahThemePreset preset,
            Color note,
            List<Color> scrimColors,
            List<double> scrimStops,
            List<Color> floorColors,
            List<double> floorStops,
          )
          in [
            (
              DovahThemePreset.frostbound,
              const Color(0xFF929DA2),
              const [Color(0xF0010406), Color(0x8A020609), Color(0x2B020609)],
              const [0.0, 0.54, 1.0],
              const [Color(0xEB030709), Color(0x00030709)],
              const [0.0, 0.66],
            ),
            (
              DovahThemePreset.dovah,
              const Color(0xFF667C8B),
              const [Color(0xE0050A0F), Color(0x5C050A0F), Color(0x0F050A0F)],
              const [0.0, 0.52, 1.0],
              const [Color(0xE00B141D), Color(0x000B141D)],
              const [0.0, 0.72],
            ),
            (
              DovahThemePreset.hearth,
              const Color(0xFF765B3E),
              const [Color(0xE6EFD9B5), Color(0xC2E4C69C), Color(0x2ECD9E62)],
              const [0.0, 0.34, 0.72],
              const [Color(0xD1E7CCA3), Color(0x94DAB57E), Color(0x00DAB57E)],
              const [0.0, 0.31, 0.68],
            ),
          ]) {
        test(
          'Behavior ${preset.name} tokens match the prototype hero scrims and panel note',
          () {
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;
            final LinearGradient scrim = tokens.heroScrim as LinearGradient;
            final LinearGradient floor =
                tokens.heroFloorScrim as LinearGradient;

            expect(tokens.panelNote, note);
            expect(scrim.colors, scrimColors);
            expect(scrim.stops, scrimStops);
            expect(scrim.begin, Alignment.centerLeft);
            expect(scrim.end, Alignment.centerRight);
            expect(floor.colors, floorColors);
            expect(floor.stops, floorStops);
            expect(floor.begin, Alignment.bottomCenter);
            expect(floor.end, Alignment.topCenter);
          },
        );
      }
    },
  );

  group('Method buildDovahPresetTheme behaves correctly', () {
    test(
      'Method buildDovahPresetTheme attaches DovahThemeTokens with doubleBevel geometry',
      () {
        final ThemeData theme = buildDovahPresetTheme();
        final DovahThemeTokens? tokens = theme.extension<DovahThemeTokens>();

        expect(tokens, isA<DovahThemeTokens>());
        expect(tokens!.cornerStyle, DovahPanelCornerStyle.doubleBevel);
        expect(tokens.primaryActionForeground, const Color(0xFF1A0E04));
        expect(theme.brightness, Brightness.dark);
      },
    );

    test('Method buildDovahPresetTheme attaches its DovahRootThemeMetrics', () {
      expect(
        buildDovahPresetTheme().extension<DovahRootThemeMetrics>(),
        DovahRootThemeMetrics.dovah,
      );
    });

    test(
      'Method buildDovahPresetTheme attaches its DovahConnectionCardThemeMetrics',
      () {
        expect(
          buildDovahPresetTheme().extension<DovahConnectionCardThemeMetrics>(),
          DovahConnectionCardThemeMetrics.dovah,
        );
      },
    );

    test('Method buildDovahPresetTheme attaches its DovahPageThemeMetrics', () {
      expect(
        buildDovahPresetTheme().extension<DovahPageThemeMetrics>(),
        DovahPageThemeMetrics.dovah,
      );
    });

    test(
      'Method buildDovahPresetTheme attaches its DovahSessionThemeMetrics',
      () {
        expect(
          buildDovahPresetTheme().extension<DovahSessionThemeMetrics>(),
          DovahSessionThemeMetrics.dovah,
        );
      },
    );

    test(
      'Method buildDovahPresetTheme attaches its DovahOverviewThemeMetrics',
      () {
        expect(
          buildDovahPresetTheme().extension<DovahOverviewThemeMetrics>(),
          DovahOverviewThemeMetrics.dovah,
        );
      },
    );

    test('Method buildDovahPresetTheme attaches its DovahThemeMaterials', () {
      expect(
        buildDovahPresetTheme().extension<DovahThemeMaterials>(),
        DovahThemeMaterials.dovah,
      );
    });
  });

  group('Method buildHearthTheme behaves correctly', () {
    test(
      'Method buildHearthTheme attaches DovahThemeTokens with rounded geometry',
      () {
        final ThemeData theme = buildHearthTheme();
        final DovahThemeTokens? tokens = theme.extension<DovahThemeTokens>();

        expect(tokens, isA<DovahThemeTokens>());
        expect(tokens!.cornerStyle, DovahPanelCornerStyle.rounded);
        expect(tokens.primaryActionForeground, const Color(0xFFFFF9EE));
        expect(theme.brightness, Brightness.light);
      },
    );

    test('Method buildHearthTheme attaches its DovahRootThemeMetrics', () {
      expect(
        buildHearthTheme().extension<DovahRootThemeMetrics>(),
        DovahRootThemeMetrics.hearth,
      );
    });

    test(
      'Method buildHearthTheme attaches its DovahConnectionCardThemeMetrics',
      () {
        expect(
          buildHearthTheme().extension<DovahConnectionCardThemeMetrics>(),
          DovahConnectionCardThemeMetrics.hearth,
        );
      },
    );

    test('Method buildHearthTheme attaches its DovahPageThemeMetrics', () {
      expect(
        buildHearthTheme().extension<DovahPageThemeMetrics>(),
        DovahPageThemeMetrics.hearth,
      );
    });

    test('Method buildHearthTheme attaches its DovahSessionThemeMetrics', () {
      expect(
        buildHearthTheme().extension<DovahSessionThemeMetrics>(),
        DovahSessionThemeMetrics.hearth,
      );
    });

    test('Method buildHearthTheme attaches its DovahOverviewThemeMetrics', () {
      expect(
        buildHearthTheme().extension<DovahOverviewThemeMetrics>(),
        DovahOverviewThemeMetrics.hearth,
      );
    });

    test('Method buildHearthTheme attaches its DovahThemeMaterials', () {
      expect(
        buildHearthTheme().extension<DovahThemeMaterials>(),
        DovahThemeMaterials.hearth,
      );
    });
  });

  group('Method dovahThemeDataFor behaves correctly', () {
    test(
      'Method dovahThemeDataFor maps frostbound to buildFrostboundTheme',
      () {
        final DovahThemeTokens expected = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;

        final DovahThemeTokens actual = dovahThemeDataFor(
          DovahThemePreset.frostbound,
        ).extension<DovahThemeTokens>()!;

        expect(actual, expected);
      },
    );

    test('Method dovahThemeDataFor maps dovah to buildDovahPresetTheme', () {
      final DovahThemeTokens expected = buildDovahPresetTheme()
          .extension<DovahThemeTokens>()!;

      final DovahThemeTokens actual = dovahThemeDataFor(
        DovahThemePreset.dovah,
      ).extension<DovahThemeTokens>()!;

      expect(actual, expected);
    });

    test('Method dovahThemeDataFor maps hearth to buildHearthTheme', () {
      final DovahThemeTokens expected = buildHearthTheme()
          .extension<DovahThemeTokens>()!;

      final DovahThemeTokens actual = dovahThemeDataFor(
        DovahThemePreset.hearth,
      ).extension<DovahThemeTokens>()!;

      expect(actual, expected);
    });

    test(
      'Method dovahThemeDataFor reuses a distinct ThemeData for every preset',
      () {
        final ThemeData frostbound = dovahThemeDataFor(
          DovahThemePreset.frostbound,
        );
        final ThemeData dovah = dovahThemeDataFor(DovahThemePreset.dovah);
        final ThemeData hearth = dovahThemeDataFor(DovahThemePreset.hearth);

        expect(identical(frostbound, dovah), isFalse);
        expect(identical(frostbound, hearth), isFalse);
        expect(identical(dovah, hearth), isFalse);
        expect(
          identical(frostbound, dovahThemeDataFor(DovahThemePreset.frostbound)),
          isTrue,
        );
        expect(
          identical(dovah, dovahThemeDataFor(DovahThemePreset.dovah)),
          isTrue,
        );
        expect(
          identical(hearth, dovahThemeDataFor(DovahThemePreset.hearth)),
          isTrue,
        );
      },
    );
  });

  group('Behavior distinct presets behaves correctly', () {
    test(
      'Behavior distinct presets use a different corner style per theme',
      () {
        final DovahThemeTokens frostbound = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens dovah = buildDovahPresetTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens hearth = buildHearthTheme()
            .extension<DovahThemeTokens>()!;

        final Set<DovahPanelCornerStyle> cornerStyles = {
          frostbound.cornerStyle,
          dovah.cornerStyle,
          hearth.cornerStyle,
        };

        expect(cornerStyles, hasLength(3));
      },
    );

    test(
      'Behavior distinct presets use a different corner radius per theme',
      () {
        final DovahThemeTokens frostbound = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens dovah = buildDovahPresetTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens hearth = buildHearthTheme()
            .extension<DovahThemeTokens>()!;

        final Set<double> radii = {
          frostbound.cornerRadius,
          dovah.cornerRadius,
          hearth.cornerRadius,
        };

        expect(radii, hasLength(3));
      },
    );

    test(
      'Behavior distinct presets use a different display font family per theme',
      () {
        final DovahThemeTokens frostbound = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens dovah = buildDovahPresetTheme()
            .extension<DovahThemeTokens>()!;

        expect(frostbound.displayFontFamily, isNot(dovah.displayFontFamily));
      },
    );

    for (final (DovahThemePreset preset, Color glyph) in [
      (DovahThemePreset.frostbound, const Color(0xFFA9C7D1)),
      (DovahThemePreset.dovah, const Color(0xFF8ED6FF)),
      (DovahThemePreset.hearth, const Color(0xFF60462D)),
    ]) {
      test(
        'Behavior ${preset.name} tokens match the prototype icon tile glyph color',
        () {
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;

          expect(tokens.iconTileForeground, glyph);
        },
      );
    }

    for (final (DovahThemePreset preset, String family, List<String> fallback)
        in [
          (DovahThemePreset.frostbound, 'Arial Narrow', const ['Impact']),
          (DovahThemePreset.dovah, 'Georgia', const ['Times New Roman']),
          (DovahThemePreset.hearth, 'Georgia', const ['Times New Roman']),
        ]) {
      test(
        'Behavior ${preset.name} tokens carry the prototype display stack',
        () {
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;

          expect(tokens.displayFontFamily, family);
          expect(tokens.displayFontFamilyFallback, fallback);
        },
      );

      test(
        'Behavior ${preset.name} sets the prototype body font on its theme',
        () {
          final ThemeData theme = dovahThemeDataFor(preset);

          expect(theme.textTheme.bodyMedium?.fontFamily, 'Inter');
          expect(theme.textTheme.bodyMedium?.fontFamilyFallback, const [
            'Segoe UI',
          ]);
          expect(theme.textTheme.titleLarge?.fontFamily, 'Inter');
        },
      );
    }

    test('Behavior distinct presets differ in background color per theme', () {
      final DovahThemeTokens frostbound = buildFrostboundTheme()
          .extension<DovahThemeTokens>()!;
      final DovahThemeTokens dovah = buildDovahPresetTheme()
          .extension<DovahThemeTokens>()!;
      final DovahThemeTokens hearth = buildHearthTheme()
          .extension<DovahThemeTokens>()!;

      final Set<Color> backgrounds = {
        frostbound.background,
        dovah.background,
        hearth.background,
      };

      expect(backgrounds, hasLength(3));
    });
  });
}
