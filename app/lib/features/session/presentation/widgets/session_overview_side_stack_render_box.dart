import 'dart:math' as math;

import 'package:flutter/rendering.dart';

/// Measures and lays out the two automatic rows in the Session Overview side stack.
class SessionOverviewSideStackRenderBox extends RenderBox
    with
        ContainerRenderObjectMixin<RenderBox, MultiChildLayoutParentData>,
        RenderBoxContainerDefaultsMixin<RenderBox, MultiChildLayoutParentData> {
  /// Creates the row layout with the prototype's inter-row [gap].
  SessionOverviewSideStackRenderBox({required double gap}) : _gap = gap;

  /// The fixed space between the Quest and Current Status rows.
  double get gap => _gap;

  /// The stored inter-row spacing.
  double _gap;

  /// Updates the inter-row gap to [value] and invalidates layout when it changes.
  set gap(double value) {
    if (_gap == value) {
      return;
    }
    _gap = value;
    markNeedsLayout();
  }

  /// Installs linked layout data for [child] when it does not already have it.
  ///
  /// [child] is one of the two side-stack panel render objects.
  @override
  void setupParentData(RenderBox child) {
    if (child.parentData is! MultiChildLayoutParentData) {
      child.parentData = MultiChildLayoutParentData();
    }
  }

  /// Returns the minimum intrinsic width needed by either panel at [height].
  ///
  /// [height] is the available height for the side stack.
  @override
  double computeMinIntrinsicWidth(double height) {
    final RenderBox quest = firstChild!;
    final RenderBox status = childAfter(quest)!;
    return math.max(
      quest.getMinIntrinsicWidth(height),
      status.getMinIntrinsicWidth(height),
    );
  }

  /// Returns the maximum intrinsic width needed by either panel at [height].
  ///
  /// [height] is the available height for the side stack.
  @override
  double computeMaxIntrinsicWidth(double height) {
    final RenderBox quest = firstChild!;
    final RenderBox status = childAfter(quest)!;
    return math.max(
      quest.getMaxIntrinsicWidth(height),
      status.getMaxIntrinsicWidth(height),
    );
  }

  /// Returns both rows' minimum intrinsic heights plus their fixed gap at [width].
  ///
  /// [width] is the available width for each panel.
  @override
  double computeMinIntrinsicHeight(double width) {
    final RenderBox quest = firstChild!;
    final RenderBox status = childAfter(quest)!;
    return quest.getMinIntrinsicHeight(width) +
        gap +
        status.getMinIntrinsicHeight(width);
  }

  /// Returns both rows' maximum intrinsic heights plus their fixed gap at [width].
  ///
  /// [width] is the available width for each panel.
  @override
  double computeMaxIntrinsicHeight(double width) {
    final RenderBox quest = firstChild!;
    final RenderBox status = childAfter(quest)!;
    return quest.getMaxIntrinsicHeight(width) +
        gap +
        status.getMaxIntrinsicHeight(width);
  }

  /// Calculates the side stack's dry size from intrinsic row heights and [constraints].
  ///
  /// [constraints] are the parent's width and height limits.
  @override
  Size computeDryLayout(BoxConstraints constraints) {
    final double width = constraints.hasBoundedWidth
        ? constraints.maxWidth
        : constraints.constrainWidth(computeMaxIntrinsicWidth(double.infinity));
    final double intrinsicHeight = computeMaxIntrinsicHeight(width);
    final double height = constraints.hasBoundedHeight
        ? constraints.maxHeight
        : constraints.constrainHeight(intrinsicHeight);

    return constraints.constrain(Size(width, height));
  }

  /// Fits both panels to the available height, sharing extra space equally and compressing them
  /// proportionally when their intrinsic heights do not fit.
  @override
  void performLayout() {
    assert(childCount == 2);

    final RenderBox quest = firstChild!;
    final RenderBox status = childAfter(quest)!;
    final double width = constraints.hasBoundedWidth
        ? constraints.maxWidth
        : constraints.constrainWidth(computeMaxIntrinsicWidth(double.infinity));
    final double questMinimum = quest.getMaxIntrinsicHeight(width);
    final double statusMinimum = status.getMaxIntrinsicHeight(width);
    final double intrinsicHeight = questMinimum + gap + statusMinimum;
    final double height = constraints.hasBoundedHeight
        ? constraints.maxHeight
        : constraints.constrainHeight(intrinsicHeight);
    final double layoutGap = math.min(gap, height);
    final double availableRowsHeight = height - layoutGap;
    final double intrinsicRowsHeight = questMinimum + statusMinimum;
    final double questHeight;
    final double statusHeight;
    if (availableRowsHeight < intrinsicRowsHeight && intrinsicRowsHeight > 0) {
      questHeight = availableRowsHeight * questMinimum / intrinsicRowsHeight;
      statusHeight = availableRowsHeight - questHeight;
    } else {
      final double extraPerRow =
          (availableRowsHeight - intrinsicRowsHeight) / 2;
      questHeight = questMinimum + extraPerRow;
      statusHeight = statusMinimum + extraPerRow;
    }

    quest.layout(
      BoxConstraints.tightFor(width: width, height: questHeight),
      parentUsesSize: true,
    );
    status.layout(
      BoxConstraints.tightFor(width: width, height: statusHeight),
      parentUsesSize: true,
    );
    size = constraints.constrain(Size(width, height));

    final MultiChildLayoutParentData questParentData =
        quest.parentData! as MultiChildLayoutParentData;
    final MultiChildLayoutParentData statusParentData =
        status.parentData! as MultiChildLayoutParentData;
    questParentData.offset = Offset.zero;
    statusParentData.offset = Offset(0, questHeight + layoutGap);
  }

  /// Paints both side-stack rows at [offset].
  ///
  /// [context] is the painting context for this frame.
  /// [offset] is this layout's origin in the painting context.
  @override
  void paint(PaintingContext context, Offset offset) {
    defaultPaint(context, offset);
  }

  /// Hit-tests the visible Quest and Current Status row content at [position].
  ///
  /// [result] receives the hit path.
  /// [position] is the point to test in this layout's local coordinates.
  @override
  bool hitTestChildren(BoxHitTestResult result, {required Offset position}) =>
      defaultHitTestChildren(result, position: position);
}
