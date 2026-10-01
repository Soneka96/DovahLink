import 'dart:ui' show PointerDeviceKind;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/widgets/discover_candidate_card.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises [DiscoverCandidateCard]'s visual states, selection, and semantics.
void main() {
  group('DiscoverCandidateCard renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'DiscoverCandidateCard renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const DiscoverCandidateCard(
                title: 'Local Host',
                subtitle: 'DovahLink · Ready to connect',
                onTap: ignoreCandidate,
              ),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(find.text('Local Host'), findsOneWidget);
            expect(find.text('DovahLink · Ready to connect'), findsOneWidget);
          },
        );
      }
    }
  });

  group('DiscoverCandidateCard selects its candidate', () {
    testWidgets(
      'DiscoverCandidateCard disables selection while real authentication is checking',
      (WidgetTester tester) async {
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
          await tester.pumpWidget(
            MaterialApp(
              theme: dovahThemeDataFor(DovahThemePreset.dovah),
              home: const Scaffold(
                body: DiscoverCandidateCard(
                  title: 'Local Host',
                  subtitle: 'DovahLink · Ready to connect',
                  isChecking: true,
                  onTap: null,
                ),
              ),
            ),
          );

          expect(find.text('Checking trusted connection…'), findsOneWidget);
          expect(
            find.byKey(const Key('candidate-checking-spinner')),
            findsOneWidget,
          );
          expect(
            tester.getSemantics(
              find.bySemanticsLabel('Local Host, Checking trusted connection…'),
            ),
            isSemantics(
              label: 'Local Host, Checking trusted connection…',
              isButton: true,
              isEnabled: false,
              hasTapAction: false,
            ),
          );
        } finally {
          semantics.dispose();
        }
      },
    );

    testWidgets('DiscoverCandidateCard calls onTap when tapped', (
      WidgetTester tester,
    ) async {
      int calls = 0;
      await pumpDovahThemedWidget(
        tester,
        DiscoverCandidateCard(
          title: 'Local Host',
          subtitle: 'DovahLink · Ready to connect',
          onTap: () => calls++,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahResponsiveTestSizes.last,
      );

      await tester.tap(find.byType(DiscoverCandidateCard));
      await tester.pump();

      expect(calls, 1);
    });

    testWidgets('DiscoverCandidateCard activates with the keyboard', (
      WidgetTester tester,
    ) async {
      int calls = 0;
      await pumpDovahThemedWidget(
        tester,
        DiscoverCandidateCard(
          title: 'Local Host',
          subtitle: 'DovahLink · Ready to connect',
          onTap: () => calls++,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahResponsiveTestSizes.last,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.pump();

      expect(calls, 1);
    });

    testWidgets('DiscoverCandidateCard exposes one labeled 48dp tap target', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        await pumpDovahThemedWidget(
          tester,
          const DiscoverCandidateCard(
            title: 'Local Host',
            subtitle: 'DovahLink · Ready to connect',
            onTap: ignoreCandidate,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahResponsiveTestSizes.last,
        );

        final Size target = tester.getSize(find.byType(DiscoverCandidateCard));
        expect(
          target.height,
          greaterThanOrEqualTo(DovahControlMetrics.minimumTapTargetSize),
        );
        expect(
          tester.getSemantics(
            find.bySemanticsLabel('Local Host, DovahLink · Ready to connect'),
          ),
          isSemantics(
            label: 'Local Host, DovahLink · Ready to connect',
            isButton: true,
            isEnabled: true,
            hasTapAction: true,
          ),
        );
      } finally {
        semantics.dispose();
      }
    });

    testWidgets(
      'DiscoverCandidateCard takes the raised material while hovered',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DiscoverCandidateCard(
            title: 'Local Host',
            subtitle: 'DovahLink · Ready to connect',
            onTap: ignoreCandidate,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahResponsiveTestSizes.last,
        );
        final Finder surface = find.byType(DovahSurface).first;
        final TestGesture pointer = await tester.createGesture(
          kind: PointerDeviceKind.mouse,
        );
        await pointer.addPointer(location: tester.getCenter(surface));
        await tester.pump();

        expect(
          tester.widget<DovahSurface>(surface).role,
          DovahMaterialRole.raised,
        );
        await pointer.removePointer();
      },
    );
  });

  group('DiscoverCandidateCard follows prototype geometry', () {
    testWidgets('DiscoverCandidateCard keeps the candidate spacing and type', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DiscoverCandidateCard(
          title: 'Local Host',
          subtitle: 'DovahLink · Ready to connect',
          onTap: ignoreCandidate,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahResponsiveTestSizes.last,
      );

      final Text title = tester.widget(find.text('Local Host'));
      final Text subtitle = tester.widget(
        find.text('DovahLink · Ready to connect'),
      );
      final DovahSurface surface = tester.widget(
        find.byType(DovahSurface).first,
      );

      expect(
        title.style?.fontSize,
        DovahDialogMetrics.discoveryCandidateTitleFontSize,
      );
      expect(
        subtitle.style?.fontSize,
        DovahDialogMetrics.discoveryCandidateSubtitleFontSize,
      );
      expect(
        surface.padding,
        const EdgeInsets.all(DovahDialogMetrics.discoveryCandidateCardPadding),
      );
      expect(find.text('›'), findsOneWidget);
    });
  });
}

/// Ignores selection when rendering is the behavior under test.
void ignoreCandidate() {}
