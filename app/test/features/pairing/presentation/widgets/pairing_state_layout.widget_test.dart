import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Pumps a [PairingStateLayout] with the given body parts under the Dovah preset.
Future<void> pumpLayout(
  WidgetTester tester, {
  String body = 'Body copy.',
  String? highlight,
  String bodyEnd = '',
  List<Widget> children = const <Widget>[],
  double? markBottomGap,
  DovahThemePreset preset = DovahThemePreset.dovah,
  Size size = const Size(900, 560),
}) => pumpDovahThemedWidget(
  tester,
  PairingStateLayout(
    mark: const SizedBox(key: Key('test-mark'), width: 42, height: 42),
    heading: 'Heading',
    body: body,
    highlight: highlight,
    bodyEnd: bodyEnd,
    markBottomGap: markBottomGap,
    children: children,
  ),
  preset: preset,
  size: size,
);

/// Exercises [PairingStateLayout] structure, emphasis, and accessibility.
void main() {
  group('PairingStateLayout contains widgets', () {
    testWidgets(
      'PairingStateLayout contains the mark above the heading above the body above its children',
      (WidgetTester tester) async {
        await pumpLayout(
          tester,
          children: const [SizedBox(key: Key('test-child'), height: 10)],
        );

        final double mark = tester
            .getTopLeft(find.byKey(const Key('test-mark')))
            .dy;
        final double heading = tester
            .getTopLeft(find.byKey(const Key('pairing-heading')))
            .dy;
        final double body = tester
            .getTopLeft(find.byKey(const Key('pairing-body')))
            .dy;
        final double child = tester
            .getTopLeft(find.byKey(const Key('test-child')))
            .dy;
        expect(mark, lessThan(heading));
        expect(heading, lessThan(body));
        expect(body, lessThan(child));
      },
    );

    testWidgets('PairingStateLayout displays the heading', (
      WidgetTester tester,
    ) async {
      await pumpLayout(tester);

      expect(find.text('Heading'), findsOneWidget);
    });

    testWidgets(
      'PairingStateLayout displays the body with the highlight emphasized between its parts',
      (WidgetTester tester) async {
        await pumpLayout(
          tester,
          body: 'Connect to ',
          highlight: 'Bedroom PC',
          bodyEnd: '.',
        );

        final Text body = tester.widget<Text>(
          find.byKey(const Key('pairing-body')),
        );
        final TextSpan span = body.textSpan! as TextSpan;
        expect(span.toPlainText(), 'Connect to Bedroom PC.');
        final TextSpan highlight = span.children!.first as TextSpan;
        expect(highlight.text, 'Bedroom PC');
        expect(highlight.style?.fontWeight, FontWeight.w700);
      },
    );

    testWidgets(
      'PairingStateLayout displays a body without a highlight or ending as plain text',
      (WidgetTester tester) async {
        await pumpLayout(tester);

        final Text body = tester.widget<Text>(
          find.byKey(const Key('pairing-body')),
        );
        expect((body.textSpan! as TextSpan).children, isEmpty);
        expect(body.textSpan!.toPlainText(), 'Body copy.');
      },
    );
  });

  group('PairingStateLayout spaces its parts for the window height', () {
    for (final Size size in dovahResponsiveTestSizes) {
      final bool isCompact = size.height <= 620;
      testWidgets(
        'PairingStateLayout sizes the heading and spaces its parts for a ${isCompact ? "compact" : "regular"} window at $size',
        (WidgetTester tester) async {
          await pumpLayout(
            tester,
            children: const [SizedBox(key: Key('test-child'), height: 10)],
            size: size,
          );

          final Rect mark = tester.getRect(find.byKey(const Key('test-mark')));
          final Rect heading = tester.getRect(
            find.byKey(const Key('pairing-heading')),
          );
          final Rect body = tester.getRect(
            find.byKey(const Key('pairing-body')),
          );
          final Rect child = tester.getRect(
            find.byKey(const Key('test-child')),
          );
          final Text headingText = tester.widget<Text>(
            find.byKey(const Key('pairing-heading')),
          );
          final Text bodyText = tester.widget<Text>(
            find.byKey(const Key('pairing-body')),
          );
          expect(headingText.style?.fontSize, isA<double>());
          expect(headingText.style?.fontSize, isCompact ? 22 : 24);
          expect(bodyText.style?.height, isA<double>());
          expect(bodyText.style?.height, isCompact ? 1.35 : 1.5);
          expect(heading.top - mark.bottom, isCompact ? 8 : 16);
          expect(body.top - heading.bottom, isCompact ? 5 : 8);
          expect(child.top - body.bottom, isCompact ? 10 : 20);
        },
      );
    }

    for (final Size size in const [Size(900, 560), Size(1280, 720)]) {
      testWidgets(
        'PairingStateLayout uses a supplied mark gap instead of the window gap at $size',
        (WidgetTester tester) async {
          await pumpLayout(tester, markBottomGap: 17, size: size);

          expect(
            tester.getTopLeft(find.byKey(const Key('pairing-heading'))).dy -
                tester.getBottomLeft(find.byKey(const Key('test-mark'))).dy,
            17,
          );
        },
      );
    }
  });

  group('PairingStateLayout meets accessibility recommended guidelines', () {
    testWidgets(
      'PairingStateLayout exposes the heading as a live-region header',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await pumpLayout(tester);

          expect(
            tester.getSemantics(find.byKey(const Key('pairing-heading'))),
            isSemantics(label: 'Heading', isHeader: true, isLiveRegion: true),
          );
        } finally {
          handle.dispose();
        }
      },
    );
  });

  group('PairingStateLayout lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'PairingStateLayout renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpLayout(
              tester,
              body: 'A long body sentence. ' * 6,
              highlight: 'Bedroom PC',
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(
              tester.getSize(find.byKey(const Key('pairing-body'))).width,
              lessThanOrEqualTo(DovahThemeTokens.pairingBodyMaxWidth),
            );
          },
        );
      }
    }
  });
}
