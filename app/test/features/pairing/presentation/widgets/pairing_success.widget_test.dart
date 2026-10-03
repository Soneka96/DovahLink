import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_success.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Pumps a [PairingSuccess], counting completions into [dones].
Future<void> pumpSuccess(
  WidgetTester tester, {
  List<int>? dones,
  DovahThemePreset preset = DovahThemePreset.dovah,
  Size size = const Size(900, 560),
}) => pumpDovahThemedWidget(
  tester,
  PairingSuccess(hostName: 'Bedroom PC', onDone: () => dones?.add(1)),
  preset: preset,
  size: size,
);

/// Exercises [PairingSuccess] copy, mark, action, and layout.
void main() {
  group('PairingSuccess displays', () {
    testWidgets('PairingSuccess displays the heading and names the paired Host', (
      WidgetTester tester,
    ) async {
      await pumpSuccess(tester);

      expect(find.text('You’re connected'), findsOneWidget);
      expect(
        (tester.widget<Text>(find.byKey(const Key('pairing-body'))).textSpan!
                as TextSpan)
            .toPlainText(),
        'Bedroom PC is ready. This device will reconnect automatically from now on.',
      );
    });

    testWidgets('PairingSuccess draws the success mark at its prototype size', (
      WidgetTester tester,
    ) async {
      await pumpSuccess(tester);

      expect(
        tester.getSize(find.byKey(const Key('pairing-success-mark'))),
        const Size.square(DovahDialogMetrics.successMarkSize),
      );
      expect(find.byIcon(Icons.check), findsOneWidget);
    });

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'PairingSuccess keeps the prototype fill and border while coloring its check for $preset',
        (WidgetTester tester) async {
          await pumpSuccess(tester, preset: preset);
          final Container mark = tester.widget(
            find.byKey(const Key('pairing-success-mark')),
          );
          final BoxDecoration decoration = mark.decoration! as BoxDecoration;
          final Border border = decoration.border! as Border;
          final Color success = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!.success;

          expect(
            decoration.color,
            DovahDialogMetrics.successMarkBaseColor.withValues(
              alpha: DovahDialogMetrics.statusMarkFillOpacity,
            ),
          );
          expect(
            border.top.color,
            DovahDialogMetrics.successMarkBaseColor.withValues(
              alpha: DovahDialogMetrics.statusMarkBorderOpacity,
            ),
          );
          expect(tester.widget<Icon>(find.byIcon(Icons.check)).color, success);
        },
      );
    }
  });

  group('PairingSuccess calls callbacks', () {
    testWidgets('PairingSuccess calls onDone when Done is tapped', (
      WidgetTester tester,
    ) async {
      final List<int> dones = [];
      await pumpSuccess(tester, dones: dones);

      await tester.tap(find.byKey(const Key('pairing-done-button')));
      await tester.pump();

      expect(dones, hasLength(1));
    });

    testWidgets('PairingSuccess does not call onDone until Done is tapped', (
      WidgetTester tester,
    ) async {
      final List<int> dones = [];
      await pumpSuccess(tester, dones: dones);

      expect(dones, isEmpty);
    });
  });

  group('PairingSuccess meets accessibility recommended guidelines', () {
    testWidgets('PairingSuccess hides its decorative check from semantics', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await pumpSuccess(tester);

        expect(find.bySemanticsLabel('You’re connected'), findsOneWidget);
        expect(
          find.descendant(
            of: find.byKey(const Key('pairing-success-mark')),
            matching: find.byType(Icon),
          ),
          findsOneWidget,
        );
        expect(
          tester
              .getSemantics(find.byKey(const Key('pairing-success-mark')))
              .label,
          isEmpty,
        );
      } finally {
        handle.dispose();
      }
    });
  });

  group('PairingSuccess lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'PairingSuccess renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpSuccess(tester, preset: preset, size: size);

            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });

  group('PairingSuccess keeps its mark the same at every window height', () {
    for (final Size size in dovahResponsiveTestSizes) {
      testWidgets(
        'PairingSuccess draws its 62 mark 17 above the heading at $size',
        (WidgetTester tester) async {
          await pumpSuccess(tester, size: size);

          final Rect mark = tester.getRect(
            find.byKey(const Key('pairing-success-mark')),
          );
          expect(mark.size, const Size.square(62));
          expect(
            tester.getTopLeft(find.byKey(const Key('pairing-heading'))).dy -
                mark.bottom,
            17,
          );
        },
      );

      testWidgets(
        'PairingSuccess puts the mark ${DovahDialogMetrics.successMarkTopGap} below its slot top at $size',
        (WidgetTester tester) async {
          await pumpSuccess(tester, size: size);

          final Finder mark = find.byKey(const Key('pairing-success-mark'));
          final Finder padding = find
              .ancestor(of: mark, matching: find.byType(Padding))
              .first;
          expect(
            tester.getTopLeft(mark).dy - tester.getTopLeft(padding).dy,
            DovahDialogMetrics.successMarkTopGap,
          );
        },
      );

      testWidgets(
        'PairingSuccess places its mark ${DovahDialogMetrics.successMarkTopGap}px below the content top at $size',
        (WidgetTester tester) async {
          await pumpSuccess(tester, size: size);

          final Finder mark = find.byKey(const Key('pairing-success-mark'));
          final Finder padding = find
              .ancestor(of: mark, matching: find.byType(Padding))
              .first;
          expect(
            tester.getTopLeft(mark).dy - tester.getTopLeft(padding).dy,
            DovahDialogMetrics.successMarkTopGap,
          );
        },
      );
    }
  });
}
