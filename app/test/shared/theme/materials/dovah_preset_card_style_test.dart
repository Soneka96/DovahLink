import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preset_card_style.dart';

/// Exercises [DovahPresetCardStyle]'s value equality.
void main() {
  const DovahPresetCardStyle base = DovahPresetCardStyle(
    material: DovahMaterial(layers: [], borderColor: Color(0xFF65747D)),
    titleColor: Color(0xFFEDF3F6),
    summaryColor: Color(0xFFC0C7CA),
    detailColor: Color(0xFF929DA2),
    badgeFill: Color(0xFFA9C7D1),
    badgeForeground: Color(0xFF061014),
  );

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for identical styles', () {
      const DovahPresetCardStyle same = DovahPresetCardStyle(
        material: DovahMaterial(layers: [], borderColor: Color(0xFF65747D)),
        titleColor: Color(0xFFEDF3F6),
        summaryColor: Color(0xFFC0C7CA),
        detailColor: Color(0xFF929DA2),
        badgeFill: Color(0xFFA9C7D1),
        badgeForeground: Color(0xFF061014),
      );

      expect(base == same, isTrue);
      expect(base.hashCode, same.hashCode);
    });

    test('Behavior equality fails when any single part differs', () {
      const Color other = Color(0xFF000000);

      for (final DovahPresetCardStyle changed in const [
        DovahPresetCardStyle(
          material: DovahMaterial(layers: []),
          titleColor: Color(0xFFEDF3F6),
          summaryColor: Color(0xFFC0C7CA),
          detailColor: Color(0xFF929DA2),
          badgeFill: Color(0xFFA9C7D1),
          badgeForeground: Color(0xFF061014),
        ),
        DovahPresetCardStyle(
          material: DovahMaterial(layers: [], borderColor: Color(0xFF65747D)),
          titleColor: other,
          summaryColor: Color(0xFFC0C7CA),
          detailColor: Color(0xFF929DA2),
          badgeFill: Color(0xFFA9C7D1),
          badgeForeground: Color(0xFF061014),
        ),
        DovahPresetCardStyle(
          material: DovahMaterial(layers: [], borderColor: Color(0xFF65747D)),
          titleColor: Color(0xFFEDF3F6),
          summaryColor: other,
          detailColor: Color(0xFF929DA2),
          badgeFill: Color(0xFFA9C7D1),
          badgeForeground: Color(0xFF061014),
        ),
        DovahPresetCardStyle(
          material: DovahMaterial(layers: [], borderColor: Color(0xFF65747D)),
          titleColor: Color(0xFFEDF3F6),
          summaryColor: Color(0xFFC0C7CA),
          detailColor: other,
          badgeFill: Color(0xFFA9C7D1),
          badgeForeground: Color(0xFF061014),
        ),
        DovahPresetCardStyle(
          material: DovahMaterial(layers: [], borderColor: Color(0xFF65747D)),
          titleColor: Color(0xFFEDF3F6),
          summaryColor: Color(0xFFC0C7CA),
          detailColor: Color(0xFF929DA2),
          badgeFill: other,
          badgeForeground: Color(0xFF061014),
        ),
        DovahPresetCardStyle(
          material: DovahMaterial(layers: [], borderColor: Color(0xFF65747D)),
          titleColor: Color(0xFFEDF3F6),
          summaryColor: Color(0xFFC0C7CA),
          detailColor: Color(0xFF929DA2),
          badgeFill: Color(0xFFA9C7D1),
          badgeForeground: other,
        ),
      ]) {
        expect(base == changed, isFalse);
      }
    });
  });
}
