import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_tile.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
import 'widgets/dovah_widget_test_helpers.dart';

/// The shape and material one component is expected to paint in one theme.
typedef _Expected = ({DovahPanelCornerStyle style, double cut, double radius});

/// The windows the matrix covers: a regular landscape window and a compact, short one.
const List<Size> matrixSizes = [Size(1280, 720), Size(900, 560)];

/// Returns the first material painter under [scope].
DovahMaterialPainter painterUnder(WidgetTester tester, Finder scope) =>
    tester
            .widget<CustomPaint>(
              find
                  .descendant(of: scope, matching: find.byType(CustomPaint))
                  .first,
            )
            .painter!
        as DovahMaterialPainter;

/// The theme's materials.
DovahThemeMaterials materialsOf(DovahThemePreset preset) =>
    dovahThemeDataFor(preset).extension<DovahThemeMaterials>()!;

/// Proves every shared themed component keeps its prototype shape and material in each of the
/// three themes at a regular and a compact window. This is the repository's visual regression
/// matrix. It asserts the painted geometry and the paint recipe instead of comparing screenshots:
/// there is no golden infrastructure, a golden baseline is a binary file that varies with the
/// renderer, and geometry plus recipe values are exactly what a fidelity regression changes.
void main() {
  const Map<DovahThemePreset, _Expected> panel = {
    DovahThemePreset.frostbound: (
      style: DovahPanelCornerStyle.singleBevel,
      cut: 9,
      radius: 0,
    ),
    DovahThemePreset.dovah: (
      style: DovahPanelCornerStyle.doubleBevel,
      cut: 12,
      radius: 0,
    ),
    DovahThemePreset.hearth: (
      style: DovahPanelCornerStyle.rounded,
      cut: 0,
      radius: 14,
    ),
  };
  const Map<DovahThemePreset, _Expected> connection = {
    DovahThemePreset.frostbound: (
      style: DovahPanelCornerStyle.singleBevel,
      cut: 11,
      radius: 0,
    ),
    DovahThemePreset.dovah: (
      style: DovahPanelCornerStyle.doubleBevel,
      cut: 16,
      radius: 0,
    ),
    DovahThemePreset.hearth: (
      style: DovahPanelCornerStyle.rounded,
      cut: 0,
      radius: 12,
    ),
  };
  const Map<DovahThemePreset, _Expected> primary = {
    DovahThemePreset.frostbound: (
      style: DovahPanelCornerStyle.singleBevel,
      cut: 9,
      radius: 0,
    ),
    DovahThemePreset.dovah: (
      style: DovahPanelCornerStyle.doubleBevel,
      cut: 12,
      radius: 0,
    ),
    DovahThemePreset.hearth: (
      style: DovahPanelCornerStyle.rounded,
      cut: 0,
      radius: 9,
    ),
  };
  const Map<DovahThemePreset, _Expected> appearanceCard = {
    DovahThemePreset.frostbound: (
      style: DovahPanelCornerStyle.singleBevel,
      cut: 9,
      radius: 0,
    ),
    DovahThemePreset.dovah: (
      style: DovahPanelCornerStyle.doubleBevel,
      cut: 10,
      radius: 0,
    ),
    DovahThemePreset.hearth: (
      style: DovahPanelCornerStyle.rounded,
      cut: 0,
      radius: 13,
    ),
  };
  const Map<DovahThemePreset, double> controlRadius = {
    DovahThemePreset.frostbound: 0,
    DovahThemePreset.dovah: 3,
    DovahThemePreset.hearth: 13,
  };
  const Map<DovahThemePreset, double> tileRotation = {
    DovahThemePreset.frostbound: 0,
    DovahThemePreset.dovah: math.pi / 4,
    DovahThemePreset.hearth: 0,
  };

  for (final DovahThemePreset preset in DovahThemePreset.values) {
    for (final Size size in matrixSizes) {
      final bool compact = size.height <= 620;
      final String at = '$preset at $size';

      testWidgets(
        'Matrix connection card keeps its shape and material under $at',
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
            size: size,
          );
          final Finder outer = find.byWidgetPredicate(
            (Widget widget) =>
                widget is DovahSurface &&
                widget.role == DovahMaterialRole.surface,
          );
          final DovahMaterialPainter painter = painterUnder(tester, outer);
          final DovahIconTile tile = tester.widget(find.byType(DovahIconTile));
          final _Expected want = connection[preset]!;

          expect(painter.cornerStyle, want.style);
          expect(painter.cutSize, want.cut);
          expect(painter.cornerRadius, want.radius);
          expect(painter.material.layers, materialsOf(preset).surface.layers);
          expect(tile.rotation, tileRotation[preset]);
          expect(
            tile.size,
            preset == DovahThemePreset.frostbound || compact ? 37 : 43,
          );
          expect(tester.takeException(), isNull);
        },
      );

      testWidgets(
        'Matrix appearance card keeps its shape and material under $at',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            SizedBox(
              width: 240,
              child: AppearancePresetCard(
                preset: preset,
                selected: true,
                onTap: () {},
              ),
            ),
            preset: preset,
            size: size,
          );
          final DovahMaterialPainter painter = painterUnder(
            tester,
            find.byKey(const Key('appearance-preset-card-surface')),
          );
          final _Expected want = appearanceCard[preset]!;

          expect(painter.cornerStyle, want.style);
          expect(painter.cutSize, want.cut);
          expect(painter.cornerRadius, want.radius);
          expect(painter.material, materialsOf(preset).presetCard.material);
          expect(
            find.byKey(const Key('appearance-preset-card-badge')),
            findsOneWidget,
          );
          expect(
            find.text(preset.materials),
            compact ? findsNothing : findsOneWidget,
          );
        },
      );

      testWidgets(
        'Matrix primary button keeps its shape and material under $at',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'Pair', onPressed: () {}),
            preset: preset,
            size: size,
          );
          final DovahMaterialPainter painter = painterUnder(
            tester,
            find.byType(DovahSurface),
          );
          final _Expected want = primary[preset]!;

          expect(painter.cornerStyle, want.style);
          expect(painter.cutSize, want.cut);
          expect(painter.cornerRadius, want.radius);
          expect(painter.material, materialsOf(preset).primaryAction);
        },
      );

      testWidgets('Matrix icon button keeps its shape and material under $at', (
        WidgetTester tester,
      ) async {
        await pumpDovahThemedWidget(
          tester,
          DovahIconButton(
            icon: Icons.settings_outlined,
            label: 'Appearance settings',
            onPressed: () {},
          ),
          preset: preset,
          size: size,
        );
        final DovahMaterialPainter painter = painterUnder(
          tester,
          find.byType(DovahSurface),
        );

        expect(painter.cornerStyle, DovahPanelCornerStyle.rounded);
        expect(painter.cornerRadius, controlRadius[preset]);
        expect(painter.material, materialsOf(preset).control);
      });

      testWidgets('Matrix icon tile keeps its shape and material under $at', (
        WidgetTester tester,
      ) async {
        await pumpDovahThemedWidget(
          tester,
          Center(
            child: DovahIconTile(
              size: 40,
              cornerRadius: 0,
              rotation: tileRotation[preset]!,
              child: const Icon(Icons.desktop_windows_outlined),
            ),
          ),
          preset: preset,
          size: size,
        );
        final DovahMaterialPainter painter = painterUnder(
          tester,
          find.byType(DovahSurface),
        );

        expect(painter.cornerStyle, DovahPanelCornerStyle.rounded);
        expect(painter.material, materialsOf(preset).icon);
        expect(
          tester.getSize(find.byType(DovahIconTile)),
          const Size.square(40),
        );
      });

      testWidgets('Matrix dialog keeps its shape and material under $at', (
        WidgetTester tester,
      ) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahDialog(title: 'Appearance', child: Text('Body')),
          preset: preset,
          size: size,
        );
        final DovahMaterialPainter painter = painterUnder(
          tester,
          find.byType(DovahSurface).first,
        );
        final _Expected want = panel[preset]!;

        expect(painter.cornerStyle, want.style);
        expect(painter.cutSize, want.cut);
        expect(painter.cornerRadius, want.radius);
        expect(painter.material, materialsOf(preset).surface);
        expect(tester.takeException(), isNull);
      });
    }
  }
}
