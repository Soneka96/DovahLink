import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_form.widget.dart';

/// Exercises PairingCodeForm rendering, submission, and accessibility
/// behavior.
void main() {
  group('PairingCodeForm contains widgets', () {
    testWidgets(
      'contains the code and display-name fields and a confirm button',
      (WidgetTester tester) async {
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(body: PairingCodeForm(onSubmit: (_, _) {})),
          ),
        );

        expect(find.byKey(const Key('pairing-code-field')), findsOneWidget);
        expect(
          find.byKey(const Key('pairing-display-name-field')),
          findsOneWidget,
        );
        expect(find.byKey(const Key('pairing-confirm-button')), findsOneWidget);
      },
    );

    testWidgets('submitting the form calls onSubmit with the entered values', (
      WidgetTester tester,
    ) async {
      String? submittedCode;
      String? submittedDisplayName;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: PairingCodeForm(
              onSubmit: (code, displayName) {
                submittedCode = code;
                submittedDisplayName = displayName;
              },
            ),
          ),
        ),
      );

      await tester.enterText(
        find.byKey(const Key('pairing-code-field')),
        '123456',
      );
      await tester.enterText(
        find.byKey(const Key('pairing-display-name-field')),
        'Desktop',
      );
      await tester.tap(find.byKey(const Key('pairing-confirm-button')));
      await tester.pump();

      expect(submittedCode, '123456');
      expect(submittedDisplayName, 'Desktop');
    });

    testWidgets(
      'submitting the form with no display name calls onSubmit with a null displayName',
      (WidgetTester tester) async {
        String? submittedDisplayName = 'not null yet';
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(
              body: PairingCodeForm(
                onSubmit: (_, displayName) =>
                    submittedDisplayName = displayName,
              ),
            ),
          ),
        );

        await tester.enterText(
          find.byKey(const Key('pairing-code-field')),
          '123456',
        );
        await tester.tap(find.byKey(const Key('pairing-confirm-button')));
        await tester.pump();

        expect(submittedDisplayName, isNull);
      },
    );

    testWidgets('disposes cleanly when unmounted', (WidgetTester tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(body: PairingCodeForm(onSubmit: (_, _) {})),
        ),
      );

      await tester.pumpWidget(const MaterialApp(home: SizedBox.shrink()));

      expect(tester.takeException(), isNull);
    });

    testWidgets(
      'submitting with an empty code still calls onSubmit with an empty string',
      (WidgetTester tester) async {
        String? submittedCode = 'not called yet';
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(
              body: PairingCodeForm(
                onSubmit: (code, _) => submittedCode = code,
              ),
            ),
          ),
        );

        await tester.tap(find.byKey(const Key('pairing-confirm-button')));
        await tester.pump();

        expect(submittedCode, '');
      },
    );
  });

  group('PairingCodeForm meets accessibility recommended guidelines', () {
    testWidgets(
      'labels the confirm button and meets its minimum interactive size',
      (WidgetTester tester) async {
        // See the equivalent check on the pairing screen's request-code
        // button for why this checks labeledTapTargetGuideline +
        // kMinInteractiveDimension directly rather than the mobile-specific
        // tap-target guidelines: DovahLink is a Windows desktop app, and the
        // app theme's default VisualDensity.adaptivePlatformDensity shrinks
        // targets below the 48dp mobile touch guideline by design.
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(
            MaterialApp(
              home: Scaffold(body: PairingCodeForm(onSubmit: (_, _) {})),
            ),
          );

          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          final Size buttonSize = tester.getSize(
            find.byKey(const Key('pairing-confirm-button')),
          );
          expect(
            buttonSize.height,
            greaterThanOrEqualTo(kMinInteractiveDimension),
          );
        } finally {
          handle.dispose();
        }
      },
    );

    testWidgets('lays out without overflow at a large text scale', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MediaQuery(
          data: const MediaQueryData(textScaler: TextScaler.linear(2.0)),
          child: MaterialApp(
            home: Scaffold(body: PairingCodeForm(onSubmit: (_, _) {})),
          ),
        ),
      );

      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('pairing-code-field')), findsOneWidget);
      expect(find.byKey(const Key('pairing-confirm-button')), findsOneWidget);
    });

    testWidgets(
      'traverses focus from the code field to the display-name field in order',
      (WidgetTester tester) async {
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(body: PairingCodeForm(onSubmit: (_, _) {})),
          ),
        );

        final Finder codeField = find.byKey(const Key('pairing-code-field'));
        final Finder displayNameField = find.byKey(
          const Key('pairing-display-name-field'),
        );
        final FocusNode codeFocusNode = tester
            .widget<EditableText>(
              find.descendant(
                of: codeField,
                matching: find.byType(EditableText),
              ),
            )
            .focusNode;
        final FocusNode displayNameFocusNode = tester
            .widget<EditableText>(
              find.descendant(
                of: displayNameField,
                matching: find.byType(EditableText),
              ),
            )
            .focusNode;

        await tester.tap(codeField);
        await tester.pump();
        expect(codeFocusNode.hasFocus, isTrue);
        expect(displayNameFocusNode.hasFocus, isFalse);

        codeFocusNode.nextFocus();
        await tester.pump();

        expect(codeFocusNode.hasFocus, isFalse);
        expect(displayNameFocusNode.hasFocus, isTrue);
      },
    );
  });
}
