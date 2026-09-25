import 'package:flutter/rendering.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_geometry.dart';

/// Exercises [buildDovahPanelPath] for every [DovahPanelCornerStyle].
void main() {
  group('Method buildDovahPanelPath behaves correctly', () {
    test(
      'Method buildDovahPanelPath cuts only the top-right corner for singleBevel',
      () {
        final Path path = buildDovahPanelPath(
          const Size(100, 100),
          cornerStyle: DovahPanelCornerStyle.singleBevel,
          cornerRadius: 0,
          cutSize: 10,
        );

        expect(path.contains(const Offset(50, 50)), isTrue);
        expect(path.contains(const Offset(1, 1)), isTrue);
        expect(path.contains(const Offset(1, 99)), isTrue);
        expect(path.contains(const Offset(99, 99)), isTrue);
        expect(path.contains(const Offset(97, 3)), isFalse);
      },
    );

    test(
      'Method buildDovahPanelPath cuts opposite corners for doubleBevel',
      () {
        final Path path = buildDovahPanelPath(
          const Size(100, 100),
          cornerStyle: DovahPanelCornerStyle.doubleBevel,
          cornerRadius: 0,
          cutSize: 10,
        );

        expect(path.contains(const Offset(1, 1)), isTrue);
        expect(path.contains(const Offset(99, 99)), isTrue);
        expect(path.contains(const Offset(97, 3)), isFalse);
        expect(path.contains(const Offset(3, 97)), isFalse);
      },
    );

    test(
      'Method buildDovahPanelPath keeps bevelled styles sharp whatever the radius',
      () {
        for (final DovahPanelCornerStyle style in const [
          DovahPanelCornerStyle.singleBevel,
          DovahPanelCornerStyle.doubleBevel,
        ]) {
          final Path path = buildDovahPanelPath(
            const Size(100, 100),
            cornerStyle: style,
            cornerRadius: 20,
            cutSize: 10,
          );

          expect(path.contains(const Offset(0.5, 0.5)), isTrue);
          expect(path.contains(const Offset(99.5, 99.5)), isTrue);
        }
      },
    );

    test(
      'Method buildDovahPanelPath excludes near-corner points for rounded',
      () {
        final Path path = buildDovahPanelPath(
          const Size(100, 100),
          cornerStyle: DovahPanelCornerStyle.rounded,
          cornerRadius: 20,
          cutSize: 0,
        );

        expect(path.contains(const Offset(50, 50)), isTrue);
        expect(path.contains(const Offset(1, 1)), isFalse);
      },
    );

    test(
      'Method buildDovahPanelPath clamps an oversized cut to half the shorter side',
      () {
        final Path path = buildDovahPanelPath(
          const Size(20, 20),
          cornerStyle: DovahPanelCornerStyle.doubleBevel,
          cornerRadius: 0,
          cutSize: 200,
        );

        expect(() => path.getBounds(), returnsNormally);
        final Rect bounds = path.getBounds();
        expect(bounds.width, lessThanOrEqualTo(20));
        expect(bounds.height, lessThanOrEqualTo(20));
      },
    );
  });
}
