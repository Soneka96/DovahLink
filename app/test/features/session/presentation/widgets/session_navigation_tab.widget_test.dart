import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_navigation_tab.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises the Session navigation tab's prototype hover treatment and callback.
void main() {
  final ThemeData theme = dovahThemeDataFor(DovahThemePreset.dovah);
  final DovahThemeTokens tokens = theme.extension<DovahThemeTokens>()!;
  final DovahSessionMetrics metrics = DovahSessionMetrics.forWindow(
    themeMetrics: theme.extension<DovahSessionThemeMetrics>()!,
    window: const Size(1280, 720),
  );

  Widget buildTab({required VoidCallback onTap}) => MaterialApp(
    theme: theme,
    home: Scaffold(
      body: Center(
        child: SessionNavigationTab(
          label: 'Map',
          selected: false,
          tokens: tokens,
          metrics: metrics,
          onTap: onTap,
        ),
      ),
    ),
  );

  testWidgets('SessionNavigationTab highlights its label while hovered', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(buildTab(onTap: () {}));
    final Finder label = find.text('Map');
    expect(tester.widget<Text>(label).style?.color, tokens.textMuted);

    final TestGesture mouse = await tester.createGesture(
      kind: PointerDeviceKind.mouse,
    );
    final Offset center = tester.getCenter(
      find.byKey(const Key('session-shell-Map-tab')),
    );
    await mouse.addPointer(location: center);
    await mouse.moveTo(center);
    await tester.pump();

    expect(tester.widget<Text>(label).style?.color, tokens.textPrimary);
    await mouse.removePointer();
    await tester.pump();
    expect(tester.widget<Text>(label).style?.color, tokens.textMuted);
  });

  testWidgets('SessionNavigationTab invokes its selection callback', (
    WidgetTester tester,
  ) async {
    int tapCount = 0;
    await tester.pumpWidget(buildTab(onTap: () => tapCount++));

    await tester.tap(find.byKey(const Key('session-shell-Map-tab')));

    expect(tapCount, 1);
  });

  for (final (DovahThemePreset preset, Size window, double visualHeight)
      in <(DovahThemePreset, Size, double)>[
        (DovahThemePreset.frostbound, const Size(1280, 720), 45),
        (DovahThemePreset.dovah, const Size(1280, 560), 45),
        (DovahThemePreset.hearth, const Size(900, 560), 45),
        (DovahThemePreset.dovah, const Size(1280, 720), 53),
      ]) {
    testWidgets(
      'SessionNavigationTab keeps its visual height and provides a 48px target under $preset at $window',
      (WidgetTester tester) async {
        final ThemeData presetTheme = dovahThemeDataFor(preset);
        final DovahThemeTokens presetTokens = presetTheme
            .extension<DovahThemeTokens>()!;
        final DovahSessionMetrics presetMetrics = DovahSessionMetrics.forWindow(
          themeMetrics: presetTheme.extension<DovahSessionThemeMetrics>()!,
          window: window,
        );
        int tapCount = 0;
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          setDovahTestWindow(tester, window);
          await tester.pumpWidget(
            MaterialApp(
              theme: presetTheme,
              home: Scaffold(
                body: Align(
                  alignment: Alignment.topLeft,
                  child: SizedBox(
                    height: presetMetrics.navTapTargetHeight,
                    child: SessionNavigationTab(
                      label: 'Map',
                      selected: true,
                      tokens: presetTokens,
                      metrics: presetMetrics,
                      onTap: () => tapCount++,
                    ),
                  ),
                ),
              ),
            ),
          );

          final Finder tab = find.byKey(const Key('session-shell-Map-tab'));
          final SemanticsNode semantics = tester.getSemantics(tab);
          expect(semantics.rect.height, greaterThanOrEqualTo(48));
          expect(tester.getSize(tab).height, presetMetrics.navTapTargetHeight);
          expect(presetMetrics.navHeight, visualHeight);

          await tester.tapAt(
            Offset(semantics.rect.center.dx, semantics.rect.bottom - 0.5),
          );
          expect(tapCount, 1);
        } finally {
          handle.dispose();
        }
      },
    );
  }
}
