import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_radial_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_stripe_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_tile_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/frostbound_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/hearth_materials.dart';

/// The three presets' materials, in [DovahThemePreset] order.
const List<DovahThemeMaterials> presetMaterials = [
  frostboundMaterials,
  dovahMaterials,
  hearthMaterials,
];

/// Returns how many layers of type [T] [material] paints.
int countLayers<T extends DovahMaterialLayer>(DovahMaterial material) =>
    material.layers.whereType<T>().length;

/// Exercises [DovahThemeMaterials]'s prototype recipes per preset, role lookup, replacement,
/// interpolation, and value semantics.
void main() {
  group('Property preset recipes behave correctly', () {
    test('Property frostbound keeps the prototype surface recipe', () {
      final DovahMaterial surface = frostboundMaterials.surface;

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
      final DovahMaterial surface = dovahMaterials.surface;

      expect(surface.layers, hasLength(4));
      expect(countLayers<DovahLinearLayer>(surface), 2);
      expect(countLayers<DovahStripeLayer>(surface), 2);
      expect(surface.borderColor, const Color(0xFF4A6B84));
      expect(surface.topEdgeHighlight, isNotNull);
      expect(surface.bottomEdgeShade, isNull);
      expect(surface.shadow, isEmpty);
    });

    test('Property hearth keeps the prototype surface recipe', () {
      final DovahMaterial surface = hearthMaterials.surface;

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
        expect(frostboundMaterials.raised.layers, hasLength(4));
        expect(frostboundMaterials.raised.borderColor, const Color(0xFFA9C7D1));
        expect(dovahMaterials.raised.layers, hasLength(3));
        expect(dovahMaterials.raised.borderColor, const Color(0xFF8ED6FF));
        expect(hearthMaterials.raised.layers, hasLength(3));
        expect(hearthMaterials.raised.borderColor, const Color(0xFF965923));
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
          frostboundMaterials.control.borderColor,
          const Color(0xFF71808A),
        );
        expect(dovahMaterials.control.borderColor, const Color(0xFF4A6B84));
        expect(hearthMaterials.control.borderColor, const Color(0xFF79542F));
      },
    );

    test('Property icon keeps the prototype tile recipe per preset', () {
      expect(frostboundMaterials.icon.layers, hasLength(1));
      expect(countLayers<DovahStripeLayer>(frostboundMaterials.icon), 1);
      expect(dovahMaterials.icon.layers, hasLength(2));
      expect(
        dovahMaterials.icon.borderColor,
        const Color.fromRGBO(226, 165, 94, 0.62),
      );
      expect(dovahMaterials.icon.shadow, hasLength(1));
      expect(hearthMaterials.icon.layers, hasLength(2));
      expect(
        (hearthMaterials.icon.layers.last as DovahRadialLayer).repeating,
        isTrue,
      );
    });

    test(
      'Property primaryAction keeps the prototype button recipe per preset',
      () {
        expect(frostboundMaterials.primaryAction.layers, hasLength(3));
        expect(
          frostboundMaterials.primaryAction.borderColor,
          const Color(0xFF7C9099),
        );
        expect(dovahMaterials.primaryAction.layers, hasLength(2));
        expect(dovahMaterials.primaryAction.borderColor, isNull);
        expect(hearthMaterials.primaryAction.layers, hasLength(2));
        expect(hearthMaterials.primaryAction.shadow, hasLength(1));
      },
    );

    test(
      'Property primaryAction keeps the approved fill colors per preset',
      () {
        const List<(DovahThemeMaterials, List<Color>)> expected = [
          (frostboundMaterials, [Color(0xFF263239), Color(0xFF11191D)]),
          (dovahMaterials, [Color(0xFFF0BD73), Color(0xFFC77D38)]),
          (hearthMaterials, [Color(0xFFA96932), Color(0xFF82491E)]),
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

    test('Property frostbound keeps the prototype atmosphere', () {
      final DovahAtmosphere atmosphere = frostboundMaterials.atmosphere;

      expect(atmosphere.imageAssetPath, frostboundEnvironmentAsset);
      expect(
        atmosphere.imageFilter,
        const DovahColorFilter(grayscale: 0.34, saturate: 0.48, contrast: 1.16),
      );
      expect(atmosphere.layers, hasLength(1));
      expect(atmosphere.hazeLayers, hasLength(2));
      expect(atmosphere.hazeOpacity, isA<double>());
      expect(atmosphere.hazeOpacity, 0.3);
    });

    test('Property dovah keeps the prototype atmosphere without an image', () {
      final DovahAtmosphere atmosphere = dovahMaterials.atmosphere;

      expect(atmosphere.imageAssetPath, isNull);
      expect(atmosphere.imageFilter.isNeutral, isTrue);
      expect(atmosphere.layers, hasLength(3));
      expect(atmosphere.hazeLayers, hasLength(2));
      expect(atmosphere.hazeLayers.every((l) => l is DovahTileLayer), isTrue);
      expect(atmosphere.hazeOpacity, isA<double>());
      expect(atmosphere.hazeOpacity, 0.32);
    });

    test('Property hearth keeps the prototype atmosphere', () {
      final DovahAtmosphere atmosphere = hearthMaterials.atmosphere;

      expect(atmosphere.imageAssetPath, hearthEnvironmentAsset);
      expect(
        atmosphere.imageFilter,
        const DovahColorFilter(
          brightness: 0.92,
          saturate: 0.92,
          contrast: 1.06,
        ),
      );
      expect(atmosphere.layers, hasLength(1));
      expect(atmosphere.hazeLayers, hasLength(1));
      expect(atmosphere.hazeLayers.single, isA<DovahTileLayer>());
      expect(atmosphere.hazeOpacity, isA<double>());
      expect(atmosphere.hazeOpacity, 0.34);
    });

    test('Property every atmosphere layer of every preset builds a shader', () {
      for (final DovahThemeMaterials materials in presetMaterials) {
        for (final DovahMaterialLayer layer in [
          ...materials.atmosphere.layers,
          ...materials.atmosphere.hazeLayers,
        ]) {
          expect(
            () => layer.createShader(const Size(320, 200)),
            returnsNormally,
            reason: 'atmosphere layer $layer',
          );
        }
      }
    });

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
    test('Behavior distinct presets never share an atmosphere', () {
      expect(frostboundMaterials.atmosphere, isNot(dovahMaterials.atmosphere));
      expect(dovahMaterials.atmosphere, isNot(hearthMaterials.atmosphere));
      expect(frostboundMaterials.atmosphere, isNot(hearthMaterials.atmosphere));
    });

    test(
      'Behavior distinct presets never share a material for the same role',
      () {
        for (final DovahMaterialRole role in DovahMaterialRole.values) {
          expect(
            frostboundMaterials.forRole(role),
            isNot(dovahMaterials.forRole(role)),
          );
          expect(
            dovahMaterials.forRole(role),
            isNot(hearthMaterials.forRole(role)),
          );
          expect(
            frostboundMaterials.forRole(role),
            isNot(hearthMaterials.forRole(role)),
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
        expect(frostboundMaterials.surface.shadow, isEmpty);
        expect(frostboundMaterials.primaryAction.shadow, isEmpty);
        expect(dovahMaterials.surface.shadow, isEmpty);
        expect(dovahMaterials.primaryAction.shadow, isEmpty);
        expect(hearthMaterials.surface.shadow, isNotEmpty);
        expect(hearthMaterials.primaryAction.shadow, isNotEmpty);
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
    const DovahThemeMaterials base = dovahMaterials;

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

    test('Method copyWith replaces only the atmosphere', () {
      const DovahAtmosphere plain = DovahAtmosphere();
      final DovahThemeMaterials copy = base.copyWith(atmosphere: plain);

      expect(copy.atmosphere, plain);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith keeps every material when nothing is passed', () {
      expect(base.copyWith(), base);
    });
  });

  group('Method lerp behaves correctly', () {
    test('Method lerp keeps this before the midpoint', () {
      expect(
        frostboundMaterials.lerp(hearthMaterials, 0.49),
        frostboundMaterials,
      );
    });

    test('Method lerp switches to the other materials at the midpoint', () {
      expect(frostboundMaterials.lerp(hearthMaterials, 0.5), hearthMaterials);
    });

    test('Method lerp returns the other materials after the midpoint', () {
      expect(frostboundMaterials.lerp(hearthMaterials, 1), hearthMaterials);
    });

    test('Method lerp returns this when the other extension is null', () {
      expect(frostboundMaterials.lerp(null, 1), frostboundMaterials);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for the same preset', () {
      final DovahThemeMaterials copy = dovahMaterials.copyWith();

      expect(copy == dovahMaterials, isTrue);
      expect(copy.hashCode, dovahMaterials.hashCode);
    });

    test('Behavior equality fails when any material differs', () {
      const DovahMaterial replacement = DovahMaterial(layers: []);
      const DovahThemeMaterials base = dovahMaterials;

      expect(base == base.copyWith(surface: replacement), isFalse);
      expect(base == base.copyWith(raised: replacement), isFalse);
      expect(base == base.copyWith(control: replacement), isFalse);
      expect(base == base.copyWith(icon: replacement), isFalse);
      expect(base == base.copyWith(primaryAction: replacement), isFalse);
      expect(
        base == base.copyWith(atmosphere: const DovahAtmosphere()),
        isFalse,
      );
    });
  });
}
