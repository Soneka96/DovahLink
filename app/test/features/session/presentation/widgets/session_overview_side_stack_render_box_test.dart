import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_side_stack.widget.dart';

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
          MaterialApp(
            home: Scaffold(
              body: Center(
                child: SizedBox(
                  width: 200,
                  height: stackHeight,
                  child: SessionOverviewSideStack(
                    gap: gap,
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
                ),
              ),
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
  });
}
