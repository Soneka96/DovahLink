import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

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
      'SessionOverviewCharacterPanel layers Frostbound textures beneath recovery and content',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(vitalStatus: DovahLinkStateStatus.recovering),
          preset: DovahThemePreset.frostbound,
          size: const Size(1280, 720),
        );

        final Stack artwork = tester.widget(
          find.byKey(const Key('session-overview-character-artwork')),
        );
        final List<Key?> layerKeys = artwork.children
            .map((Widget layer) => layer.key)
            .toList();

        expect(
          layerKeys.indexOf(const Key('session-overview-character-hero-scrim')),
          lessThan(
            layerKeys.indexOf(const Key('session-overview-character-texture')),
          ),
        );
        expect(
          layerKeys.indexOf(const Key('session-overview-character-texture')),
          lessThan(
            layerKeys.indexOf(
              const Key('session-overview-character-floor-scrim'),
            ),
          ),
        );
        expect(
          layerKeys.indexOf(
            const Key('session-overview-character-floor-scrim'),
          ),
          lessThan(
            layerKeys.indexOf(const Key('session-overview-character-sheen')),
          ),
        );
        expect(artwork.children.last, isA<Padding>());
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'SessionOverviewCharacterPanel uses the $preset hero texture',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            _buildPanel(),
            preset: preset,
            size: const Size(1280, 720),
          );

          final Finder texture = find.byKey(
            const Key('session-overview-character-texture'),
          );
          expect(
            texture,
            preset == DovahThemePreset.frostbound
                ? findsOneWidget
                : findsNothing,
          );
          if (preset == DovahThemePreset.frostbound) {
            expect(
              (tester.widget<DecoratedBox>(texture).decoration as BoxDecoration)
                  .gradient,
              isA<LinearGradient>(),
            );
          }
        },
      );
    }

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
      'SessionOverviewCharacterPanel rounds positive vitals upward for display',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(
            vitalValues: Fixtures.buildCharacterVitals(
              health: Fixtures.buildCharacterVital(current: 19.01, max: 40),
              magicka: Fixtures.buildCharacterVital(current: 19.31, max: 40),
              stamina: Fixtures.buildCharacterVital(current: 19.99, max: 40),
            ),
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(
          tester
              .widget<Text>(
                find.byKey(const Key('session-overview-health-current')),
              )
              .data,
          '20',
        );
        expect(
          tester
              .widget<Text>(
                find.byKey(const Key('session-overview-magicka-current')),
              )
              .data,
          '20',
        );
        expect(
          tester
              .widget<Text>(
                find.byKey(const Key('session-overview-stamina-current')),
              )
              .data,
          '20',
        );
        expect(find.text('19.01'), findsNothing);
        expect(find.text('19.31'), findsNothing);
        expect(find.text('19.99'), findsNothing);
      },
    );

    testWidgets(
      'SessionOverviewCharacterPanel keeps integral values unchanged and omits negative vitals',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(
            vitalValues: Fixtures.buildCharacterVitals(
              health: Fixtures.buildCharacterVital(current: 20, max: 40),
              magicka: Fixtures.buildCharacterVital(current: -0.2, max: 40),
              stamina: Fixtures.buildCharacterVital(current: 50, max: 90),
            ),
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(
          tester
              .widget<Text>(
                find.byKey(const Key('session-overview-health-current')),
              )
              .data,
          '20',
        );
        expect(
          tester
              .widget<Text>(
                find.byKey(const Key('session-overview-magicka-current')),
              )
              .data,
          ' ',
        );
        expect(find.text('0'), findsNothing);
        expect(find.text('-0.2'), findsNothing);
        expect(find.text('50'), findsOneWidget);
      },
    );

    testWidgets(
      'SessionOverviewCharacterPanel omits non-finite vitals from display',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(
            vitalValues: Fixtures.buildCharacterVitals(
              health: Fixtures.buildCharacterVital(
                current: double.nan,
                max: 100,
              ),
              magicka: Fixtures.buildCharacterVital(
                current: double.infinity,
                max: 100,
              ),
              stamina: Fixtures.buildCharacterVital(
                current: double.negativeInfinity,
                max: 100,
              ),
            ),
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        for (final String vital in <String>['health', 'magicka', 'stamina']) {
          expect(
            tester
                .widget<Text>(
                  find.byKey(Key('session-overview-$vital-current')),
                )
                .data,
            ' ',
          );
        }
        expect(find.textContaining('NaN'), findsNothing);
        expect(find.textContaining('Infinity'), findsNothing);
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
            const String longName =
                'A modded character name long enough to require hero ellipsis';
            await pumpDovahThemedWidget(
              tester,
              _buildPanel(
                name: longName,
                supernaturalLabel: 'Vampire Lord · Werewolf',
              ),
              preset: preset,
              size: size,
            );

            final Text name = tester.widget(
              find.byKey(const Key('session-overview-character-name')),
            );
            expect(name.data, longName);
            expect(name.maxLines, 1);
            expect(name.overflow, TextOverflow.ellipsis);
            expect(name.softWrap, isFalse);
            if (size.width == dovahResponsiveTestSizes.first.width) {
              final RenderParagraph paragraph = tester.renderObject(
                find.byKey(const Key('session-overview-character-name')),
              );
              expect(paragraph.didExceedMaxLines, isTrue);
            }
            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });
}
