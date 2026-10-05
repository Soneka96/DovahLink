import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_character.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import '../../../../fixtures/fixtures.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show CharacterVitalsState, DovahLinkStateStatus;

/// Builds one Current Character panel using representative Redux-backed values.
SessionOverviewCharacterPanel _buildPanel({
  String? name = 'Gonçalo',
  DovahLinkStateStatus nameStatus = DovahLinkStateStatus.synchronized,
  String? race = 'Nord',
  String? level = 'Level 43',
  DovahLinkStateStatus levelStatus = DovahLinkStateStatus.synchronized,
  String? supernaturalLabel,
  DovahLinkStateStatus supernaturalStatus = DovahLinkStateStatus.synchronized,
  CharacterVitalsState? vitalValues,
  DovahLinkStateStatus vitalStatus = DovahLinkStateStatus.synchronized,
}) => SessionOverviewCharacterPanel(
  name: name,
  nameStatus: nameStatus,
  race: race,
  level: level,
  levelStatus: levelStatus,
  supernaturalLabel: supernaturalLabel,
  supernaturalStatus: supernaturalStatus,
  vitals: Fixtures.buildSessionOverviewVitalsViewData(
    value: vitalValues ?? Fixtures.buildCharacterVitals(),
    status: vitalStatus,
  ),
);

/// Exercises the Current Character hero and its three real vital values.
void main() {
  group('SessionOverviewCharacterPanel displays', () {
    testWidgets(
      'SessionOverviewCharacterPanel displays the character and current vitals',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(supernaturalLabel: 'Vampire Lord · Werewolf'),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(find.text('CURRENT CHARACTER'), findsOneWidget);
        expect(find.text('Gonçalo'), findsOneWidget);
        expect(
          find.text('Nord · Level 43 · Vampire Lord · Werewolf'),
          findsOneWidget,
        );
        expect(find.text('80'), findsOneWidget);
        expect(find.text('40'), findsOneWidget);
        expect(find.text('50'), findsOneWidget);
        expect(find.text('Health'), findsOneWidget);
        expect(find.text('Magicka'), findsOneWidget);
        expect(find.text('Stamina'), findsOneWidget);
      },
    );

    testWidgets(
      'SessionOverviewCharacterPanel keeps labels and geometry when values are absent',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );
        final Size availableSize = tester.getSize(
          find.byKey(const Key('session-overview-character-panel')),
        );

        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: Scaffold(
              body: _buildPanel(
                name: null,
                nameStatus: DovahLinkStateStatus.notSubscribed,
                race: null,
                level: null,
                levelStatus: DovahLinkStateStatus.notSubscribed,
                supernaturalLabel: null,
                supernaturalStatus: DovahLinkStateStatus.notSubscribed,
                vitalValues: Fixtures.buildCharacterVitals(isAvailable: false),
                vitalStatus: DovahLinkStateStatus.unavailable,
              ),
            ),
          ),
        );

        expect(
          tester.getSize(
            find.byKey(const Key('session-overview-character-panel')),
          ),
          availableSize,
        );
        expect(find.text('Health'), findsOneWidget);
        expect(find.text('Magicka'), findsOneWidget);
        expect(find.text('Stamina'), findsOneWidget);
        expect(find.text('0'), findsNothing);
        expect(find.textContaining('Unavailable'), findsNothing);
      },
    );

    testWidgets(
      'SessionOverviewCharacterPanel marks a retained stale name with reduced emphasis',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(nameStatus: DovahLinkStateStatus.stale),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        final Text name = tester.widget(
          find.byKey(const Key('session-overview-character-name')),
        );
        expect(name.data, 'Gonçalo');
        expect(name.style?.fontStyle, FontStyle.italic);
      },
    );

    testWidgets(
      'SessionOverviewCharacterPanel sheens recovering values without status copy',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(vitalStatus: DovahLinkStateStatus.recovering),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(
          find.byKey(const Key('session-overview-character-sheen')),
          findsOneWidget,
        );
        expect(find.textContaining('Recovering'), findsNothing);
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'SessionOverviewCharacterPanel renders $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              _buildPanel(
                name: 'A modded character name long enough to wrap in the hero',
                supernaturalLabel: 'Vampire Lord · Werewolf',
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
}
