import 'dart:typed_data';
import 'dart:ui';

import 'package:flutter/rendering.dart' show CustomPainter;

import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// Paints [layer] over an origin-anchored canvas of [size] and returns its pixels in row-major
/// order: index `y * width + x` is the straight-alpha color at that pixel. It must run inside
/// `WidgetTester.runAsync`, because image encoding completes outside the test's fake clock.
Future<List<Color>> paintDovahLayer(DovahMaterialLayer layer, Size size) async {
  final PictureRecorder recorder = PictureRecorder();
  final Canvas canvas = Canvas(recorder);
  canvas.drawRect(
    Offset.zero & size,
    Paint()..shader = layer.createShader(size),
  );
  final Image image = await recorder.endRecording().toImage(
    size.width.toInt(),
    size.height.toInt(),
  );
  final ByteData data = (await image.toByteData(
    format: ImageByteFormat.rawStraightRgba,
  ))!;
  image.dispose();

  return [
    for (int index = 0; index < data.lengthInBytes; index += 4)
      Color.fromARGB(
        data.getUint8(index + 3),
        data.getUint8(index),
        data.getUint8(index + 1),
        data.getUint8(index + 2),
      ),
  ];
}

/// Returns the 8-bit alpha channel of [color].
int alphaOf(Color color) => (color.a * 255).round();

/// Returns the 8-bit red channel of [color].
int redOf(Color color) => (color.r * 255).round();

/// Returns the 8-bit blue channel of [color].
int blueOf(Color color) => (color.b * 255).round();

/// Paints [painter] for a surface of [size] whose top-left corner sits [margin] logical pixels
/// inside an otherwise transparent image, and returns the image's pixels in row-major order like
/// [paintDovahLayer]. The margin lets a test see paint that falls outside the surface, such as its
/// shadow. It must run inside `WidgetTester.runAsync`.
Future<List<Color>> paintDovahPainter(
  CustomPainter painter,
  Size size, {
  double margin = 0,
}) async {
  final PictureRecorder recorder = PictureRecorder();
  final Canvas canvas = Canvas(recorder);
  canvas.translate(margin, margin);
  painter.paint(canvas, size);
  final int width = (size.width + margin * 2).toInt();
  final int height = (size.height + margin * 2).toInt();
  final Image image = await recorder.endRecording().toImage(width, height);
  final ByteData data = (await image.toByteData(
    format: ImageByteFormat.rawStraightRgba,
  ))!;
  image.dispose();

  return [
    for (int index = 0; index < data.lengthInBytes; index += 4)
      Color.fromARGB(
        data.getUint8(index + 3),
        data.getUint8(index),
        data.getUint8(index + 1),
        data.getUint8(index + 2),
      ),
  ];
}
