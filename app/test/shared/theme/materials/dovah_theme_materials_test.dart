import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_radial_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_stripe_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';

/// The three presets' materials, in [DovahThemePreset] order.
const List<DovahThemeMaterials> presetMaterials = [
  DovahThemeMaterials.frostbound,
  DovahThemeMaterials.dovah,
  DovahThemeMaterials.hearth,
];

/// Returns how many layers of type [T] [material] paints.
int countLayers<T extends DovahMaterialLayer>(DovahMaterial material) =>
    material.layers.whereType<T>().length;

/// Exercises [DovahThemeMaterials]'s prototype recipes per preset, role lookup, replacement,
/// interpolation, and value semantics.
void main() {
  group('Property preset recipes behave correctly', () {
    test('Property frostbound keeps the prototype surface recipe', () {
      final DovahMaterial surface = DovahThemeMaterials.frostbound.surface;

      expect(surface.layers, hasLength(6));
      expect(countLayers<DovahLinearLayer>(surface), 3);
      expect(countLayers<DovahRadialLayer>(surface), 2);
      expect(countLayers<DovahStripeLayer>(surface), 1);
      expect(surface.borderColor, const Color(0xFF71808A));
      expect(surface.topEdgeHighlight, isNotNull);
      expect(surface.bottomEdgeShade, isNotNull);
      expect(surface.shadow, isEmpty);
    });

    test('Property dovah keeps the prototype surface recipe', () {
      final DovahMaterial surface = DovahThemeMaterials.dovah.surface;

      expect(surface.layers, hasLength(4));
      expect(countLayers<DovahLinearLayer>(surface), 2);
      expect(countLayers<DovahStripeLayer>(surface), 2);
      expect(surface.borderColor, const Color(0xFF4A6B84));
      expect(surface.topEdgeHighlight, isNotNull);
      expect(surface.bottomEdgeShade, isNull);
      expect(surface.shadow, isEmpty);
    });

    test('Property hearth keeps the prototype surface recipe', () {
      final DovahMaterial surface = DovahThemeMaterials.hearth.surface;

      expect(surface.layers, hasLength(4));
      expect(countLayers<DovahRadialLayer>(surface), 2);
      expect(countLayers<DovahStripeLayer>(surface), 1);
      expect(surface.borderColor, const Color(0xFF8D6336));
      expect(surface.topEdgeHighlight, isNotNull);
      expect(surface.bottomEdgeShade, isNotNull);
      expect(surface.shadow, hasLength(1));
    });

    test(
      'Property raised uses its own layers and the hover border per preset',
      () {
        expect(DovahThemeMaterials.frostbound.raised.layers, hasLength(4));
        expect(
          DovahThemeMaterials.frostbound.raised.borderColor,
          const Color(0xFFA9C7D1),
        );
        expect(DovahThemeMaterials.dovah.raised.layers, hasLength(3));
        expect(
          DovahThemeMaterials.dovah.raised.borderColor,
          const Color(0xFF8ED6FF),
        );
        expect(DovahThemeMaterials.hearth.raised.layers, hasLength(3));
        expect(
          DovahThemeMaterials.hearth.raised.borderColor,
          const Color(0xFF965923),
        );
      },
    );

    test(
      'Property control reuses the raised layers without edges or shadow',
      () {
        for (final DovahThemeMaterials materials in presetMaterials) {
          expect(materials.control.layers, materials.raised.layers);
          expect(materials.control.topEdgeHighlight, isNull);
          expect(materials.control.bottomEdgeShade, isNull);
          expect(materials.control.shadow, isEmpty);
        }

        expect(
          DovahThemeMaterials.frostbound.control.borderColor,
          const Color(0xFF71808A),
        );
        expect(
          DovahThemeMaterials.dovah.control.borderColor,
          const Color(0xFF4A6B84),
        );
        expect(
          DovahThemeMaterials.hearth.control.borderColor,
          const Color(0xFF79542F),
        );
      },
    );

    test('Property icon keeps the prototype tile recipe per preset', () {
      expect(DovahThemeMaterials.frostbound.icon.layers, hasLength(1));
      expect(
        countLayers<DovahStripeLayer>(DovahThemeMaterials.frostbound.icon),
        1,
      );
      expect(DovahThemeMaterials.dovah.icon.layers, hasLength(2));
      expect(
        DovahThemeMaterials.dovah.icon.borderColor,
        const Color.fromRGBO(226, 165, 94, 0.62),
      );
      expect(DovahThemeMaterials.dovah.icon.shadow, hasLength(1));
      expect(DovahThemeMaterials.hearth.icon.layers, hasLength(2));
      expect(
        (DovahThemeMaterials.hearth.icon.layers.last as DovahRadialLayer)
            .repeating,
        isTrue,
      );
    });

    test(
      'Property primaryAction keeps the prototype button recipe per preset',
      () {
        expect(
          DovahThemeMaterials.frostbound.primaryAction.layers,
          hasLength(3),
        );
        expect(
          DovahThemeMaterials.frostbound.primaryAction.borderColor,
          const Color(0xFF7C9099),
        );
        expect(DovahThemeMaterials.dovah.primaryAction.layers, hasLength(2));
        expect(DovahThemeMaterials.dovah.primaryAction.borderColor, isNull);
        expect(DovahThemeMaterials.hearth.primaryAction.layers, hasLength(2));
        expect(DovahThemeMaterials.hearth.primaryAction.shadow, hasLength(1));
      },
    );

    test(
      'Property primaryAction keeps the approved fill colors per preset',
      () {
        const List<(DovahThemeMaterials, List<Color>)> expected = [
          (
            DovahThemeMaterials.frostbound,
            [Color(0xFF263239), Color(0xFF11191D)],
          ),
          (DovahThemeMaterials.dovah, [Color(0xFFF0BD73), Color(0xFFC77D38)]),
          (DovahThemeMaterials.hearth, [Color(0xFFA96932), Color(0xFF82491E)]),
        ];

        for (final (DovahThemeMaterials materials, List<Color> colors)
            in expected) {
          expect(
            (materials.primaryAction.layers.first as DovahLinearLayer).colors,
            colors,
          );
        }
      },
    );

    test('Property every layer of every material builds a shader', () {
      for (final DovahThemeMaterials materials in presetMaterials) {
        for (final DovahMaterialRole role in DovahMaterialRole.values) {
          for (final DovahMaterialLayer layer
              in materials.forRole(role).layers) {
            expect(
              () => layer.createShader(const Size(320, 96)),
              returnsNormally,
              reason: '$role layer $layer',
            );
          }
        }
      }
    });
  });

  group('Behavior distinct presets behaves correctly', () {
    test(
      'Behavior distinct presets never share a material for the same role',
      () {
        for (final DovahMaterialRole role in DovahMaterialRole.values) {
          expect(
            DovahThemeMaterials.frostbound.forRole(role),
            isNot(DovahThemeMaterials.dovah.forRole(role)),
          );
          expect(
            DovahThemeMaterials.dovah.forRole(role),
            isNot(DovahThemeMaterials.hearth.forRole(role)),
          );
          expect(
            DovahThemeMaterials.frostbound.forRole(role),
            isNot(DovahThemeMaterials.hearth.forRole(role)),
          );
        }
      },
    );

    test(
      'Behavior distinct presets keep every role distinct within a preset',
      () {
        for (final DovahThemeMaterials materials in presetMaterials) {
          final Set<DovahMaterial> distinct = {
            for (final DovahMaterialRole role in DovahMaterialRole.values)
              materials.forRole(role),
          };

          expect(distinct, hasLength(DovahMaterialRole.values.length));
        }
      },
    );

    test(
      'Behavior distinct presets cast a drop shadow only where the prototype does',
      () {
        expect(DovahThemeMaterials.frostbound.surface.shadow, isEmpty);
        expect(DovahThemeMaterials.frostbound.primaryAction.shadow, isEmpty);
        expect(DovahThemeMaterials.dovah.surface.shadow, isEmpty);
        expect(DovahThemeMaterials.dovah.primaryAction.shadow, isEmpty);
        expect(DovahThemeMaterials.hearth.surface.shadow, isNotEmpty);
        expect(DovahThemeMaterials.hearth.primaryAction.shadow, isNotEmpty);
      },
    );
  });

  group('Method forRole behaves correctly', () {
    test('Method forRole returns the matching material for every role', () {
      for (final DovahThemeMaterials materials in presetMaterials) {
        expect(materials.forRole(DovahMaterialRole.surface), materials.surface);
        expect(materials.forRole(DovahMaterialRole.raised), materials.raised);
        expect(materials.forRole(DovahMaterialRole.control), materials.control);
        expect(materials.forRole(DovahMaterialRole.icon), materials.icon);
        expect(
          materials.forRole(DovahMaterialRole.primaryAction),
          materials.primaryAction,
        );
      }
    });
  });

  group('Method copyWith behaves correctly', () {
    const DovahMaterial replacement = DovahMaterial(layers: []);
    const DovahThemeMaterials base = DovahThemeMaterials.dovah;

    test('Method copyWith replaces only the surface', () {
      final DovahThemeMaterials copy = base.copyWith(surface: replacement);

      expect(copy.surface, replacement);
      expect(copy.raised, base.raised);
      expect(copy.control, base.control);
      expect(copy.icon, base.icon);
      expect(copy.primaryAction, base.primaryAction);
    });

    test('Method copyWith replaces only the raised material', () {
      final DovahThemeMaterials copy = base.copyWith(raised: replacement);

      expect(copy.raised, replacement);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith replaces only the control material', () {
      final DovahThemeMaterials copy = base.copyWith(control: replacement);

      expect(copy.control, replacement);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith replaces only the icon material', () {
      final DovahThemeMaterials copy = base.copyWith(icon: replacement);

      expect(copy.icon, replacement);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith replaces only the primary action material', () {
      final DovahThemeMaterials copy = base.copyWith(
        primaryAction: replacement,
      );

      expect(copy.primaryAction, replacement);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith keeps every material when nothing is passed', () {
      expect(base.copyWith(), base);
    });
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp keeps this before the midpoint', () {
      expect(
        DovahThemeMaterials.frostbound.lerp(DovahThemeMaterials.hearth, 0.49),
        DovahThemeMaterials.frostbound,
      );
    });

    test('Method lerp switches to the other materials at the midpoint', () {
      expect(
        DovahThemeMaterials.frostbound.lerp(DovahThemeMaterials.hearth, 0.5),
        DovahThemeMaterials.hearth,
      );
    });

    test('Method lerp returns the other materials after the midpoint', () {
      expect(
        DovahThemeMaterials.frostbound.lerp(DovahThemeMaterials.hearth, 1),
        DovahThemeMaterials.hearth,
      );
    });

    test('Method lerp returns this when the other extension is null', () {
      expect(
        DovahThemeMaterials.frostbound.lerp(null, 1),
        DovahThemeMaterials.frostbound,
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same preset', () {
      final DovahThemeMaterials copy = DovahThemeMaterials.dovah.copyWith();

      expect(copy == DovahThemeMaterials.dovah, isTrue);
      expect(copy.hashCode, DovahThemeMaterials.dovah.hashCode);
    });

    test('Behavior equality fails when any material differs', () {
      const DovahMaterial replacement = DovahMaterial(layers: []);
      const DovahThemeMaterials base = DovahThemeMaterials.dovah;

      expect(base == base.copyWith(surface: replacement), isFalse);
      expect(base == base.copyWith(raised: replacement), isFalse);
      expect(base == base.copyWith(control: replacement), isFalse);
      expect(base == base.copyWith(icon: replacement), isFalse);
      expect(base == base.copyWith(primaryAction: replacement), isFalse);
    });
  });
}
