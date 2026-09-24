import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_loading.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises PairingLoadingIndicator rendering.
void main() {
  group('PairingLoadingIndicator', () {
    testWidgets(
      'PairingLoadingIndicator renders a progress indicator keyed pairing-loading',
      (WidgetTester tester) async {
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: const PairingLoadingIndicator(),
          ),
        );

        expect(find.byKey(const Key('pairing-loading')), findsOneWidget);
        expect(find.byType(CircularProgressIndicator), findsOneWidget);
      },
    );
  });
}
