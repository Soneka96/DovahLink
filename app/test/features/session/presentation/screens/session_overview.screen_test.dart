import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/session/presentation/screens/session_overview.screen.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_overview.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_environment_background.widget.dart';
import '../../../../fixtures/fixtures.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterLevelState,
        CharacterSupernaturalTraitsState,
        DovahLinkStateStatus,
        GameTimeState,
        PlayerLocationState,
        StateSynchronization,
        TrackedQuest;

/// Mocks the Session Overview's Redux subscription.
class MockStore extends Mock implements Store<AppState> {}

/// Mocks the Redux-backed ViewModel consumed by the screen.
class MockSessionOverviewViewModel extends Mock
    implements SessionOverviewViewModel {}

/// Builds a shared contrast ratio check for context-line accessibility tests.
double _contrastRatio(Color first, Color second) {
  final double firstLuminance = first.computeLuminance();
  final double secondLuminance = second.computeLuminance();
  final double lighter = firstLuminance > secondLuminance
      ? firstLuminance
      : secondLuminance;
  final double darker = firstLuminance > secondLuminance
      ? secondLuminance
      : firstLuminance;
  return (lighter + 0.05) / (darker + 0.05);
}

/// Exercises the Overview screen through its ViewModel contract.
void main() {
  late MockStore store;
  late MockSessionOverviewViewModel viewModel;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    viewModel = MockSessionOverviewViewModel();
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => store.state).thenReturn(AppState.initial());
    when(() => viewModel.contextLine).thenReturn(null);
    when(() => viewModel.isContextStale).thenReturn(false);
    when(() => viewModel.isContextRecovering).thenReturn(false);
    when(() => viewModel.characterName).thenReturn(null);
    when(() => viewModel.characterIdentity).thenReturn(
      const StateSynchronization<CharacterIdentityState?>.notSubscribed(),
    );
    when(() => viewModel.characterRace).thenReturn(null);
    when(() => viewModel.characterLevelText).thenReturn(null);
    when(() => viewModel.characterLevel).thenReturn(
      const StateSynchronization<CharacterLevelState>.notSubscribed(),
    );
    when(() => viewModel.supernaturalLabel).thenReturn(null);
    when(() => viewModel.supernaturalTraits).thenReturn(
      const StateSynchronization<
        CharacterSupernaturalTraitsState?
      >.notSubscribed(),
    );
    when(() => viewModel.playerLocation).thenReturn(
      const StateSynchronization<PlayerLocationState?>.notSubscribed(),
    );
    when(
      () => viewModel.gameTime,
    ).thenReturn(const StateSynchronization<GameTimeState?>.notSubscribed());
    when(() => viewModel.vitalsViewData).thenReturn(
      Fixtures.buildSessionOverviewVitalsViewData(
        status: DovahLinkStateStatus.notSubscribed,
      ),
    );
    when(() => viewModel.questsViewData).thenReturn(
      Fixtures.buildSessionOverviewQuestViewData(
        status: DovahLinkStateStatus.notSubscribed,
      ),
    );
    sl.registerFactoryParam<SessionOverviewViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => viewModel,
    );
  });

  tearDown(() async {
    reset(viewModel);
    reset(store);
    await sl.reset();
  });

  Widget buildWidget({
    DovahThemePreset preset = DovahThemePreset.dovah,
    TextScaler textScaler = TextScaler.noScaling,
  }) => StoreProvider<AppState>(
    store: store,
    child: MaterialApp(
      key: ValueKey<DovahThemePreset>(preset),
      theme: dovahThemeDataFor(preset),
      home: Builder(
        builder: (BuildContext context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(textScaler: textScaler),
          child: const Scaffold(
            body: DovahEnvironmentBackground(child: SessionOverviewScreen()),
          ),
        ),
      ),
    ),
  );

  void stubRepresentativeOverview() {
    when(
      () => viewModel.contextLine,
    ).thenReturn('Gonçalo · The Bannered Mare · 4E 201, 6:42 PM');
    when(() => viewModel.characterName).thenReturn('Gonçalo');
    when(() => viewModel.characterIdentity).thenReturn(
      Fixtures.buildStateSynchronization<CharacterIdentityState?>(
        value: Fixtures.buildCharacterIdentity(name: 'Gonçalo', race: 'Nord'),
      ),
    );
    when(() => viewModel.characterRace).thenReturn('Nord');
    when(() => viewModel.characterLevelText).thenReturn('Level 43');
    when(() => viewModel.characterLevel).thenReturn(
      Fixtures.buildStateSynchronization<CharacterLevelState>(
        value: Fixtures.buildCharacterLevel(value: 43),
      ),
    );
    when(() => viewModel.supernaturalLabel).thenReturn('Werewolf');
    when(() => viewModel.supernaturalTraits).thenReturn(
      Fixtures.buildStateSynchronization<CharacterSupernaturalTraitsState?>(
        value: Fixtures.buildSupernaturalTraits(hasWerewolfForm: true),
      ),
    );
    when(() => viewModel.playerLocation).thenReturn(
      Fixtures.buildStateSynchronization<PlayerLocationState?>(
        value: Fixtures.buildPlayerLocation(),
      ),
    );
    when(() => viewModel.gameTime).thenReturn(
      Fixtures.buildStateSynchronization<GameTimeState?>(
        value: Fixtures.buildGameTime(year: 201, hour: 18, minute: 42),
      ),
    );
    when(() => viewModel.vitalsViewData).thenReturn(
      Fixtures.buildSessionOverviewVitalsViewData(
        value: Fixtures.buildCharacterVitals(
          health: Fixtures.buildCharacterVital(current: 86, max: 100),
          magicka: Fixtures.buildCharacterVital(current: 62, max: 100),
          stamina: Fixtures.buildCharacterVital(current: 74, max: 100),
        ),
      ),
    );
    when(() => viewModel.questsViewData).thenReturn(
      Fixtures.buildSessionOverviewQuestViewData(
        value: Fixtures.buildTrackedQuests(
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
      ),
    );
  }

  group('SessionOverviewScreen displays', () {
    testWidgets(
      'SessionOverviewScreen displays the supplied context and Overview values',
      (WidgetTester tester) async {
        stubRepresentativeOverview();
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());

        expect(find.text('Current play session'), findsOneWidget);
        expect(
          find.text('Gonçalo · The Bannered Mare · 4E 201, 6:42 PM'),
          findsOneWidget,
        );
        expect(find.text('Gonçalo'), findsOneWidget);
        expect(find.text('Nord · Level 43 · Werewolf'), findsOneWidget);
        expect(find.text('320 XP'), findsNothing);
        expect(find.text('86'), findsOneWidget);
        expect(find.text('62'), findsOneWidget);
        expect(find.text('74'), findsOneWidget);
        expect(find.text('86%'), findsOneWidget);
        expect(find.text('The Horn of Jurgen Windcaller'), findsOneWidget);
        expect(find.text('Retrieve the Horn from Ustengrav.'), findsOneWidget);
        expect(find.text('Updated just now'), findsNothing);
        expect(find.textContaining('Unavailable'), findsNothing);
        expect(find.textContaining('Loading'), findsNothing);
      },
    );

    testWidgets(
      'SessionOverviewScreen omits unknown content without placeholders',
      (WidgetTester tester) async {
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());

        expect(find.text('Current play session'), findsOneWidget);
        expect(find.byKey(const Key('session-overview-context')), findsNothing);
        expect(find.text('Unknown'), findsNothing);
        expect(find.text('Loading'), findsNothing);
        expect(find.text('Unavailable'), findsNothing);
        expect(find.text('0'), findsNothing);
        expect(
          find.byKey(const Key('session-overview-character-panel')),
          findsOneWidget,
        );
        expect(
          find.byKey(const Key('session-overview-quest-panel')),
          findsOneWidget,
        );
        expect(
          find.byKey(const Key('session-overview-vitals-panel')),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'SessionOverviewScreen exposes the page title and context to semantics',
      (WidgetTester tester) async {
        stubRepresentativeOverview();
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          setDovahTestWindow(tester, const Size(1280, 720));
          await tester.pumpWidget(buildWidget());

          final SemanticsNode title = tester.getSemantics(
            find.byKey(const Key('session-overview-title')),
          );
          final SemanticsNode context = tester.getSemantics(
            find.byKey(const Key('session-overview-context')),
          );
          expect(title.label, 'Current play session');
          expect(title.flagsCollection.isHeader, isTrue);
          expect(
            context.label,
            'Gonçalo · The Bannered Mare · 4E 201, 6:42 PM',
          );
        } finally {
          handle.dispose();
        }
      },
    );
  });

  group('SessionOverviewScreen styles context synchronization', () {
    for (final (
          String description,
          DovahLinkStateStatus identityStatus,
          DovahLinkStateStatus locationStatus,
          bool isStale,
          bool isRecovering,
        )
        in <(String, DovahLinkStateStatus, DovahLinkStateStatus, bool, bool)>[
          (
            'stale character before recovering place',
            DovahLinkStateStatus.stale,
            DovahLinkStateStatus.recovering,
            true,
            false,
          ),
          (
            'failed character before recovering place',
            DovahLinkStateStatus.failed,
            DovahLinkStateStatus.recovering,
            true,
            false,
          ),
          (
            'recovering place when character is synchronized',
            DovahLinkStateStatus.synchronized,
            DovahLinkStateStatus.recovering,
            false,
            true,
          ),
          (
            'synchronized context',
            DovahLinkStateStatus.synchronized,
            DovahLinkStateStatus.synchronized,
            false,
            false,
          ),
          (
            'unavailable context',
            DovahLinkStateStatus.unavailable,
            DovahLinkStateStatus.unavailable,
            false,
            false,
          ),
          (
            'not-subscribed context',
            DovahLinkStateStatus.notSubscribed,
            DovahLinkStateStatus.notSubscribed,
            false,
            false,
          ),
        ]) {
      testWidgets(
        'SessionOverviewScreen styles $description without status copy',
        (WidgetTester tester) async {
          when(
            () => viewModel.contextLine,
          ).thenReturn('Aela · The Bannered Mare');
          when(() => viewModel.characterName).thenReturn('Aela');
          when(() => viewModel.characterIdentity).thenReturn(
            Fixtures.buildStateSynchronization<CharacterIdentityState?>(
              value: Fixtures.buildCharacterIdentity(name: 'Aela'),
              status: identityStatus,
            ),
          );
          when(() => viewModel.isContextStale).thenReturn(isStale);
          when(() => viewModel.isContextRecovering).thenReturn(isRecovering);
          when(() => viewModel.playerLocation).thenReturn(
            Fixtures.buildStateSynchronization<PlayerLocationState?>(
              value: Fixtures.buildPlayerLocation(),
              status: locationStatus,
            ),
          );
          setDovahTestWindow(tester, const Size(1280, 720));
          await tester.pumpWidget(buildWidget());

          final DovahThemeTokens tokens = dovahThemeDataFor(
            DovahThemePreset.dovah,
          ).extension<DovahThemeTokens>()!;
          final Text context = tester.widget(
            find.byKey(const Key('session-overview-context')),
          );
          final Color expected = isRecovering
              ? tokens.textPrimary
              : isStale
              ? tokens.textPrimary
              : tokens.textMuted;

          expect(context.style?.color, expected);
          expect(context.style?.fontStyle, isStale ? FontStyle.italic : isNull);
          expect(find.textContaining('Stale'), findsNothing);
          expect(find.textContaining('Recovering'), findsNothing);
        },
      );
    }

    testWidgets('SessionOverviewScreen keeps stale context text readable', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.contextLine).thenReturn('Aela · The Bannered Mare');
      when(() => viewModel.characterName).thenReturn('Aela');
      when(() => viewModel.characterIdentity).thenReturn(
        Fixtures.buildStateSynchronization<CharacterIdentityState?>(
          value: Fixtures.buildCharacterIdentity(name: 'Aela'),
          status: DovahLinkStateStatus.stale,
        ),
      );
      when(() => viewModel.isContextStale).thenReturn(true);
      for (final DovahThemePreset preset in DovahThemePreset.values) {
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget(preset: preset));
        final DovahThemeTokens tokens = dovahThemeDataFor(
          preset,
        ).extension<DovahThemeTokens>()!;
        final Text context = tester.widget(
          find.byKey(const Key('session-overview-context')),
        );

        final List<Color> contextBackgrounds = switch (preset) {
          DovahThemePreset.hearth =>
            (dovahThemeDataFor(
                      preset,
                    ).extension<DovahThemeMaterials>()!.surface.layers.first
                    as DovahLinearLayer)
                .colors,
          _ => [tokens.background],
        };
        for (final Color background in contextBackgrounds) {
          expect(
            _contrastRatio(context.style!.color!, background),
            greaterThanOrEqualTo(4.5),
            reason:
                '${preset.name}: fg=${context.style!.color}, bg=$background',
          );
        }
      }
    });
  });

  group('SessionOverviewScreen lays out', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'SessionOverviewScreen renders $preset at $size without overflow',
          (WidgetTester tester) async {
            setDovahTestWindow(tester, size);
            await tester.pumpWidget(buildWidget(preset: preset));

            expect(
              find.byKey(const Key('session-overview-page')),
              findsOneWidget,
            );
            expect(tester.takeException(), isNull);
          },
        );
      }
    }

    testWidgets(
      'SessionOverviewScreen respects increased text scaling without overflow',
      (WidgetTester tester) async {
        stubRepresentativeOverview();
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(
          buildWidget(textScaler: const TextScaler.linear(1.5)),
        );

        expect(tester.takeException(), isNull);
      },
    );
  });
}
