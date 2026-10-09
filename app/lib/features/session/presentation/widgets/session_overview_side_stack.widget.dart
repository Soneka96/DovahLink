import 'package:flutter/widgets.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_side_stack_render_box.dart';

/// Lays out the Overview's Quest and Current Status panels like the prototype's automatic Grid rows.
class SessionOverviewSideStack extends MultiChildRenderObjectWidget {
  /// Creates the side stack with Quest first and Current Status second.
  SessionOverviewSideStack({
    required this.questPanel,
    required this.statusPanel,
    required this.gap,
    super.key,
  }) : super(children: <Widget>[questPanel, statusPanel]);

  /// The Quest panel that occupies the first automatic row.
  final Widget questPanel;

  /// The Current Status panel that occupies the second automatic row.
  final Widget statusPanel;

  /// The prototype's gap between the two rows.
  final double gap;

  /// Creates the render object that shares extra height between both rows.
  ///
  /// [context] provides the app environment for render-object creation.
  @override
  SessionOverviewSideStackRenderBox createRenderObject(BuildContext context) =>
      SessionOverviewSideStackRenderBox(gap: gap);

  /// Updates [renderObject] with this widget's current gap.
  ///
  /// [context] provides the app environment for the update.
  /// [renderObject] is the render object created for this widget.
  @override
  void updateRenderObject(
    BuildContext context,
    covariant SessionOverviewSideStackRenderBox renderObject,
  ) {
    renderObject.gap = gap;
  }
}
