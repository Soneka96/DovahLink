import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_side_stack.widget.dart';

/// Builds the side stack with a fixed width and optional bounded height.
Widget buildSideStackWidget({
  required double gap,
  required Widget questPanel,
  required Widget statusPanel,
  double? height,
}) {
  final Widget sideStack = SessionOverviewSideStack(
    gap: gap,
    questPanel: questPanel,
    statusPanel: statusPanel,
  );
  final Widget sizedStack = SizedBox(
    width: 200,
    height: height,
    child: sideStack,
  );
  final Widget body = height == null
      ? UnconstrainedBox(alignment: Alignment.topLeft, child: sizedStack)
      : Center(child: sizedStack);
  return MaterialApp(home: Scaffold(body: body));
}

/// Verifies the Overview side stack's automatic-row height distribution.
void main() {
  group('SessionOverviewSideStackRenderBox distributes height', () {
    testWidgets(
      'SessionOverviewSideStackRenderBox shares extra height equally after intrinsic row heights',
      (WidgetTester tester) async {
        const double questContentHeight = 60;
        const double statusContentHeight = 100;
        const double gap = 14;
        const double stackHeight = 300;
        const double extraHeight =
            stackHeight - questContentHeight - gap - statusContentHeight;

        await tester.pumpWidget(
          buildSideStackWidget(
            gap: gap,
            height: stackHeight,
            questPanel: const SizedBox(
              key: Key('quest-row'),
              width: 80,
              height: questContentHeight,
            ),
            statusPanel: const SizedBox(
              key: Key('status-row'),
              width: 120,
              height: statusContentHeight,
            ),
          ),
        );

        final Rect stackRect = tester.getRect(
          find.byType(SessionOverviewSideStack),
        );
        final Rect questRect = tester.getRect(
          find.byKey(const Key('quest-row')),
        );
        final Rect statusRect = tester.getRect(
          find.byKey(const Key('status-row')),
        );
        final RenderBox stackRenderBox = tester.renderObject(
          find.byType(SessionOverviewSideStack),
        );
        final RenderBox questRenderBox = tester.renderObject(
          find.byKey(const Key('quest-row')),
        );
        final RenderBox statusRenderBox = tester.renderObject(
          find.byKey(const Key('status-row')),
        );

        expect(stackRect.height, stackHeight);
        expect(questRect.height, questContentHeight + extraHeight / 2);
        expect(statusRect.height, statusContentHeight + extraHeight / 2);
        expect(statusRect.top - questRect.bottom, gap);
        expect(statusRect.bottom, stackRect.bottom);
        expect(stackRenderBox.getMinIntrinsicWidth(stackHeight), 120);
        expect(stackRenderBox.getMaxIntrinsicWidth(stackHeight), 120);
        expect(
          stackRenderBox.getMinIntrinsicHeight(stackRect.width),
          questRenderBox.getMinIntrinsicHeight(stackRect.width) +
              gap +
              statusRenderBox.getMinIntrinsicHeight(stackRect.width),
        );
        expect(
          stackRenderBox.getDryLayout(
            BoxConstraints.tight(const Size(200, stackHeight)),
          ),
          const Size(200, stackHeight),
        );
        expect(
          stackRenderBox.getDryLayout(const BoxConstraints(maxWidth: 200)),
          const Size(200, 174),
        );
        expect(
          stackRenderBox.getDryLayout(
            const BoxConstraints(maxHeight: stackHeight),
          ),
          const Size(120, stackHeight),
        );
        expect(tester.takeException(), isNull);
      },
    );

    testWidgets(
      'SessionOverviewSideStackRenderBox keeps both rows within a short bounded height',
      (WidgetTester tester) async {
        const double questContentHeight = 60;
        const double statusContentHeight = 100;
        const double gap = 14;
        const double stackHeight = 150;

        await tester.pumpWidget(
          buildSideStackWidget(
            gap: gap,
            height: stackHeight,
            questPanel: const SizedBox(
              key: Key('quest-row'),
              width: 80,
              height: questContentHeight,
            ),
            statusPanel: const SizedBox(
              key: Key('status-row'),
              width: 120,
              height: statusContentHeight,
            ),
          ),
        );

        final Rect stackRect = tester.getRect(
          find.byType(SessionOverviewSideStack),
        );
        final Rect questRect = tester.getRect(
          find.byKey(const Key('quest-row')),
        );
        final Rect statusRect = tester.getRect(
          find.byKey(const Key('status-row')),
        );
        final RenderBox stackRenderBox = tester.renderObject(
          find.byType(SessionOverviewSideStack),
        );

        expect(stackRect.height, stackHeight);
        expect(questRect.height, 51);
        expect(statusRect.height, 85);
        expect(questRect.height + statusRect.height + gap, stackHeight);
        expect(questRect.top, stackRect.top);
        expect(statusRect.top - questRect.bottom, gap);
        expect(statusRect.bottom, stackRect.bottom);
        expect(
          stackRenderBox.getDryLayout(
            BoxConstraints.tight(const Size(200, stackHeight)),
          ),
          const Size(200, stackHeight),
        );
        expect(tester.takeException(), isNull);
      },
    );

    testWidgets(
      'SessionOverviewSideStackRenderBox lays out intrinsic height when height is unbounded',
      (WidgetTester tester) async {
        const double questContentHeight = 60;
        const double statusContentHeight = 100;
        const double gap = 14;
        const double stackHeight =
            questContentHeight + statusContentHeight + gap;

        await tester.pumpWidget(
          buildSideStackWidget(
            gap: gap,
            questPanel: const SizedBox(
              key: Key('quest-row'),
              height: questContentHeight,
            ),
            statusPanel: const SizedBox(
              key: Key('status-row'),
              height: statusContentHeight,
            ),
          ),
        );

        final Rect stackRect = tester.getRect(
          find.byType(SessionOverviewSideStack),
        );
        final Rect questRect = tester.getRect(
          find.byKey(const Key('quest-row')),
        );
        final Rect statusRect = tester.getRect(
          find.byKey(const Key('status-row')),
        );

        expect(stackRect.height, stackHeight);
        expect(questRect.height, questContentHeight);
        expect(statusRect.height, statusContentHeight);
        expect(statusRect.top - questRect.bottom, gap);
        expect(statusRect.bottom, stackRect.bottom);
        expect(tester.takeException(), isNull);
      },
    );

    testWidgets(
      'SessionOverviewSideStackRenderBox fits rows when the bound is shorter than the gap',
      (WidgetTester tester) async {
        const double gap = 14;
        const double stackHeight = 10;

        await tester.pumpWidget(
          buildSideStackWidget(
            gap: gap,
            height: stackHeight,
            questPanel: const SizedBox(key: Key('quest-row'), height: 60),
            statusPanel: const SizedBox(key: Key('status-row'), height: 100),
          ),
        );

        final Rect stackRect = tester.getRect(
          find.byType(SessionOverviewSideStack),
        );
        final Rect questRect = tester.getRect(
          find.byKey(const Key('quest-row')),
        );
        final Rect statusRect = tester.getRect(
          find.byKey(const Key('status-row')),
        );

        expect(stackRect.height, stackHeight);
        expect(questRect.height, 0);
        expect(questRect.top, stackRect.top);
        expect(statusRect.height, 0);
        expect(statusRect.top, stackRect.bottom);
        expect(tester.takeException(), isNull);
      },
    );

    testWidgets(
      'SessionOverviewSideStackRenderBox splits available height when both intrinsic rows are empty',
      (WidgetTester tester) async {
        const double gap = 10;
        const double stackHeight = 100;
        const double rowHeight = (stackHeight - gap) / 2;

        await tester.pumpWidget(
          buildSideStackWidget(
            gap: gap,
            height: stackHeight,
            questPanel: const SizedBox(key: Key('quest-row')),
            statusPanel: const SizedBox(key: Key('status-row')),
          ),
        );

        final Rect stackRect = tester.getRect(
          find.byType(SessionOverviewSideStack),
        );
        final Rect questRect = tester.getRect(
          find.byKey(const Key('quest-row')),
        );
        final Rect statusRect = tester.getRect(
          find.byKey(const Key('status-row')),
        );

        expect(stackRect.height, stackHeight);
        expect(questRect.height, rowHeight);
        expect(statusRect.height, rowHeight);
        expect(statusRect.top - questRect.bottom, gap);
        expect(statusRect.bottom, stackRect.bottom);
        expect(tester.takeException(), isNull);
      },
    );
  });
}
