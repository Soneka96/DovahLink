import 'package:flutter/rendering.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_clipper.dart';

/// Exercises [DovahPanelClipper]'s `getClip` and `shouldReclip` contracts.
void main() {
  group('Method getClip behaves correctly', () {
    test('Method getClip returns the path for the clipper\'s own geometry', () {
      const DovahPanelClipper clipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.singleBevel,
        cornerRadius: 0,
        cutSize: 10,
      );

      final Path clip = clipper.getClip(const Size(100, 100));

      expect(clip.contains(const Offset(50, 50)), isTrue);
      expect(clip.contains(const Offset(97, 3)), isFalse);
    });
  });

  group('Method shouldReclip behaves correctly', () {
    test('Method shouldReclip returns false when geometry is unchanged', () {
      const DovahPanelClipper clipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
        cutSize: 0,
      );
      const DovahPanelClipper sameClipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
        cutSize: 0,
      );

      expect(clipper.shouldReclip(sameClipper), isFalse);
    });

    test('Method shouldReclip returns true when cornerStyle differs', () {
      const DovahPanelClipper clipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
        cutSize: 0,
      );
      const DovahPanelClipper otherClipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.singleBevel,
        cornerRadius: 13,
        cutSize: 0,
      );

      expect(clipper.shouldReclip(otherClipper), isTrue);
    });

    test('Method shouldReclip returns true when cornerRadius differs', () {
      const DovahPanelClipper clipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
        cutSize: 0,
      );
      const DovahPanelClipper otherClipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 4,
        cutSize: 0,
      );

      expect(clipper.shouldReclip(otherClipper), isTrue);
    });

    test('Method shouldReclip returns true when cutSize differs', () {
      const DovahPanelClipper clipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.singleBevel,
        cornerRadius: 0,
        cutSize: 9,
      );
      const DovahPanelClipper otherClipper = DovahPanelClipper(
        cornerStyle: DovahPanelCornerStyle.singleBevel,
        cornerRadius: 0,
        cutSize: 12,
      );

      expect(clipper.shouldReclip(otherClipper), isTrue);
    });
  });
}
