import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_preset_theme.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/frostbound_theme.dart';
import 'package:dovahlink_client/shared/theme/hearth_theme.dart';

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
        expect((tokens.primaryActionGradient as LinearGradient).colors, const [
          Color(0xFF263239),
          Color(0xFF11191D),
        ]);
        expect(theme.brightness, Brightness.dark);
      },
    );
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

  group('Method buildDovahPresetTheme behaves correctly', () {
    test(
      'Method buildDovahPresetTheme attaches DovahThemeTokens with doubleBevel geometry',
      () {
        final ThemeData theme = buildDovahPresetTheme();
        final DovahThemeTokens? tokens = theme.extension<DovahThemeTokens>();

        expect(tokens, isA<DovahThemeTokens>());
        expect(tokens!.cornerStyle, DovahPanelCornerStyle.doubleBevel);
        expect(tokens.primaryActionForeground, const Color(0xFF1A0E04));
        expect((tokens.primaryActionGradient as LinearGradient).colors, const [
          Color(0xFFF0BD73),
          Color(0xFFC77D38),
        ]);
        expect(tokens.environmentAssetPath, isNull);
        expect(theme.brightness, Brightness.dark);
      },
    );
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
        expect((tokens.primaryActionGradient as LinearGradient).colors, const [
          Color(0xFFA96932),
          Color(0xFF82491E),
        ]);
        expect(theme.brightness, Brightness.light);
      },
    );
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
      'Behavior distinct presets use a different density scale per theme',
      () {
        final DovahThemeTokens frostbound = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens dovah = buildDovahPresetTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens hearth = buildHearthTheme()
            .extension<DovahThemeTokens>()!;

        final Set<double> densities = {
          frostbound.densityScale,
          dovah.densityScale,
          hearth.densityScale,
        };

        expect(densities, hasLength(3));
        expect(frostbound.densityScale, lessThan(dovah.densityScale));
        expect(dovah.densityScale, lessThan(hearth.densityScale));
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

    test(
      'Behavior distinct presets use a different material gradient per theme',
      () {
        final DovahThemeTokens frostbound = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens dovah = buildDovahPresetTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens hearth = buildHearthTheme()
            .extension<DovahThemeTokens>()!;

        expect(frostbound.materialGradient, isNot(dovah.materialGradient));
        expect(dovah.materialGradient, isNot(hearth.materialGradient));
      },
    );

    test(
      'Behavior distinct presets use a different material-raised gradient per theme',
      () {
        final DovahThemeTokens frostbound = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens dovah = buildDovahPresetTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens hearth = buildHearthTheme()
            .extension<DovahThemeTokens>()!;

        expect(
          frostbound.materialRaisedGradient,
          isNot(dovah.materialRaisedGradient),
        );
        expect(
          dovah.materialRaisedGradient,
          isNot(hearth.materialRaisedGradient),
        );
      },
    );

    test(
      'Behavior distinct presets use a different panel shadow per theme',
      () {
        final DovahThemeTokens frostbound = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens dovah = buildDovahPresetTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens hearth = buildHearthTheme()
            .extension<DovahThemeTokens>()!;

        expect(frostbound.panelShadow, isNot(dovah.panelShadow));
        expect(dovah.panelShadow, isNot(hearth.panelShadow));
      },
    );

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

    test(
      'Behavior distinct presets only Frostbound and Hearth have an environment asset',
      () {
        final DovahThemeTokens frostbound = buildFrostboundTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens dovah = buildDovahPresetTheme()
            .extension<DovahThemeTokens>()!;
        final DovahThemeTokens hearth = buildHearthTheme()
            .extension<DovahThemeTokens>()!;

        expect(frostbound.environmentAssetPath, isA<String>());
        expect(dovah.environmentAssetPath, isNull);
        expect(hearth.environmentAssetPath, isA<String>());
      },
    );
  });
}
