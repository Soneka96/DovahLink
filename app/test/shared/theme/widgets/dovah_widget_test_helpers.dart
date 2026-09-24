import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Two representative landscape constraint sizes every shared DovahLink widget test renders
/// under, per `ai/context/flutter/testing.md`'s multi-size expectation for this theme system.
const List<Size> dovahTestSizes = [Size(900, 560), Size(1280, 720)];

/// The landscape window sizes responsive dialog content is proven at: two at or below the
/// compact-height breakpoint and two above it.
const List<Size> dovahResponsiveTestSizes = [
  Size(720, 480),
  Size(900, 560),
  Size(1280, 720),
  Size(1600, 900),
];

/// Sets the test view, and so `MediaQuery`, to [size] until the test ends. The device pixel
/// ratio is left at the test default so density-dependent guidelines behave as before.
void setDovahTestWindow(WidgetTester tester, Size size) {
  tester.view.physicalSize = size * tester.view.devicePixelRatio;
  addTearDown(tester.view.reset);
}

/// Pumps [child] inside a [MaterialApp] using [preset]'s theme at [size], the standard harness
/// every shared DovahLink widget test uses to prove multi-theme, multi-size rendering.
Future<void> pumpDovahThemedWidget(
  WidgetTester tester,
  Widget child, {
  required DovahThemePreset preset,
  required Size size,
}) async {
  setDovahTestWindow(tester, size);
  await tester.pumpWidget(
    MaterialApp(
      theme: dovahThemeDataFor(preset),
      home: Scaffold(body: child),
    ),
  );
  await tester.pumpAndSettle();
}
