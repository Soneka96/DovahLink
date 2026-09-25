import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_hover_lift.widget.dart';
import 'dovah_widget_test_helpers.dart';

/// The lifted box every test hovers.
const Key boxKey = Key('lifted-box');

/// Pumps a [DovahHoverLift] around a 100x40 box, recording each hovered state it builds.
Future<TestGesture> pumpLift(
  WidgetTester tester, {
  required bool enabled,
  required Offset offset,
  required List<bool> builtStates,
  bool disableAnimations = false,
}) async {
  setDovahTestWindow(tester, dovahTestSizes.first);
  await tester.pumpWidget(
    MaterialApp(
      theme: dovahThemeDataFor(DovahThemePreset.dovah),
      home: MediaQuery(
        data: MediaQueryData(disableAnimations: disableAnimations),
        child: Scaffold(
          body: Align(
            alignment: Alignment.topLeft,
            child: Padding(
              padding: const EdgeInsets.all(50),
              child: DovahHoverLift(
                enabled: enabled,
                offset: offset,
                builder: (BuildContext context, bool hovered) {
                  builtStates.add(hovered);
                  return const SizedBox(key: boxKey, width: 100, height: 40);
                },
              ),
            ),
          ),
        ),
      ),
    ),
  );
  final TestGesture pointer = await tester.createGesture(
    kind: PointerDeviceKind.mouse,
  );
  await pointer.addPointer(location: const Offset(700, 500));
  await tester.pump();
  return pointer;
}

/// Exercises [DovahHoverLift]'s slide, hover state, and reduced-motion behavior.
void main() {
  group('DovahHoverLift slides its child', () {
    testWidgets('DovahHoverLift keeps its child in place until hovered', (
      WidgetTester tester,
    ) async {
      final List<bool> states = <bool>[];
      await pumpLift(
        tester,
        enabled: true,
        offset: const Offset(5, 0),
        builtStates: states,
      );

      expect(tester.getTopLeft(find.byKey(boxKey)), const Offset(50, 50));
      expect(states.last, isFalse);
    });

    testWidgets('DovahHoverLift slides its child by the offset when hovered', (
      WidgetTester tester,
    ) async {
      final List<bool> states = <bool>[];
      final TestGesture pointer = await pumpLift(
        tester,
        enabled: true,
        offset: const Offset(5, -2),
        builtStates: states,
      );

      await pointer.moveTo(tester.getCenter(find.byKey(boxKey)));
      await tester.pump();
      await tester.pump(DovahControlMetrics.liftDuration);

      expect(tester.getTopLeft(find.byKey(boxKey)), const Offset(55, 48));
      expect(states.last, isTrue);
      await pointer.removePointer();
    });

    testWidgets('DovahHoverLift eases the slide instead of jumping', (
      WidgetTester tester,
    ) async {
      final List<bool> states = <bool>[];
      final TestGesture pointer = await pumpLift(
        tester,
        enabled: true,
        offset: const Offset(10, 0),
        builtStates: states,
      );

      await pointer.moveTo(tester.getCenter(find.byKey(boxKey)));
      await tester.pump();
      await tester.pump(DovahControlMetrics.liftDuration ~/ 2);

      final double left = tester.getTopLeft(find.byKey(boxKey)).dx;
      expect(left, greaterThan(50));
      expect(left, lessThan(60));
      await pointer.removePointer();
    });

    testWidgets('DovahHoverLift returns the child when the pointer leaves', (
      WidgetTester tester,
    ) async {
      final List<bool> states = <bool>[];
      final TestGesture pointer = await pumpLift(
        tester,
        enabled: true,
        offset: const Offset(5, 0),
        builtStates: states,
      );
      await pointer.moveTo(tester.getCenter(find.byKey(boxKey)));
      await tester.pump(DovahControlMetrics.liftDuration);

      await pointer.moveTo(const Offset(700, 500));
      await tester.pump();
      await tester.pump(DovahControlMetrics.liftDuration);

      expect(tester.getTopLeft(find.byKey(boxKey)), const Offset(50, 50));
      expect(states.last, isFalse);
      await pointer.removePointer();
    });

    testWidgets('DovahHoverLift does nothing while disabled', (
      WidgetTester tester,
    ) async {
      final List<bool> states = <bool>[];
      final TestGesture pointer = await pumpLift(
        tester,
        enabled: false,
        offset: const Offset(5, 0),
        builtStates: states,
      );

      await pointer.moveTo(tester.getCenter(find.byKey(boxKey)));
      await tester.pump(DovahControlMetrics.liftDuration);

      expect(tester.getTopLeft(find.byKey(boxKey)), const Offset(50, 50));
      expect(states, isNot(contains(true)));
      await pointer.removePointer();
    });

    testWidgets(
      'DovahHoverLift applies its slide at once with reduced motion',
      (WidgetTester tester) async {
        final List<bool> states = <bool>[];
        final TestGesture pointer = await pumpLift(
          tester,
          enabled: true,
          offset: const Offset(5, 0),
          builtStates: states,
          disableAnimations: true,
        );

        await pointer.moveTo(tester.getCenter(find.byKey(boxKey)));
        await tester.pump();
        await tester.pump();

        expect(tester.getTopLeft(find.byKey(boxKey)), const Offset(55, 50));
        expect(states.last, isTrue);
        await pointer.removePointer();
      },
    );

    testWidgets(
      'DovahHoverLift settles back when it is disabled while hovered',
      (WidgetTester tester) async {
        setDovahTestWindow(tester, dovahTestSizes.first);
        bool enabled = true;
        late StateSetter update;
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: Scaffold(
              body: Align(
                alignment: Alignment.topLeft,
                child: Padding(
                  padding: const EdgeInsets.all(50),
                  child: StatefulBuilder(
                    builder: (BuildContext context, StateSetter setState) {
                      update = setState;
                      return DovahHoverLift(
                        enabled: enabled,
                        offset: const Offset(5, -2),
                        builder: (BuildContext context, bool hovered) =>
                            const SizedBox(key: boxKey, width: 100, height: 40),
                      );
                    },
                  ),
                ),
              ),
            ),
          ),
        );
        final TestGesture pointer = await tester.createGesture(
          kind: PointerDeviceKind.mouse,
        );
        await pointer.addPointer(location: const Offset(700, 500));
        await tester.pump();
        await pointer.moveTo(tester.getCenter(find.byKey(boxKey)));
        await tester.pump();
        await tester.pump(DovahControlMetrics.liftDuration);
        expect(tester.getTopLeft(find.byKey(boxKey)), const Offset(55, 48));

        update(() => enabled = false);
        await tester.pump();
        await tester.pump(DovahControlMetrics.liftDuration);

        expect(tester.getTopLeft(find.byKey(boxKey)), const Offset(50, 50));
        await pointer.removePointer();
      },
    );
  });
}
