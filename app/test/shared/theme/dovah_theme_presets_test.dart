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
        expect(theme.brightness, Brightness.dark);
      },
    );
  });

  group('Method buildDovahPresetTheme behaves correctly', () {
    test(
      'Method buildDovahPresetTheme attaches DovahThemeTokens with doubleBevel geometry',
      () {
        final ThemeData theme = buildDovahPresetTheme();
        final DovahThemeTokens? tokens = theme.extension<DovahThemeTokens>();

        expect(tokens, isA<DovahThemeTokens>());
        expect(tokens!.cornerStyle, DovahPanelCornerStyle.doubleBevel);
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
