import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_side_stack.widget.dart';
import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_side_stack_render_box.dart';

/// Exercises the Overview side-stack widget's render-object wiring.
void main() {
  testWidgets('SessionOverviewSideStack updates the render-object gap', (
    WidgetTester tester,
  ) async {
    late StateSetter setSideStack;
    double gap = 14;

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Center(
            child: SizedBox(
              width: 200,
              height: 300,
              child: StatefulBuilder(
                builder: (BuildContext context, StateSetter setState) {
                  setSideStack = setState;
                  return SessionOverviewSideStack(
                    gap: gap,
                    questPanel: const SizedBox(
                      key: Key('quest-row'),
                      height: 60,
                    ),
                    statusPanel: const SizedBox(
                      key: Key('status-row'),
                      height: 100,
                    ),
                  );
                },
              ),
            ),
          ),
        ),
      ),
    );

    final SessionOverviewSideStackRenderBox renderBox = tester.renderObject(
      find.byType(SessionOverviewSideStack),
    );
    final Rect initialQuestRect = tester.getRect(
      find.byKey(const Key('quest-row')),
    );
    final Rect initialStatusRect = tester.getRect(
      find.byKey(const Key('status-row')),
    );
    expect(renderBox.gap, 14);
    expect(initialStatusRect.top - initialQuestRect.bottom, 14);

    setSideStack(() => gap = 10);
    await tester.pump();

    final Rect updatedQuestRect = tester.getRect(
      find.byKey(const Key('quest-row')),
    );
    final Rect updatedStatusRect = tester.getRect(
      find.byKey(const Key('status-row')),
    );
    final Rect stackRect = tester.getRect(
      find.byType(SessionOverviewSideStack),
    );
    expect(renderBox.gap, 10);
    expect(updatedStatusRect.top - updatedQuestRect.bottom, 10);
    expect(updatedQuestRect.height, initialQuestRect.height + 2);
    expect(updatedStatusRect.height, initialStatusRect.height + 2);
    expect(updatedStatusRect.bottom, stackRect.bottom);
    expect(tester.takeException(), isNull);
  });
}
