import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/device_identity/presentation/widgets/device_name_editor.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Builds a themed editor with the supplied Redux-projected save result.
/// @param savedNames Receives names submitted by the editor.
/// @param isSaving Whether the simulated save is still pending.
/// @param remoteRenameStatus The result supplied by Redux, if any.
/// @param remoteHostId The Host associated with the rename result.
/// @param admittedHostId The Host with the current admitted session.
/// @return The editor mounted in its themed test scaffold.
Widget buildDeviceNameEditor({
  required List<String> savedNames,
  bool isSaving = false,
  DeviceNameRenameStatus? remoteRenameStatus,
  String? remoteHostId,
  String? admittedHostId,
}) => MaterialApp(
  theme: dovahThemeDataFor(DovahThemePreset.dovah),
  home: Scaffold(
    body: DeviceNameEditor(
      key: const Key('device-name-editor'),
      displayName: 'Gaming PC',
      isSaving: isSaving,
      remoteRenameStatus: remoteRenameStatus,
      remoteHostId: remoteHostId,
      admittedHostId: admittedHostId,
      onSave: savedNames.add,
    ),
  ),
);

/// Exercises the Settings device-name editor's input and feedback behavior.
void main() {
  group('DeviceNameEditor calls onSave', () {
    testWidgets('DeviceNameEditor saves the edited value from its button', (
      WidgetTester tester,
    ) async {
      final List<String> savedNames = [];
      await pumpDovahThemedWidget(
        tester,
        DeviceNameEditor(displayName: 'Living Room PC', onSave: savedNames.add),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );
      await tester.enterText(
        find.byKey(const Key('settings-device-name-input')),
        'Gaming PC',
      );
      await tester.tap(find.byKey(const Key('settings-device-name-save')));

      expect(savedNames, ['Gaming PC']);
    });

    testWidgets('DeviceNameEditor saves the edited value from Enter', (
      WidgetTester tester,
    ) async {
      final List<String> savedNames = [];
      await pumpDovahThemedWidget(
        tester,
        DeviceNameEditor(displayName: 'Living Room PC', onSave: savedNames.add),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );
      await tester.enterText(
        find.byKey(const Key('settings-device-name-input')),
        'Gaming PC',
      );
      await tester.testTextInput.receiveAction(TextInputAction.done);

      expect(savedNames, ['Gaming PC']);
    });
  });

  group('DeviceNameEditor displays', () {
    testWidgets('DeviceNameEditor limits input to the UTF-8 byte boundary', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        DeviceNameEditor(displayName: '', onSave: (_) {}),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );
      final Finder input = find.byKey(const Key('settings-device-name-input'));
      await tester.enterText(input, 'é' * 32);
      expect(tester.widget<TextField>(input).controller!.text, 'é' * 32);
      await tester.enterText(input, 'é' * 33);
      expect(tester.widget<TextField>(input).controller!.text, 'é' * 32);
    });

    const Map<DeviceNameRenameStatus, String> outcomes = {
      DeviceNameRenameStatus.notAttempted:
          'Saved locally. This name will be used for future pairings.',
      DeviceNameRenameStatus.renamed:
          'Saved locally and confirmed by the Host.',
      DeviceNameRenameStatus.invalidDisplayName:
          'Saved locally, but the Host rejected the name.',
      DeviceNameRenameStatus.notTrusted:
          'Saved locally, but the active Host no longer trusts this device.',
      DeviceNameRenameStatus.unconfirmed:
          'Saved locally, but the Host did not confirm the rename.',
    };
    for (final MapEntry<DeviceNameRenameStatus, String> outcome
        in outcomes.entries) {
      testWidgets(
        'DeviceNameEditor displays the ${outcome.key} result for its save',
        (WidgetTester tester) async {
          final List<String> savedNames = <String>[];

          await tester.pumpWidget(
            buildDeviceNameEditor(
              savedNames: savedNames,
              remoteRenameStatus: outcome.key,
              remoteHostId: outcome.key == DeviceNameRenameStatus.notAttempted
                  ? null
                  : 'host-a',
              admittedHostId: outcome.key == DeviceNameRenameStatus.notAttempted
                  ? null
                  : 'host-a',
            ),
          );
          expect(find.text(outcome.value), findsNothing);

          await tester.tap(find.byKey(const Key('settings-device-name-save')));
          expect(savedNames, ['Gaming PC']);
          await tester.pumpWidget(
            buildDeviceNameEditor(savedNames: savedNames, isSaving: true),
          );
          await tester.pumpWidget(
            buildDeviceNameEditor(
              savedNames: savedNames,
              remoteRenameStatus: outcome.key,
              remoteHostId: outcome.key == DeviceNameRenameStatus.notAttempted
                  ? null
                  : 'host-a',
              admittedHostId: outcome.key == DeviceNameRenameStatus.notAttempted
                  ? null
                  : 'host-a',
            ),
          );

          expect(find.text(outcome.value), findsOneWidget);
        },
      );
    }

    testWidgets(
      'DeviceNameEditor hides a previous Host result when Settings reopens',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DeviceNameEditor(
            displayName: 'Gaming PC',
            remoteRenameStatus: DeviceNameRenameStatus.renamed,
            remoteHostId: 'host-a',
            admittedHostId: 'host-a',
            onSave: _ignoreName,
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(
          find.text('Saved locally and confirmed by the Host.'),
          findsNothing,
        );
      },
    );

    testWidgets(
      'DeviceNameEditor hides Host A feedback after disconnect or Host B admission',
      (WidgetTester tester) async {
        final List<String> savedNames = <String>[];

        await tester.pumpWidget(buildDeviceNameEditor(savedNames: savedNames));
        await tester.tap(find.byKey(const Key('settings-device-name-save')));
        await tester.pumpWidget(
          buildDeviceNameEditor(savedNames: savedNames, isSaving: true),
        );
        await tester.pumpWidget(
          buildDeviceNameEditor(
            savedNames: savedNames,
            remoteRenameStatus: DeviceNameRenameStatus.renamed,
            remoteHostId: 'host-a',
            admittedHostId: 'host-a',
          ),
        );
        expect(
          find.text('Saved locally and confirmed by the Host.'),
          findsOneWidget,
        );

        await tester.pumpWidget(
          buildDeviceNameEditor(
            savedNames: savedNames,
            remoteRenameStatus: DeviceNameRenameStatus.renamed,
            remoteHostId: 'host-a',
            admittedHostId: 'host-b',
          ),
        );
        expect(
          find.text('Saved locally and confirmed by the Host.'),
          findsNothing,
        );

        await tester.pumpWidget(
          buildDeviceNameEditor(
            savedNames: savedNames,
            remoteRenameStatus: DeviceNameRenameStatus.renamed,
            remoteHostId: 'host-a',
          ),
        );
        expect(
          find.text('Saved locally and confirmed by the Host.'),
          findsNothing,
        );
      },
    );

    testWidgets(
      'DeviceNameEditor hides the previous Host result until the next save completes',
      (WidgetTester tester) async {
        final List<String> savedNames = <String>[];

        await tester.pumpWidget(buildDeviceNameEditor(savedNames: savedNames));
        await tester.tap(find.byKey(const Key('settings-device-name-save')));
        await tester.pumpWidget(
          buildDeviceNameEditor(savedNames: savedNames, isSaving: true),
        );
        await tester.pumpWidget(
          buildDeviceNameEditor(
            savedNames: savedNames,
            remoteRenameStatus: DeviceNameRenameStatus.renamed,
            remoteHostId: 'host-a',
            admittedHostId: 'host-a',
          ),
        );
        expect(
          find.text('Saved locally and confirmed by the Host.'),
          findsOneWidget,
        );

        await tester.tap(find.byKey(const Key('settings-device-name-save')));
        await tester.pumpWidget(
          buildDeviceNameEditor(savedNames: savedNames, isSaving: true),
        );
        expect(
          find.text('Saved locally and confirmed by the Host.'),
          findsNothing,
        );

        await tester.pumpWidget(
          buildDeviceNameEditor(savedNames: savedNames, isSaving: true),
        );
        expect(
          find.text('Saved locally and confirmed by the Host.'),
          findsNothing,
        );
        await tester.pumpWidget(
          buildDeviceNameEditor(
            savedNames: savedNames,
            remoteRenameStatus: DeviceNameRenameStatus.notAttempted,
          ),
        );
        expect(
          find.text(
            'Saved locally. This name will be used for future pairings.',
          ),
          findsOneWidget,
        );
        expect(savedNames, ['Gaming PC', 'Gaming PC']);
      },
    );

    testWidgets(
      'DeviceNameEditor displays local failures and disables saving',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DeviceNameEditor(
            displayName: 'Gaming PC',
            isSaving: true,
            saveFailure: 'This name could not be saved.',
            onSave: (_) {},
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(find.text('This name could not be saved.'), findsOneWidget);
        final DovahButton button = tester.widget(
          find.byKey(const Key('settings-device-name-save')),
        );
        expect(button.onPressed, isNull);
      },
    );

    testWidgets('DeviceNameEditor stacks controls in a narrow window', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        DeviceNameEditor(displayName: 'Gaming PC', onSave: (_) {}),
        preset: DovahThemePreset.dovah,
        size: const Size(240, 560),
      );

      expect(tester.takeException(), isNull);
      expect(
        find.byKey(const Key('settings-device-name-save')),
        findsOneWidget,
      );
    });
  });

  group('DeviceNameEditor handles saving state', () {
    testWidgets('DeviceNameEditor does not submit Enter while saving', (
      WidgetTester tester,
    ) async {
      final List<String> savedNames = [];
      await pumpDovahThemedWidget(
        tester,
        DeviceNameEditor(
          displayName: 'Gaming PC',
          isSaving: true,
          onSave: savedNames.add,
        ),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );
      await tester.testTextInput.receiveAction(TextInputAction.done);

      expect(savedNames, isEmpty);
    });

    testWidgets(
      'DeviceNameEditor shows Saved for one second after completion',
      (WidgetTester tester) async {
        setDovahTestWindow(tester, const Size(1280, 720));
        final ThemeData theme = dovahThemeDataFor(DovahThemePreset.dovah);
        await tester.pumpWidget(
          MaterialApp(
            theme: theme,
            home: const Scaffold(
              body: DeviceNameEditor(
                key: Key('device-name-editor'),
                displayName: 'Old name',
                isSaving: true,
                onSave: _ignoreName,
              ),
            ),
          ),
        );
        await tester.pumpWidget(
          MaterialApp(
            theme: theme,
            home: const Scaffold(
              body: DeviceNameEditor(
                key: Key('device-name-editor'),
                displayName: 'Gaming PC',
                onSave: _ignoreName,
              ),
            ),
          ),
        );

        expect(find.text('Saved'), findsOneWidget);
        await tester.pump(const Duration(milliseconds: 999));
        expect(find.text('Saved'), findsOneWidget);
        await tester.pump(const Duration(milliseconds: 1));
        expect(find.text('Save'), findsOneWidget);
      },
    );

    testWidgets('DeviceNameEditor hides Saved when the user edits again', (
      WidgetTester tester,
    ) async {
      setDovahTestWindow(tester, const Size(1280, 720));
      final ThemeData theme = dovahThemeDataFor(DovahThemePreset.dovah);
      await tester.pumpWidget(
        MaterialApp(
          theme: theme,
          home: const Scaffold(
            body: DeviceNameEditor(
              key: Key('device-name-editor'),
              displayName: 'Old name',
              isSaving: true,
              onSave: _ignoreName,
            ),
          ),
        ),
      );
      await tester.pumpWidget(
        MaterialApp(
          theme: theme,
          home: const Scaffold(
            body: DeviceNameEditor(
              key: Key('device-name-editor'),
              displayName: 'Gaming PC',
              onSave: _ignoreName,
            ),
          ),
        ),
      );
      await tester.enterText(
        find.byKey(const Key('settings-device-name-input')),
        'Gaming desktop',
      );
      await tester.pump();

      expect(find.text('Save'), findsOneWidget);
      expect(find.text('Saved'), findsNothing);
    });

    testWidgets('DeviceNameEditor displays a preference-load failure', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        DeviceNameEditor(
          displayName: null,
          loadFailure: 'The saved device name could not be loaded.',
          onSave: (_) {},
        ),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );

      expect(
        find.text('The saved device name could not be loaded.'),
        findsOneWidget,
      );
    });
  });
}

/// Ignores the edited name in harnesses that only verify presentation state.
void _ignoreName(String _) {}
