import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_back_button.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises the Session Shell's Connections back action.
void main() {
  group('SessionBackButton matches prototype geometry', () {
    testWidgets('SessionBackButton keeps its style and accessible target', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        SessionBackButton(onPressed: () {}),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );

      final Text label = tester.widget(find.text('Connections'));
      final Icon icon = tester.widget(find.byType(Icon));
      final Padding padding = tester.widget(
        find
            .ancestor(
              of: find.text('Connections'),
              matching: find.byType(Padding),
            )
            .first,
      );

      expect(label.style?.fontSize, DovahSessionMetrics.backFontSize);
      expect(icon.size, DovahSessionMetrics.backIconSize);
      expect(padding.padding, const EdgeInsets.fromLTRB(0, 10, 8, 10));
      expect(
        tester.getSize(find.byType(SessionBackButton)).height,
        greaterThanOrEqualTo(48),
      );
    });
  });

  group('SessionBackButton hover treatment', () {
    testWidgets('SessionBackButton brightens its label while hovered', (
      WidgetTester tester,
    ) async {
      final ThemeData theme = dovahThemeDataFor(DovahThemePreset.dovah);
      final DovahThemeTokens tokens = theme.extension<DovahThemeTokens>()!;
      await tester.pumpWidget(
        MaterialApp(
          theme: theme,
          home: const Scaffold(body: SessionBackButton(onPressed: _noop)),
        ),
      );

      final TestGesture mouse = await tester.createGesture(
        kind: PointerDeviceKind.mouse,
      );
      final Offset center = tester.getCenter(find.byType(SessionBackButton));
      await mouse.addPointer(location: center);
      await mouse.moveTo(center);
      await tester.pumpAndSettle();

      expect(
        tester.widget<Text>(find.text('Connections')).style?.color,
        tokens.textPrimary,
      );
      final Finder backInkWell = find.descendant(
        of: find.byType(SessionBackButton),
        matching: find.byType(InkWell),
      );
      expect(
        tester.widget<InkWell>(backInkWell).splashFactory,
        NoSplash.splashFactory,
      );
      await mouse.removePointer();
      await tester.pumpAndSettle();

      expect(
        tester.widget<Text>(find.text('Connections')).style?.color,
        tokens.textMuted,
      );
      expect(tester.widget<Icon>(find.byType(Icon)).color, tokens.textMuted);
    });
  });

  group('SessionBackButton keyboard focus', () {
    testWidgets('SessionBackButton keeps a visible focus ring and tap action', (
      WidgetTester tester,
    ) async {
      int tapCount = 0;
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: Scaffold(
              body: SessionBackButton(onPressed: () => tapCount++),
            ),
          ),
        );

        expect(
          tester.getSemantics(find.byKey(const Key('dovah-button-semantics'))),
          isSemantics(label: 'Connections', isButton: true, hasTapAction: true),
        );
        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();
        expect(find.byKey(DovahFocusRing.ringKey), findsOneWidget);

        await tester.sendKeyEvent(LogicalKeyboardKey.enter);
        expect(tapCount, 1);
        await tester.sendKeyEvent(LogicalKeyboardKey.space);
        expect(tapCount, 2);

        await tester.tap(find.byType(SessionBackButton));
        expect(tapCount, 3);
      } finally {
        handle.dispose();
      }
    });
  });
}

/// Supplies a no-op callback for a hover-only test.
void _noop() {}
