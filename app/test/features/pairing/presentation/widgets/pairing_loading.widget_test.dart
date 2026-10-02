import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_loading.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Exercises PairingLoadingIndicator rendering.
void main() {
  group('PairingLoadingIndicator', () {
    testWidgets(
      'PairingLoadingIndicator renders the prototype ring keyed pairing-loading',
      (WidgetTester tester) async {
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: const Center(child: PairingLoadingIndicator()),
          ),
        );

        expect(find.byKey(const Key('pairing-loading')), findsOneWidget);
        expect(find.byType(RotationTransition), findsOneWidget);
        expect(
          tester.getSize(find.byKey(const Key('pairing-loading'))),
          const Size.square(DovahDialogMetrics.progressIndicatorSize),
        );
        final BoxDecoration ring =
            tester
                    .widget<DecoratedBox>(
                      find.descendant(
                        of: find.byKey(const Key('pairing-loading')),
                        matching: find.byType(DecoratedBox),
                      ),
                    )
                    .decoration
                as BoxDecoration;
        expect(ring.shape, BoxShape.circle);
        expect(
          ring.border,
          Border.all(
            color: DovahDialogMetrics.progressIndicatorTrackColor,
            width: DovahDialogMetrics.progressIndicatorStrokeWidth,
          ),
        );
        expect(find.byKey(const Key('pairing-loading-accent')), findsOneWidget);
        final AnimationController rotation =
            tester
                    .widget<RotationTransition>(find.byType(RotationTransition))
                    .turns
                as AnimationController;
        expect(
          rotation.duration,
          DovahDialogMetrics.progressIndicatorRotationDuration,
        );
        await tester.pump(const Duration(milliseconds: 500));
        expect(rotation.value, closeTo(0.5, 0.01));
      },
    );

    testWidgets('PairingLoadingIndicator stops for reduced motion', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: dovahThemeDataFor(DovahThemePreset.dovah),
          home: const MediaQuery(
            data: MediaQueryData(disableAnimations: true),
            child: PairingLoadingIndicator(),
          ),
        ),
      );

      final AnimationController rotation =
          tester
                  .widget<RotationTransition>(find.byType(RotationTransition))
                  .turns
              as AnimationController;
      expect(rotation.isAnimating, isFalse);
      expect(rotation.value, 0);
    });

    testWidgets(
      'PairingLoadingIndicator follows reduced-motion changes while mounted',
      (WidgetTester tester) async {
        final ValueNotifier<bool> disableAnimations = ValueNotifier(false);
        addTearDown(disableAnimations.dispose);
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: ValueListenableBuilder<bool>(
              valueListenable: disableAnimations,
              builder: (BuildContext context, bool disabled, Widget? child) =>
                  MediaQuery(
                    data: MediaQueryData(disableAnimations: disabled),
                    child: child!,
                  ),
              child: const Center(child: PairingLoadingIndicator()),
            ),
          ),
        );
        AnimationController rotation =
            tester
                    .widget<RotationTransition>(find.byType(RotationTransition))
                    .turns
                as AnimationController;
        expect(rotation.isAnimating, isTrue);

        disableAnimations.value = true;
        await tester.pump();
        rotation =
            tester
                    .widget<RotationTransition>(find.byType(RotationTransition))
                    .turns
                as AnimationController;
        expect(rotation.isAnimating, isFalse);
        expect(rotation.value, 0);

        disableAnimations.value = false;
        await tester.pump();
        rotation =
            tester
                    .widget<RotationTransition>(find.byType(RotationTransition))
                    .turns
                as AnimationController;
        expect(rotation.isAnimating, isTrue);
      },
    );
  });
}
