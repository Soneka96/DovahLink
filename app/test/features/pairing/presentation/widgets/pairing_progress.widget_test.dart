import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_loading.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_progress.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// The phases [PairingProgress] presents.
const List<PairingPhase> progressPhases = [
  PairingPhase.none,
  PairingPhase.connecting,
  PairingPhase.disconnected,
  PairingPhase.requestingCode,
  PairingPhase.confirming,
];

/// Pumps a [PairingProgress] for [phase], counting closes into [closes]. Pumps a single frame
/// rather than settling, because the spinner animates forever.
Future<void> pumpProgress(
  WidgetTester tester, {
  required PairingPhase phase,
  List<int>? closes,
  DovahThemePreset preset = DovahThemePreset.dovah,
  Size size = const Size(900, 560),
}) async {
  setDovahTestWindow(tester, size);
  await tester.pumpWidget(
    MaterialApp(
      theme: dovahThemeDataFor(preset),
      home: Scaffold(
        body: PairingProgress(
          phase: phase,
          hostName: 'Bedroom PC',
          onClose: () => closes?.add(1),
        ),
      ),
    ),
  );
}

/// Exercises [PairingProgress] copy, spinner, close action, and accessibility.
void main() {
  group('PairingProgress displays', () {
    testWidgets(
      'PairingProgress displays the connecting copy before and while connecting',
      (WidgetTester tester) async {
        for (final PairingPhase phase in [
          PairingPhase.none,
          PairingPhase.connecting,
        ]) {
          await pumpProgress(tester, phase: phase);

          expect(find.text('Connecting'), findsOneWidget);
          expect(find.text('Connecting…'), findsOneWidget);
        }
      },
    );

    testWidgets('PairingProgress displays the requesting-code copy', (
      WidgetTester tester,
    ) async {
      await pumpProgress(tester, phase: PairingPhase.requestingCode);

      expect(find.text('Requesting a code'), findsOneWidget);
      expect(find.text('Requesting code…'), findsOneWidget);
    });

    testWidgets(
      'PairingProgress displays the requesting-code copy for an unpaired session whose request is starting',
      (WidgetTester tester) async {
        await pumpProgress(tester, phase: PairingPhase.unpaired);

        expect(find.text('Requesting a code'), findsOneWidget);
        expect(find.text('Requesting code…'), findsOneWidget);
        expect(find.byKey(const Key('pairing-close-button')), findsNothing);
      },
    );

    testWidgets('PairingProgress displays the confirming copy', (
      WidgetTester tester,
    ) async {
      await pumpProgress(tester, phase: PairingPhase.confirming);

      expect(find.text('Confirming'), findsOneWidget);
      expect(find.text('Confirming…'), findsOneWidget);
    });

    testWidgets(
      'PairingProgress displays the offline copy naming the Host while waiting for it',
      (WidgetTester tester) async {
        await pumpProgress(tester, phase: PairingPhase.disconnected);

        expect(find.text('Bedroom PC is offline'), findsOneWidget);
        expect(find.textContaining('Start Skyrim'), findsOneWidget);
        expect(find.text('Waiting for Skyrim…'), findsOneWidget);
      },
    );

    testWidgets('PairingProgress shows a spinner in every phase', (
      WidgetTester tester,
    ) async {
      for (final PairingPhase phase in progressPhases) {
        await pumpProgress(tester, phase: phase);

        expect(find.byType(PairingLoadingIndicator), findsOneWidget);
      }
    });
  });

  group('PairingProgress calls callbacks', () {
    testWidgets(
      'PairingProgress offers a close button only while waiting for the Host',
      (WidgetTester tester) async {
        for (final PairingPhase phase in progressPhases) {
          await pumpProgress(tester, phase: phase);

          expect(
            find.byKey(const Key('pairing-close-button')),
            phase == PairingPhase.disconnected ? findsOneWidget : findsNothing,
            reason: '$phase',
          );
        }
      },
    );

    testWidgets('PairingProgress calls onClose when Close is tapped', (
      WidgetTester tester,
    ) async {
      final List<int> closes = [];
      await pumpProgress(
        tester,
        phase: PairingPhase.disconnected,
        closes: closes,
      );

      await tester.tap(find.byKey(const Key('pairing-close-button')));
      await tester.pump();

      expect(closes, hasLength(1));
    });
  });

  group('PairingProgress meets accessibility recommended guidelines', () {
    testWidgets('PairingProgress announces its status as a live region', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await pumpProgress(tester, phase: PairingPhase.requestingCode);

        expect(
          tester.getSemantics(find.byKey(const Key('pairing-status'))),
          isSemantics(label: 'Requesting code…', isLiveRegion: true),
        );
      } finally {
        handle.dispose();
      }
    });
  });

  group('PairingProgress lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'PairingProgress renders every phase under $preset at $size without overflow',
          (WidgetTester tester) async {
            for (final PairingPhase phase in progressPhases) {
              await pumpProgress(
                tester,
                phase: phase,
                preset: preset,
                size: size,
              );

              expect(tester.takeException(), isNull, reason: '$phase');
            }
          },
        );
      }
    }
  });

  group('PairingProgress sizes its parts for the window height', () {
    for (final Size size in dovahResponsiveTestSizes) {
      final bool isCompact = size.height <= 620;
      testWidgets(
        'PairingProgress draws a ${isCompact ? 42 : 54} mark and spaces Close ${isCompact ? 7 : 18} below the status at $size',
        (WidgetTester tester) async {
          await pumpProgress(
            tester,
            phase: PairingPhase.disconnected,
            size: size,
          );

          expect(
            tester.getSize(find.byType(PairingMark)),
            Size.square(isCompact ? 42 : 54),
          );
          expect(
            tester
                    .getTopLeft(find.byKey(const Key('pairing-close-button')))
                    .dy -
                tester
                    .getBottomLeft(find.byKey(const Key('pairing-status')))
                    .dy,
            isCompact ? 7 : 18,
          );
        },
      );
    }
  });
}
