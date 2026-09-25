import 'dart:ui';

import 'package:equatable/equatable.dart';

/// One paint layer of a themed material: a semantic, immutable description of a gradient-based
/// texture that a painter fills a surface with. Layers carry the approved prototype's texture
/// recipe so no feature widget ever knows what a scratch, a fibre, or a stain is.
abstract class DovahMaterialLayer extends Equatable {
  /// Creates a layer.
  const DovahMaterialLayer();

  /// Builds the shader that fills a rectangle of [size] anchored at the origin. A layer resolves
  /// its own geometry (angles, ellipses, periods) against [size], so one layer serves every
  /// surface size.
  Shader createShader(Size size);
}
