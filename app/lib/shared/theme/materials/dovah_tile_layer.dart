import 'dart:collection';
import 'dart:typed_data';
import 'dart:ui';

import 'package:flutter/foundation.dart' show visibleForTesting;
import 'package:flutter/widgets.dart' show Matrix4;

import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// A fixed-size pattern repeated across a surface (the prototype's `background-size` tiles, such as
/// a 90px lattice of ember specks or a 17x13 paper grain). [content] is laid out once inside a tile
/// of [tileSize], so its geometry resolves against the tile, not the surface. The tile is rasterized
/// at [resolutionScale] times its logical size and drawn back down, which keeps one-pixel details
/// smooth at 1x and crisp on denser displays.
class DovahTileLayer extends DovahMaterialLayer {
  /// How many times denser than logical pixels the tile is rasterized.
  static const int resolutionScale = 2;

  /// Maximum number of rasterized tile images retained for reuse.
  static const int _maximumCachedTiles = 32;

  /// Rasterized tile images, ordered from least to most recently used.
  static final LinkedHashMap<DovahTileLayer, Image> _rasterizedTiles =
      LinkedHashMap<DovahTileLayer, Image>();

  /// Number of tile images rasterized since the cache was initialized.
  static int _tileRasterizationCount = 0;

  /// The size of one tile, in whole logical pixels.
  final Size tileSize;

  /// The pattern painted into each tile.
  final DovahMaterialLayer content;

  /// Creates a tile layer that repeats [content] in tiles of [tileSize].
  const DovahTileLayer({required this.tileSize, required this.content});

  /// The number of tile images currently held by the bounded raster cache.
  @visibleForTesting
  static int get cachedTileCount => _rasterizedTiles.length;

  /// Number of rasterizations performed by the tile cache.
  @visibleForTesting
  static int get tileRasterizationCount => _tileRasterizationCount;

  /// See [DovahMaterialLayer.createShader].
  @override
  Shader createShader(Size size) {
    final int width = (tileSize.width * resolutionScale).round();
    final int height = (tileSize.height * resolutionScale).round();
    final Image? cachedTile = _rasterizedTiles.remove(this);
    final Image tile;
    if (cachedTile != null) {
      tile = cachedTile;
    } else {
      final PictureRecorder recorder = PictureRecorder();
      final Canvas canvas = Canvas(recorder)
        ..scale(resolutionScale.toDouble(), resolutionScale.toDouble());
      canvas.drawRect(
        Offset.zero & tileSize,
        Paint()..shader = content.createShader(tileSize),
      );
      final Picture picture = recorder.endRecording();
      try {
        tile = picture.toImageSync(width, height);
        _tileRasterizationCount++;
      } finally {
        picture.dispose();
      }
    }

    _rasterizedTiles[this] = tile;
    if (_rasterizedTiles.length > _maximumCachedTiles) {
      final DovahTileLayer oldestTile = _rasterizedTiles.keys.first;
      _rasterizedTiles.remove(oldestTile)!.dispose();
    }

    // The cache keeps the image handle for reuse; shaders retain their own pixel reference.
    final Float64List transform = Matrix4.diagonal3Values(
      1 / resolutionScale,
      1 / resolutionScale,
      1,
    ).storage;
    final ImageShader shader = ImageShader(
      tile,
      TileMode.repeated,
      TileMode.repeated,
      transform,
      filterQuality: FilterQuality.medium,
    );
    return shader;
  }

  /// The fields that define this layer's value equality.
  @override
  List<Object?> get props => [tileSize, content];
}
