import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_quest.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';
import '../../../../fixtures/fixtures.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkStateStatus, TrackedQuest;

/// Builds the single quest panel from one SDK collection.
SessionOverviewQuestPanel _buildPanel({
  List<TrackedQuest>? quests,
  DovahLinkStateStatus status = DovahLinkStateStatus.synchronized,
}) => SessionOverviewQuestPanel(
  viewData: Fixtures.buildSessionOverviewQuestViewData(
    value: quests == null ? null : Fixtures.buildTrackedQuests(quests: quests),
    status: status,
  ),
);

/// Exercises the single quest panel's plurality and text layout.
void main() {
  group('SessionOverviewQuestPanel displays', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'SessionOverviewQuestPanel uses the $preset side-panel texture',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            _buildPanel(quests: <TrackedQuest>[Fixtures.buildTrackedQuest()]),
            preset: preset,
            size: const Size(1280, 720),
          );

          final DovahPanel panel = tester.widget(
            find.byKey(const Key('session-overview-quest-panel')),
          );
          expect(
            panel.overlayGradient,
            preset == DovahThemePreset.dovah ? isA<RadialGradient>() : isNull,
          );
          expect(
            panel.leadingAccent,
            dovahThemeDataFor(preset).extension<DovahThemeTokens>()!.ember,
          );
        },
      );
    }

    testWidgets('SessionOverviewQuestPanel displays the approved empty copy', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        _buildPanel(quests: <TrackedQuest>[]),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );

      expect(find.text('Tracked quest'), findsOneWidget);
      expect(find.text('Journal'), findsOneWidget);
      expect(find.text('NO QUEST TRACKED'), findsOneWidget);
      expect(find.text('No path is marked.'), findsOneWidget);
    });

    testWidgets(
      'SessionOverviewQuestPanel displays a real title and objective',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(
            quests: <TrackedQuest>[
              Fixtures.buildTrackedQuest(
                title: 'The Horn of Jurgen Windcaller',
                objectives: [
                  Fixtures.buildQuestObjective(
                    text: 'Retrieve the Horn from Ustengrav.',
                  ),
                ],
              ),
            ],
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(find.text('The Horn of Jurgen Windcaller'), findsOneWidget);
        expect(find.text('Retrieve the Horn from Ustengrav.'), findsOneWidget);
      },
    );

    testWidgets(
      'SessionOverviewQuestPanel shows plurality without picking a quest',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(
            quests: <TrackedQuest>[
              Fixtures.buildTrackedQuest(title: 'First quest'),
              Fixtures.buildTrackedQuest(questId: 2, title: 'Second quest'),
            ],
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(find.text('2 QUESTS TRACKED'), findsOneWidget);
        expect(find.text('Multiple paths remain open.'), findsOneWidget);
        expect(find.text('First quest'), findsNothing);
        expect(find.text('Second quest'), findsNothing);
      },
    );

    testWidgets(
      'SessionOverviewQuestPanel keeps geometry with no usable value',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(quests: <TrackedQuest>[Fixtures.buildTrackedQuest()]),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );
        final Size populatedSize = tester.getSize(
          find.byKey(const Key('session-overview-quest-panel')),
        );

        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: Scaffold(
              body: _buildPanel(status: DovahLinkStateStatus.failed),
            ),
          ),
        );

        expect(
          tester.getSize(find.byKey(const Key('session-overview-quest-panel'))),
          populatedSize,
        );
        expect(find.text('Unavailable'), findsNothing);
        expect(find.text('Loading'), findsNothing);
        expect(find.text('Test Quest'), findsNothing);
      },
    );

    testWidgets('SessionOverviewQuestPanel retains stale content subdued', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        _buildPanel(
          quests: <TrackedQuest>[Fixtures.buildTrackedQuest()],
          status: DovahLinkStateStatus.stale,
        ),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );

      expect(find.text('Test Quest'), findsOneWidget);
      expect(find.text('Complete the objective'), findsOneWidget);
      final Text title = tester.widget(
        find.byKey(const Key('session-overview-quest-title')),
      );
      expect(title.style?.fontStyle, FontStyle.italic);
    });

    testWidgets(
      'SessionOverviewQuestPanel uses the theme material while recovering',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(
            quests: <TrackedQuest>[Fixtures.buildTrackedQuest()],
            status: DovahLinkStateStatus.recovering,
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        final DovahPanel panel = tester.widget(
          find.byKey(const Key('session-overview-quest-panel')),
        );
        expect(panel.raised, isTrue);
        expect(find.text('Complete the objective'), findsOneWidget);
        expect(find.textContaining('Recovering'), findsNothing);
      },
    );
  });

  group('SessionOverviewQuestPanel lays out', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'SessionOverviewQuestPanel wraps long localized copy under $preset at $size',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              _buildPanel(
                quests: <TrackedQuest>[
                  Fixtures.buildTrackedQuest(
                    title:
                        'A very long modded quest title that needs to wrap over more than one line',
                    objectives: [
                      Fixtures.buildQuestObjective(
                        text:
                            'Find the hidden passage beyond the old stone bridge and return with the map.',
                      ),
                    ],
                  ),
                ],
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

  group('SessionOverviewQuestPanel preserves both accent rules', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'SessionOverviewQuestPanel keeps the outer rail and inner quest rule under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            _buildPanel(quests: <TrackedQuest>[Fixtures.buildTrackedQuest()]),
            preset: preset,
            size: const Size(1280, 720),
          );

          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final ColoredBox outerAccent = tester.widget(
            find.byKey(const Key('dovah-surface-leading-accent')),
          );
          final Container innerRule = tester.widget(
            find.byKey(const Key('session-overview-quest-inner-rule')),
          );

          expect(outerAccent.color, tokens.ember);
          expect(
            (innerRule.decoration! as BoxDecoration).gradient,
            isA<LinearGradient>(),
          );
          final LinearGradient innerGradient =
              (innerRule.decoration! as BoxDecoration).gradient!
                  as LinearGradient;
          expect(innerGradient.colors, [tokens.ember, tokens.signal]);
        },
      );
    }
  });
}
