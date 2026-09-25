import 'dart:ui';

import 'package:flutter/painting.dart' show BoxShadow;

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';

/// Exercises [DovahMaterial]'s defaults and equality.
void main() {
  const DovahLinearLayer base = DovahLinearLayer(
    angleDegrees: 145,
    colors: [Color(0xFF11181C), Color(0xFF040708)],
    stops: [0, 1],
  );

  group('Behavior construction behaves correctly', () {
    test('Behavior construction defaults to no edges, border, or shadow', () {
      const DovahMaterial material = DovahMaterial(layers: [base]);

      expect(material.layers, [base]);
      expect(material.topEdgeHighlight, isNull);
      expect(material.bottomEdgeShade, isNull);
      expect(material.borderColor, isNull);
      expect(material.shadow, isEmpty);
    });
  });

  group('Behavior equality behaves correctly', () {
    const DovahMaterial material = DovahMaterial(
      layers: [base],
      topEdgeHighlight: Color(0x21DCEBF0),
      borderColor: Color(0xFF71808A),
      shadow: [BoxShadow(color: Color(0x80000000), blurRadius: 32)],
    );

    test('Behavior equality holds for identical materials', () {
      const DovahMaterial same = DovahMaterial(
        layers: [base],
        topEdgeHighlight: Color(0x21DCEBF0),
        borderColor: Color(0xFF71808A),
        shadow: [BoxShadow(color: Color(0x80000000), blurRadius: 32)],
      );

      expect(material == same, isTrue);
      expect(material.hashCode, same.hashCode);
    });

    test('Behavior equality fails when the layers differ', () {
      const DovahMaterial other = DovahMaterial(
        layers: [],
        topEdgeHighlight: Color(0x21DCEBF0),
        borderColor: Color(0xFF71808A),
        shadow: [BoxShadow(color: Color(0x80000000), blurRadius: 32)],
      );

      expect(material == other, isFalse);
    });

    test('Behavior equality fails when the top edge differs', () {
      const DovahMaterial other = DovahMaterial(
        layers: [base],
        borderColor: Color(0xFF71808A),
        shadow: [BoxShadow(color: Color(0x80000000), blurRadius: 32)],
      );

      expect(material == other, isFalse);
    });

    test('Behavior equality fails when the bottom edge differs', () {
      const DovahMaterial other = DovahMaterial(
        layers: [base],
        topEdgeHighlight: Color(0x21DCEBF0),
        bottomEdgeShade: Color(0xB8000000),
        borderColor: Color(0xFF71808A),
        shadow: [BoxShadow(color: Color(0x80000000), blurRadius: 32)],
      );

      expect(material == other, isFalse);
    });

    test('Behavior equality fails when the border differs', () {
      const DovahMaterial other = DovahMaterial(
        layers: [base],
        topEdgeHighlight: Color(0x21DCEBF0),
        shadow: [BoxShadow(color: Color(0x80000000), blurRadius: 32)],
      );

      expect(material == other, isFalse);
    });

    test('Behavior equality fails when the shadow differs', () {
      const DovahMaterial other = DovahMaterial(
        layers: [base],
        topEdgeHighlight: Color(0x21DCEBF0),
        borderColor: Color(0xFF71808A),
      );

      expect(material == other, isFalse);
    });
  });
}
