import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_brand_mark_treatment.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_connection_accent.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preset_card_style.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_scene.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_radial_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_stripe_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_tile_layer.dart';

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

    test('Property frostbound keeps the prototype atmosphere', () {
      final DovahAtmosphere atmosphere =
          DovahThemeMaterials.frostbound.atmosphere;

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
      final DovahAtmosphere atmosphere = DovahThemeMaterials.dovah.atmosphere;

      expect(atmosphere.imageAssetPath, isNull);
      expect(atmosphere.imageFilter.isNeutral, isTrue);
      expect(atmosphere.layers, hasLength(3));
      expect(atmosphere.hazeLayers, hasLength(2));
      expect(atmosphere.hazeLayers.every((l) => l is DovahTileLayer), isTrue);
      expect(atmosphere.hazeOpacity, isA<double>());
      expect(atmosphere.hazeOpacity, 0.32);
    });

    test('Property hearth keeps the prototype atmosphere', () {
      final DovahAtmosphere atmosphere = DovahThemeMaterials.hearth.atmosphere;

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

    test('Property backdrop keeps the prototype modal backdrop per preset', () {
      expect(
        DovahThemeMaterials.frostbound.backdrop,
        const DovahBackdrop(
          tint: Color.fromRGBO(0, 2, 4, 0.78),
          blurSigma: 7,
          saturation: 0.72,
        ),
      );
      expect(
        DovahThemeMaterials.dovah.backdrop,
        const DovahBackdrop(tint: Color.fromRGBO(2, 4, 7, 0.76), blurSigma: 8),
      );
      expect(
        DovahThemeMaterials.hearth.backdrop,
        const DovahBackdrop(
          tint: Color.fromRGBO(47, 31, 18, 0.54),
          blurSigma: 9,
          sepia: 0.12,
        ),
      );
    });

    test('Property frostbound keeps the prototype preview scene', () {
      final DovahPreviewScene scene =
          DovahThemeMaterials.frostbound.previewScene;

      expect(scene.imageAssetPath, frostboundEnvironmentAsset);
      expect(
        scene.imageFilter,
        const DovahColorFilter(grayscale: 0.35, saturate: 0.55, contrast: 1.15),
      );
      expect(scene.layers, hasLength(2));
      expect(scene.sigil.shape, DovahPreviewSigilShape.square);
      expect(scene.sigil.fill, const Color(0xFF090E12));
      expect(scene.sigil.markFilter, const DovahColorFilter(grayscale: 0.72));
      expect(scene.barEdgeColor, const Color(0xFFA43B40));
      expect(scene.barCornerRadius, 0);
    });

    test(
      'Property dovah keeps the prototype preview scene with its hero art',
      () {
        final DovahPreviewScene scene = DovahThemeMaterials.dovah.previewScene;

        expect(scene.imageAssetPath, dovahConnectionHeroAsset);
        expect(scene.imageFilter.isNeutral, isTrue);
        expect(scene.layers, hasLength(2));
        expect(scene.sigil.shape, DovahPreviewSigilShape.diamond);
        expect(scene.sigil.border, const Color(0xFFD49A55));
        expect(scene.sigil.markFilter.isNeutral, isTrue);
        expect(scene.barEdgeColor, isNull);
      },
    );

    test('Property hearth keeps the prototype preview scene', () {
      final DovahPreviewScene scene = DovahThemeMaterials.hearth.previewScene;

      expect(scene.imageAssetPath, hearthEnvironmentAsset);
      expect(
        scene.imageFilter,
        const DovahColorFilter(saturate: 0.92, contrast: 1.05),
      );
      expect(scene.layers, hasLength(2));
      expect(scene.sigil.shape, DovahPreviewSigilShape.circle);
      expect(
        scene.sigil.markFilter,
        const DovahColorFilter(
          sepia: 0.38,
          hueRotateDegrees: 345,
          saturate: 0.75,
        ),
      );
      expect(scene.barCornerRadius, isA<double>());
      expect(scene.barCornerRadius, 4);
    });

    test(
      'Property the frostbound and hearth scenes reuse their canvas environment image',
      () {
        expect(
          DovahThemeMaterials.frostbound.previewScene.imageAssetPath,
          DovahThemeMaterials.frostbound.atmosphere.imageAssetPath,
        );
        expect(
          DovahThemeMaterials.hearth.previewScene.imageAssetPath,
          DovahThemeMaterials.hearth.atmosphere.imageAssetPath,
        );
      },
    );

    test('Property every preview layer of every preset builds a shader', () {
      for (final DovahThemeMaterials materials in presetMaterials) {
        for (final DovahMaterialLayer layer in [
          ...materials.previewScene.layers,
          materials.previewScene.barFill,
        ]) {
          expect(
            () => layer.createShader(const Size(240, 112)),
            returnsNormally,
            reason: 'preview layer $layer',
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

  group('Property preset card recipes behave correctly', () {
    test('Property frostbound keeps the prototype .preset-frostbound card', () {
      final DovahPresetCardStyle card =
          DovahThemeMaterials.frostbound.presetCard;
      final DovahLinearLayer fracture =
          card.material.layers.last as DovahLinearLayer;

      expect(card.material.layers, hasLength(3));
      expect(countLayers<DovahLinearLayer>(card.material), 2);
      expect(countLayers<DovahStripeLayer>(card.material), 1);
      expect(fracture.angleDegrees, 116);
      expect(fracture.stops, [0, 0.19, 0.1915, 0.1935, 0.197]);
      expect(card.material.borderColor, const Color(0xFF65747D));
      expect(card.material.shadow, isEmpty);
      expect(card.material.topEdgeHighlight, isNull);
      expect(card.titleColor, const Color(0xFFEDF3F6));
      expect(card.summaryColor, const Color(0xFFC0C7CA));
      expect(card.detailColor, const Color(0xFF929DA2));
      expect(card.badgeFill, const Color(0xFFA9C7D1));
      expect(card.badgeForeground, const Color(0xFF061014));
    });

    test('Property dovah keeps the prototype .preset-dovah card', () {
      final DovahPresetCardStyle card = DovahThemeMaterials.dovah.presetCard;

      expect(card.material.layers, hasLength(2));
      expect(countLayers<DovahStripeLayer>(card.material), 1);
      expect(card.material.borderColor, const Color(0xFF45667E));
      expect(card.material.shadow, isEmpty);
      expect(card.titleColor, const Color(0xFFF1F6F9));
      expect(card.summaryColor, const Color(0xFFB1C2CD));
      expect(card.detailColor, const Color(0xFF7892A2));
      expect(card.badgeFill, const Color(0xFF8ED6FF));
      expect(card.badgeForeground, const Color(0xFF071015));
    });

    test('Property hearth keeps the prototype .preset-hearth card', () {
      final DovahPresetCardStyle card = DovahThemeMaterials.hearth.presetCard;

      expect(card.material.layers, hasLength(3));
      expect(countLayers<DovahRadialLayer>(card.material), 1);
      expect(card.material.borderColor, const Color(0xFF8D6336));
      expect(card.material.shadow, isEmpty);
      expect(card.titleColor, const Color(0xFF271B12));
      expect(card.summaryColor, const Color(0xFF4F3A28));
      expect(card.detailColor, const Color(0xFF6D5035));
      expect(card.badgeFill, const Color(0xFF965923));
      expect(card.badgeForeground, const Color(0xFFFFF9EE));
    });

    test(
      'Property preset card materials differ from each theme panel material',
      () {
        for (final DovahThemeMaterials materials in presetMaterials) {
          expect(materials.presetCard.material, isNot(materials.surface));
        }
      },
    );
  });

  group('Property brand mark recipes behave correctly', () {
    test('Property frostbound keeps the prototype brand mark treatment', () {
      final DovahBrandMarkTreatment mark =
          DovahThemeMaterials.frostbound.brandMark;

      expect(mark.filter.grayscale, 0.65);
      expect(mark.filter.contrast, 1.25);
      expect(mark.glowColor, const Color.fromRGBO(154, 201, 220, 0.17));
      expect(mark.glowBlurRadius, 8);
      expect(mark.backingColor, isNull);
    });

    test('Property dovah keeps the prototype brand mark treatment', () {
      final DovahBrandMarkTreatment mark = DovahThemeMaterials.dovah.brandMark;

      expect(mark.filter.isNeutral, isTrue);
      expect(mark.glowColor, const Color.fromRGBO(116, 189, 232, 0.22));
      expect(mark.glowBlurRadius, 15);
      expect(mark.backingColor, isNull);
    });

    test('Property hearth keeps the prototype brand mark treatment', () {
      final DovahBrandMarkTreatment mark = DovahThemeMaterials.hearth.brandMark;

      expect(mark.filter.sepia, 0.4);
      expect(mark.filter.hueRotateDegrees, 345);
      expect(mark.filter.saturate, 0.78);
      expect(mark.glowColor, isNull);
      expect(mark.backingColor, const Color.fromRGBO(255, 248, 230, 0.3));
    });
  });

  group('Property connection accent recipes behave correctly', () {
    test('Property frostbound keeps the prototype connection decoration', () {
      final DovahConnectionAccent accent =
          DovahThemeMaterials.frostbound.connectionAccent;
      final DovahLinearLayer fracture =
          accent.overlayLayers.single as DovahLinearLayer;

      expect(fracture.angleDegrees, 118);
      expect(fracture.stops, [0, 0.17, 0.173, 0.177, 0.63, 0.632, 0.636]);
      expect(fracture.colors[2], const Color.fromRGBO(206, 229, 237, 0.07));
      expect(fracture.colors[5], const Color.fromRGBO(206, 229, 237, 0.05));
      expect(accent.overlayOpacity, 0.8);
      expect(accent.linkLayer, isNull);
      expect(accent.cornerOutline, const Color.fromRGBO(169, 201, 216, 0.08));
      expect(accent.availableEdge, const Color(0xFF86B4C7));
      expect(accent.restingBorder, isNull);
      expect(accent.overContent, isTrue);
    });

    test('Property dovah keeps the prototype connection decoration', () {
      final DovahConnectionAccent accent =
          DovahThemeMaterials.dovah.connectionAccent;
      final DovahLinearLayer link = accent.linkLayer! as DovahLinearLayer;

      expect(link.angleDegrees, 90);
      expect(link.stops, [0, 0.25, 0.73, 1]);
      expect(link.colors, const [
        Color.fromRGBO(226, 165, 94, 0.75),
        Color.fromRGBO(226, 165, 94, 0.08),
        Color.fromRGBO(116, 189, 232, 0.08),
        Color.fromRGBO(116, 189, 232, 0.75),
      ]);
      expect(accent.linkOpacity, 0.6);
      expect(accent.overlayLayers, isEmpty);
      expect(accent.cornerOutline, const Color.fromRGBO(169, 201, 216, 0.08));
      expect(accent.availableEdge, isNull);
      expect(accent.restingBorder, isNull);
      expect(accent.overContent, isFalse);
    });

    test('Property hearth keeps only the darker resting border', () {
      final DovahConnectionAccent accent =
          DovahThemeMaterials.hearth.connectionAccent;

      expect(accent.restingBorder, const Color(0xFF79542F));
      expect(accent.drawsNothing, isTrue);
    });
  });

  group('Behavior distinct presets behaves correctly', () {
    test('Behavior distinct presets never share a preview scene', () {
      expect(
        DovahThemeMaterials.frostbound.previewScene,
        isNot(DovahThemeMaterials.dovah.previewScene),
      );
      expect(
        DovahThemeMaterials.dovah.previewScene,
        isNot(DovahThemeMaterials.hearth.previewScene),
      );
      expect(
        DovahThemeMaterials.frostbound.previewScene,
        isNot(DovahThemeMaterials.hearth.previewScene),
      );
    });

    test(
      'Behavior distinct presets use three different preview scene images',
      () {
        expect({
          for (final DovahThemeMaterials materials in presetMaterials)
            materials.previewScene.imageAssetPath,
        }, hasLength(3));
      },
    );

    test('Behavior distinct presets never share a backdrop', () {
      expect(
        DovahThemeMaterials.frostbound.backdrop,
        isNot(DovahThemeMaterials.dovah.backdrop),
      );
      expect(
        DovahThemeMaterials.dovah.backdrop,
        isNot(DovahThemeMaterials.hearth.backdrop),
      );
      expect(
        DovahThemeMaterials.frostbound.backdrop,
        isNot(DovahThemeMaterials.hearth.backdrop),
      );
    });

    test('Behavior distinct presets never share an atmosphere', () {
      expect(
        DovahThemeMaterials.frostbound.atmosphere,
        isNot(DovahThemeMaterials.dovah.atmosphere),
      );
      expect(
        DovahThemeMaterials.dovah.atmosphere,
        isNot(DovahThemeMaterials.hearth.atmosphere),
      );
      expect(
        DovahThemeMaterials.frostbound.atmosphere,
        isNot(DovahThemeMaterials.hearth.atmosphere),
      );
    });

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

    test('Method copyWith replaces only the preset card', () {
      final DovahPresetCardStyle other = DovahThemeMaterials.hearth.presetCard;
      final DovahThemeMaterials copy = base.copyWith(presetCard: other);

      expect(copy.presetCard, other);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith replaces only the brand mark', () {
      const DovahBrandMarkTreatment plain = DovahBrandMarkTreatment();
      final DovahThemeMaterials copy = base.copyWith(brandMark: plain);

      expect(copy.brandMark, plain);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith replaces only the connection accent', () {
      const DovahConnectionAccent plain = DovahConnectionAccent();
      final DovahThemeMaterials copy = base.copyWith(connectionAccent: plain);

      expect(copy.connectionAccent, plain);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith replaces only the atmosphere', () {
      const DovahAtmosphere plain = DovahAtmosphere();
      final DovahThemeMaterials copy = base.copyWith(atmosphere: plain);

      expect(copy.atmosphere, plain);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith replaces only the preview scene', () {
      final DovahPreviewScene other = DovahThemeMaterials.hearth.previewScene;
      final DovahThemeMaterials copy = base.copyWith(previewScene: other);

      expect(copy.previewScene, other);
      expect(copy.surface, base.surface);
    });

    test('Method copyWith replaces only the backdrop', () {
      const DovahBackdrop plain = DovahBackdrop(
        tint: Color(0x00000000),
        blurSigma: 1,
      );
      final DovahThemeMaterials copy = base.copyWith(backdrop: plain);

      expect(copy.backdrop, plain);
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
      expect(
        base == base.copyWith(atmosphere: const DovahAtmosphere()),
        isFalse,
      );
      expect(
        base == base.copyWith(connectionAccent: DovahConnectionAccent.none),
        isFalse,
      );
      expect(
        base == base.copyWith(brandMark: const DovahBrandMarkTreatment()),
        isFalse,
      );
      expect(
        base ==
            base.copyWith(presetCard: DovahThemeMaterials.hearth.presetCard),
        isFalse,
      );
      expect(
        base ==
            base.copyWith(
              previewScene: DovahThemeMaterials.hearth.previewScene,
            ),
        isFalse,
      );
      expect(
        base ==
            base.copyWith(
              backdrop: const DovahBackdrop(
                tint: Color(0x00000000),
                blurSigma: 1,
              ),
            ),
        isFalse,
      );
    });
  });
}
