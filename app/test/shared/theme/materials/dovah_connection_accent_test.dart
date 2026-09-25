import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_connection_accent.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';

/// Exercises [DovahConnectionAccent]'s defaults, emptiness, and equality.
void main() {
  const DovahLinearLayer layer = DovahLinearLayer(
    angleDegrees: 90,
    colors: [Color(0xFF000000), Color(0xFFFFFFFF)],
    stops: [0, 1],
  );

  group('Behavior construction behaves correctly', () {
    test('Behavior construction defaults every part to none', () {
      const DovahConnectionAccent accent = DovahConnectionAccent();

      expect(accent.overlayLayers, isEmpty);
      expect(accent.overlayOpacity, isA<double>());
      expect(accent.overlayOpacity, 1);
      expect(accent.linkLayer, isNull);
      expect(accent.linkOpacity, isA<double>());
      expect(accent.linkOpacity, 1);
      expect(accent.cornerOutline, isNull);
      expect(accent.availableEdge, isNull);
      expect(accent.restingBorder, isNull);
      expect(accent.overContent, isFalse);
    });

    test('Behavior construction makes none equal to a default accent', () {
      expect(DovahConnectionAccent.none == const DovahConnectionAccent(), true);
    });
  });

  group('Property drawsNothing behaves correctly', () {
    test('Property drawsNothing is true for none', () {
      expect(DovahConnectionAccent.none.drawsNothing, isA<bool>());
      expect(DovahConnectionAccent.none.drawsNothing, true);
    });

    test(
      'Property drawsNothing ignores a resting border, which draws nothing',
      () {
        expect(
          const DovahConnectionAccent(
            restingBorder: Color(0xFF79542F),
          ).drawsNothing,
          true,
        );
      },
    );

    test('Property drawsNothing is false for every drawn part', () {
      for (final DovahConnectionAccent accent in const [
        DovahConnectionAccent(overlayLayers: [layer]),
        DovahConnectionAccent(linkLayer: layer),
        DovahConnectionAccent(cornerOutline: Color(0x14A9C9D8)),
        DovahConnectionAccent(availableEdge: Color(0xFF86B4C7)),
      ]) {
        expect(accent.drawsNothing, false);
      }
    });
  });

  group('Behavior equality behaves correctly', () {
    const DovahConnectionAccent base = DovahConnectionAccent(
      overlayLayers: [layer],
      overlayOpacity: 0.8,
      linkLayer: layer,
      linkOpacity: 0.6,
      cornerOutline: Color(0x14A9C9D8),
      availableEdge: Color(0xFF86B4C7),
      restingBorder: Color(0xFF79542F),
      overContent: true,
    );

    test('Behavior equality holds for identical accents', () {
      const DovahConnectionAccent same = DovahConnectionAccent(
        overlayLayers: [layer],
        overlayOpacity: 0.8,
        linkLayer: layer,
        linkOpacity: 0.6,
        cornerOutline: Color(0x14A9C9D8),
        availableEdge: Color(0xFF86B4C7),
        restingBorder: Color(0xFF79542F),
        overContent: true,
      );

      expect(base == same, true);
      expect(base.hashCode, same.hashCode);
    });

    test('Behavior equality fails when any single part differs', () {
      for (final DovahConnectionAccent other in const [
        DovahConnectionAccent(),
        DovahConnectionAccent(
          overlayLayers: [],
          overlayOpacity: 0.8,
          linkLayer: layer,
          linkOpacity: 0.6,
          cornerOutline: Color(0x14A9C9D8),
          availableEdge: Color(0xFF86B4C7),
          restingBorder: Color(0xFF79542F),
          overContent: true,
        ),
        DovahConnectionAccent(
          overlayLayers: [layer],
          overlayOpacity: 0.5,
          linkLayer: layer,
          linkOpacity: 0.6,
          cornerOutline: Color(0x14A9C9D8),
          availableEdge: Color(0xFF86B4C7),
          restingBorder: Color(0xFF79542F),
          overContent: true,
        ),
        DovahConnectionAccent(
          overlayLayers: [layer],
          overlayOpacity: 0.8,
          linkOpacity: 0.6,
          cornerOutline: Color(0x14A9C9D8),
          availableEdge: Color(0xFF86B4C7),
          restingBorder: Color(0xFF79542F),
          overContent: true,
        ),
        DovahConnectionAccent(
          overlayLayers: [layer],
          overlayOpacity: 0.8,
          linkLayer: layer,
          linkOpacity: 0.6,
          availableEdge: Color(0xFF86B4C7),
          restingBorder: Color(0xFF79542F),
          overContent: true,
        ),
        DovahConnectionAccent(
          overlayLayers: [layer],
          overlayOpacity: 0.8,
          linkLayer: layer,
          linkOpacity: 0.6,
          cornerOutline: Color(0x14A9C9D8),
          restingBorder: Color(0xFF79542F),
          overContent: true,
        ),
        DovahConnectionAccent(
          overlayLayers: [layer],
          overlayOpacity: 0.8,
          linkLayer: layer,
          linkOpacity: 0.6,
          cornerOutline: Color(0x14A9C9D8),
          availableEdge: Color(0xFF86B4C7),
          overContent: true,
        ),
        DovahConnectionAccent(
          overlayLayers: [layer],
          overlayOpacity: 0.8,
          linkLayer: layer,
          linkOpacity: 0.6,
          cornerOutline: Color(0x14A9C9D8),
          availableEdge: Color(0xFF86B4C7),
          restingBorder: Color(0xFF79542F),
        ),
      ]) {
        expect(base == other, false);
      }
    });
  });
}
