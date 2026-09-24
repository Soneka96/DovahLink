import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_form.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// The pairing code field's key.
const Key codeFieldKey = Key('pairing-code-field');

/// The Pair button's key.
const Key confirmButtonKey = Key('pairing-confirm-button');

/// Builds a form wrapped in a themed [MaterialApp], recording submissions into [submissions].
Widget buildForm({
  List<String>? submissions,
  String? errorMessage,
  List<Widget> secondaryActions = const <Widget>[],
}) => MaterialApp(
  theme: dovahThemeDataFor(DovahThemePreset.dovah),
  home: Scaffold(
    body: Center(
      child: PairingCodeForm(
        onSubmit: (String code) => submissions?.add(code),
        errorMessage: errorMessage,
        secondaryActions: secondaryActions,
      ),
    ),
  ),
);

/// Finds the semantics node of the Pair button's own control.
Finder pairSemantics() => find.descendant(
  of: find.byKey(confirmButtonKey),
  matching: find.byKey(const Key('dovah-button-semantics')),
);

/// Whether the Pair button is enabled.
bool isPairEnabled(WidgetTester tester) =>
    tester.widget<DovahButton>(find.byKey(confirmButtonKey)).onPressed != null;

/// The current text of the code field.
String codeText(WidgetTester tester) =>
    tester.widget<TextField>(find.byKey(codeFieldKey)).controller!.text;

/// Exercises PairingCodeForm rendering, code entry, submission, and accessibility behavior.
void main() {
  group('PairingCodeForm contains widgets', () {
    testWidgets('PairingCodeForm contains the code field and a Pair button', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      expect(find.byKey(codeFieldKey), findsOneWidget);
      expect(find.byKey(confirmButtonKey), findsOneWidget);
      expect(find.text('Pair'), findsOneWidget);
    });

    testWidgets('PairingCodeForm contains no device-name field', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      expect(find.byKey(const Key('pairing-display-name-field')), findsNothing);
      expect(find.text('Device name (optional)'), findsNothing);
      expect(find.byType(TextField), findsOneWidget);
    });

    testWidgets('PairingCodeForm contains one digit box per code digit', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      for (int index = 0; index < pairingCodeLength; index++) {
        expect(find.byKey(Key('pairing-code-box-$index')), findsOneWidget);
      }
    });

    testWidgets('PairingCodeForm contains secondary actions before Pair', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        buildForm(
          secondaryActions: const [SizedBox(key: Key('secondary'), width: 40)],
        ),
      );

      expect(
        tester.getTopLeft(find.byKey(const Key('secondary'))).dx,
        lessThan(tester.getTopLeft(find.byKey(confirmButtonKey)).dx),
      );
    });

    testWidgets('PairingCodeForm disposes cleanly when unmounted', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      await tester.pumpWidget(const MaterialApp(home: SizedBox.shrink()));

      expect(tester.takeException(), isNull);
    });
  });

  group('PairingCodeForm accepts only a complete code', () {
    testWidgets('PairingCodeForm disables Pair before any digit is entered', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      expect(isPairEnabled(tester), isFalse);
    });

    testWidgets('PairingCodeForm keeps Pair disabled for an incomplete code', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      await tester.enterText(find.byKey(codeFieldKey), '12345');
      await tester.pump();

      expect(isPairEnabled(tester), isFalse);
    });

    testWidgets('PairingCodeForm enables Pair for a complete code', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      await tester.enterText(find.byKey(codeFieldKey), '123456');
      await tester.pump();

      expect(isPairEnabled(tester), isTrue);
    });

    testWidgets('PairingCodeForm disables Pair again when a digit is deleted', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());
      await tester.enterText(find.byKey(codeFieldKey), '123456');
      await tester.pump();

      await tester.enterText(find.byKey(codeFieldKey), '12345');
      await tester.pump();

      expect(isPairEnabled(tester), isFalse);
    });

    testWidgets('PairingCodeForm drops digits beyond the code length', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      await tester.enterText(find.byKey(codeFieldKey), '12345678');
      await tester.pump();

      expect(codeText(tester), '123456');
    });

    testWidgets('PairingCodeForm strips non-digits from typed or pasted text', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      await tester.enterText(find.byKey(codeFieldKey), '12-3 a4');
      await tester.pump();

      expect(codeText(tester), '1234');
    });

    testWidgets('PairingCodeForm shows each entered digit in its box', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      await tester.enterText(find.byKey(codeFieldKey), '4821');
      await tester.pump();

      for (final (int index, String digit) in [
        (0, '4'),
        (1, '8'),
        (2, '2'),
        (3, '1'),
      ]) {
        expect(
          find.descendant(
            of: find.byKey(Key('pairing-code-box-$index')),
            matching: find.text(digit),
          ),
          findsOneWidget,
        );
      }
    });
  });

  group('PairingCodeForm submits', () {
    testWidgets(
      'PairingCodeForm tapping Pair calls onSubmit with only the code',
      (WidgetTester tester) async {
        final List<String> submissions = [];
        await tester.pumpWidget(buildForm(submissions: submissions));

        await tester.enterText(find.byKey(codeFieldKey), '123456');
        await tester.pump();
        await tester.tap(find.byKey(confirmButtonKey));
        await tester.pump();

        expect(submissions, ['123456']);
      },
    );

    testWidgets(
      'PairingCodeForm tapping a disabled Pair does not call onSubmit',
      (WidgetTester tester) async {
        final List<String> submissions = [];
        await tester.pumpWidget(buildForm(submissions: submissions));

        await tester.enterText(find.byKey(codeFieldKey), '123');
        await tester.pump();
        await tester.tap(find.byKey(confirmButtonKey));
        await tester.pump();

        expect(submissions, isEmpty);
      },
    );

    testWidgets(
      'PairingCodeForm pressing Enter in the code field submits a complete code',
      (WidgetTester tester) async {
        final List<String> submissions = [];
        await tester.pumpWidget(buildForm(submissions: submissions));

        await tester.enterText(find.byKey(codeFieldKey), '654321');
        await tester.testTextInput.receiveAction(TextInputAction.done);
        await tester.pump();

        expect(submissions, ['654321']);
      },
    );

    testWidgets(
      'PairingCodeForm pressing Enter with an incomplete code shows a message and does not call onSubmit',
      (WidgetTester tester) async {
        final List<String> submissions = [];
        await tester.pumpWidget(buildForm(submissions: submissions));

        await tester.enterText(find.byKey(codeFieldKey), '123');
        await tester.testTextInput.receiveAction(TextInputAction.done);
        await tester.pump();

        expect(submissions, isEmpty);
        expect(
          find.text('Enter the $pairingCodeLength-digit code shown in Skyrim.'),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'PairingCodeForm clears the incomplete-code message when the code is edited',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildForm());
        await tester.enterText(find.byKey(codeFieldKey), '123');
        await tester.testTextInput.receiveAction(TextInputAction.done);
        await tester.pump();

        await tester.enterText(find.byKey(codeFieldKey), '1234');
        await tester.pump();

        expect(
          find.text('Enter the $pairingCodeLength-digit code shown in Skyrim.'),
          findsNothing,
        );
      },
    );
  });

  group('PairingCodeForm shows messages', () {
    testWidgets('PairingCodeForm displays errorMessage in the message slot', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        buildForm(errorMessage: 'That code is not correct.'),
      );

      expect(find.text('That code is not correct.'), findsOneWidget);
    });

    testWidgets('PairingCodeForm displays an empty message slot without one', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());

      expect(
        tester
            .widget<Text>(
              find.descendant(
                of: find.byKey(const Key('pairing-code-message')),
                matching: find.byType(Text),
              ),
            )
            .data,
        '',
      );
    });

    testWidgets(
      'PairingCodeForm shows the incomplete-code message ahead of errorMessage',
      (WidgetTester tester) async {
        await tester.pumpWidget(
          buildForm(errorMessage: 'That code is not correct.'),
        );

        await tester.enterText(find.byKey(codeFieldKey), '12');
        await tester.testTextInput.receiveAction(TextInputAction.done);
        await tester.pump();

        expect(find.text('That code is not correct.'), findsNothing);
        expect(
          find.text('Enter the $pairingCodeLength-digit code shown in Skyrim.'),
          findsOneWidget,
        );
      },
    );
  });

  group('PairingCodeForm meets accessibility recommended guidelines', () {
    testWidgets(
      'PairingCodeForm labels the code field for assistive technology',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(buildForm());

          expect(
            tester.getSemantics(find.byKey(codeFieldKey)).label,
            contains('Pairing code, $pairingCodeLength digits'),
          );
        } finally {
          handle.dispose();
        }
      },
    );

    testWidgets(
      'PairingCodeForm labels the Pair button and reports it disabled until the code is complete',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(buildForm());

          expect(
            tester.getSemantics(pairSemantics()),
            isSemantics(
              label: 'Pair',
              isButton: true,
              hasEnabledState: true,
              isEnabled: false,
            ),
          );

          await tester.enterText(find.byKey(codeFieldKey), '123456');
          await tester.pump();

          expect(
            tester.getSemantics(pairSemantics()),
            isSemantics(
              label: 'Pair',
              isButton: true,
              hasEnabledState: true,
              isEnabled: true,
              hasTapAction: true,
            ),
          );
        } finally {
          handle.dispose();
        }
      },
    );

    testWidgets('PairingCodeForm announces its message as a live region', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(buildForm(errorMessage: 'Wrong code.'));

        expect(
          tester.getSemantics(find.text('Wrong code.')),
          isSemantics(label: 'Wrong code.', isLiveRegion: true),
        );
      } finally {
        handle.dispose();
      }
    });

    testWidgets('PairingCodeForm hides the digit boxes from semantics', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(buildForm());
        await tester.enterText(find.byKey(codeFieldKey), '4821');
        await tester.pump();

        expect(find.bySemanticsLabel('4'), findsNothing);
      } finally {
        handle.dispose();
      }
    });

    testWidgets('PairingCodeForm focuses the code field when it appears', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildForm());
      await tester.pump();

      final FocusNode codeFocusNode = tester
          .widget<EditableText>(
            find.descendant(
              of: find.byKey(codeFieldKey),
              matching: find.byType(EditableText),
            ),
          )
          .focusNode;
      expect(codeFocusNode.hasFocus, isTrue);
    });

    testWidgets(
      'PairingCodeForm traverses focus from the code field to the Pair button in order',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildForm());
        await tester.enterText(find.byKey(codeFieldKey), '123456');
        await tester.pump();

        final FocusNode codeFocusNode = tester
            .widget<EditableText>(
              find.descendant(
                of: find.byKey(codeFieldKey),
                matching: find.byType(EditableText),
              ),
            )
            .focusNode;
        expect(codeFocusNode.hasFocus, isTrue);

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();

        expect(codeFocusNode.hasFocus, isFalse);
        expect(
          FocusManager.instance.primaryFocus!.context!
              .findAncestorWidgetOfExactType<DovahButton>()
              ?.key,
          confirmButtonKey,
        );
      },
    );

    testWidgets(
      'PairingCodeForm traverses focus back from the Pair button to the code field with Shift+Tab',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildForm());
        await tester.enterText(find.byKey(codeFieldKey), '123456');
        await tester.pump();
        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();

        await tester.sendKeyDownEvent(LogicalKeyboardKey.shiftLeft);
        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.sendKeyUpEvent(LogicalKeyboardKey.shiftLeft);
        await tester.pump();

        final FocusNode codeFocusNode = tester
            .widget<EditableText>(
              find.descendant(
                of: find.byKey(codeFieldKey),
                matching: find.byType(EditableText),
              ),
            )
            .focusNode;
        expect(codeFocusNode.hasFocus, isTrue);
      },
    );

    testWidgets(
      'PairingCodeForm keeps the Pair button at the minimum interactive size',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildForm());

        final Size buttonSize = tester.getSize(find.byKey(confirmButtonKey));
        expect(
          buttonSize.height,
          greaterThanOrEqualTo(kMinInteractiveDimension),
        );
        expect(
          buttonSize.width,
          greaterThanOrEqualTo(kMinInteractiveDimension),
        );
      },
    );

    testWidgets(
      'PairingCodeForm lays out without overflow at a large text scale',
      (WidgetTester tester) async {
        await tester.binding.setSurfaceSize(const Size(900, 560));
        addTearDown(() => tester.binding.setSurfaceSize(null));
        await tester.pumpWidget(
          MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(2.0)),
            child: buildForm(errorMessage: 'That code is not correct.'),
          ),
        );

        expect(tester.takeException(), isNull);
        expect(find.byKey(codeFieldKey), findsOneWidget);
        expect(find.byKey(confirmButtonKey), findsOneWidget);
      },
    );
  });

  group('PairingCodeForm lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'PairingCodeForm renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              Center(
                child: PairingCodeForm(
                  onSubmit: (_) {},
                  errorMessage: 'That code is not correct.',
                  secondaryActions: const [
                    SizedBox(width: 120, height: 48),
                    SizedBox(width: 120, height: 48),
                  ],
                ),
              ),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });

  group('PairingCodeForm sizes its parts for the window height', () {
    for (final Size size in dovahResponsiveTestSizes) {
      final bool isCompact = size.height <= 620;
      testWidgets(
        'PairingCodeForm draws ${isCompact ? "45x48" : "49x56"} boxes and spaces the message ${isCompact ? 6 : 12} below them at $size',
        (WidgetTester tester) async {
          setDovahTestWindow(tester, size);
          await tester.pumpWidget(buildForm());

          final Rect box = tester.getRect(
            find.byKey(const Key('pairing-code-box-0')),
          );
          final Rect lastBox = tester.getRect(
            find.byKey(const Key('pairing-code-box-${pairingCodeLength - 1}')),
          );
          final Rect message = tester.getRect(
            find.byKey(const Key('pairing-code-message')),
          );
          expect(box.size, Size(isCompact ? 45 : 49, isCompact ? 48 : 56));
          expect(
            lastBox.right - box.left,
            pairingCodeLength * (isCompact ? 45 : 49) +
                (pairingCodeLength - 1) * 8,
          );
          expect(message.top - box.bottom, isCompact ? 6 : 12);
          expect(
            tester.getTopLeft(find.byKey(confirmButtonKey)).dy - message.bottom,
            isCompact ? 7 : 18,
          );
        },
      );
    }
  });
}
