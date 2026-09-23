import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Two representative landscape constraint sizes every shared DovahLink widget test renders
/// under, per `ai/context/flutter/testing.md`'s multi-size expectation for this theme system.
const List<Size> dovahTestSizes = [Size(900, 560), Size(1280, 720)];

/// Pumps [child] inside a [MaterialApp] using [preset]'s theme at [size], the standard harness
/// every shared DovahLink widget test uses to prove multi-theme, multi-size rendering.
Future<void> pumpDovahThemedWidget(
  WidgetTester tester,
  Widget child, {
  required DovahThemePreset preset,
  required Size size,
}) async {
  await tester.binding.setSurfaceSize(size);
  addTearDown(() => tester.binding.setSurfaceSize(null));
  await tester.pumpWidget(
    MaterialApp(
      theme: dovahThemeDataFor(preset),
      home: Scaffold(body: child),
    ),
  );
  await tester.pumpAndSettle();
}
