import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/viewmodels/device_identity_section.viewmodel.dart';
import 'package:dovahlink_client/features/session/presentation/screens/session_overview.screen.dart';
import 'package:dovahlink_client/features/session/presentation/screens/session_shell.screen.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_overview.viewmodel.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_shell.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';
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
        StateSynchronization;

/// Mocks the Session Shell's Redux store subscription.
class MockStore extends Mock implements Store<AppState> {}

/// Mocks the Overview ViewModel embedded in the Session Shell.
class MockSessionOverviewViewModel extends Mock
    implements SessionOverviewViewModel {}

/// Mocks the appearance section's ViewModel inside Settings.
class MockAppearanceSectionViewModel extends Mock
    implements AppearanceSectionViewModel {}

/// Mocks the device-identity section's Redux projection.
class MockDeviceIdentitySectionViewModel extends Mock
    implements DeviceIdentitySectionViewModel {}

/// Exercises the minimal Session Shell using its ViewModel contract.
void main() {
  late MockStore store;
  late MockSessionOverviewViewModel overviewViewModel;
  late MockAppearanceSectionViewModel appearanceViewModel;
  late MockDeviceIdentitySectionViewModel deviceIdentityViewModel;
  late SessionShellViewModel viewModel;
  int backCalls = 0;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    overviewViewModel = MockSessionOverviewViewModel();
    appearanceViewModel = MockAppearanceSectionViewModel();
    deviceIdentityViewModel = MockDeviceIdentitySectionViewModel();
    backCalls = 0;
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => store.state).thenReturn(AppState.initial());
    when(() => overviewViewModel.contextLine).thenReturn(null);
    when(() => overviewViewModel.characterName).thenReturn(null);
    when(() => overviewViewModel.characterIdentity).thenReturn(
      const StateSynchronization<CharacterIdentityState?>.notSubscribed(),
    );
    when(() => overviewViewModel.characterRace).thenReturn(null);
    when(() => overviewViewModel.characterLevelText).thenReturn(null);
    when(() => overviewViewModel.characterLevelLabel).thenReturn(null);
    when(() => overviewViewModel.characterLevel).thenReturn(
      const StateSynchronization<CharacterLevelState>.notSubscribed(),
    );
    when(() => overviewViewModel.supernaturalLabel).thenReturn(null);
    when(() => overviewViewModel.supernaturalTraits).thenReturn(
      const StateSynchronization<
        CharacterSupernaturalTraitsState?
      >.notSubscribed(),
    );
    when(() => overviewViewModel.playerLocation).thenReturn(
      const StateSynchronization<PlayerLocationState?>.notSubscribed(),
    );
    when(
      () => overviewViewModel.gameTime,
    ).thenReturn(const StateSynchronization<GameTimeState?>.notSubscribed());
    when(() => overviewViewModel.vitalsViewData).thenReturn(
      Fixtures.buildSessionOverviewVitalsViewData(
        status: DovahLinkStateStatus.notSubscribed,
      ),
    );
    when(() => overviewViewModel.questsViewData).thenReturn(
      Fixtures.buildSessionOverviewQuestViewData(
        status: DovahLinkStateStatus.notSubscribed,
      ),
    );
    when(
      () => appearanceViewModel.activePreset,
    ).thenReturn(DovahThemePreset.dovah);
    when(
      () => appearanceViewModel.onSelectPreset,
    ).thenReturn((DovahThemePreset _) {});
    when(() => deviceIdentityViewModel.displayName).thenReturn('Gaming PC');
    when(() => deviceIdentityViewModel.loadFailure).thenReturn(null);
    when(() => deviceIdentityViewModel.isSaving).thenReturn(false);
    when(() => deviceIdentityViewModel.saveFailure).thenReturn(null);
    when(() => deviceIdentityViewModel.remoteRenameStatus).thenReturn(null);
    when(() => deviceIdentityViewModel.remoteHostId).thenReturn(null);
    when(() => deviceIdentityViewModel.admittedHostId).thenReturn(null);
    when(() => deviceIdentityViewModel.onSave).thenReturn((String _) {});
    sl.registerFactoryParam<AppearanceSectionViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => appearanceViewModel,
    );
    sl.registerFactoryParam<SessionOverviewViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => overviewViewModel,
    );
    sl.registerFactoryParam<
      DeviceIdentitySectionViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => deviceIdentityViewModel);
    viewModel = SessionShellViewModel(
      host: Fixtures.buildHostCardViewData(
        host: Fixtures.buildHost(
          hostId: 'selected-host',
          displayName: 'Living Room PC',
        ),
        title: 'Living Room PC',
        detail: 'living-room.local:58231',
        state: DovahConnectionCardState.connected,
      ),
      onBack: () => backCalls++,
      characterName: 'Gonçalo',
      characterLevelLabel: 'Level 43 (320 XP)',
    );
    sl.registerFactoryParam<SessionShellViewModel, Store<AppState>, String>((
      Store<AppState> _,
      String _,
    ) {
      return viewModel;
    });
  });

  tearDown(() async {
    await sl.reset();
  });

  Widget buildWidget({
    DovahThemePreset preset = DovahThemePreset.dovah,
    TextScaler textScaler = TextScaler.noScaling,
  }) => StoreProvider<AppState>(
    store: store,
    child: MaterialApp(
      theme: dovahThemeDataFor(preset),
      home: Builder(
        builder: (BuildContext context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(textScaler: textScaler),
          child: const SessionShellScreen(hostId: 'selected-host'),
        ),
      ),
    ),
  );

  group('SessionShellScreen displays', () {
    testWidgets('SessionShellScreen shows the real Host and connected state', (
      WidgetTester tester,
    ) async {
      setDovahTestWindow(tester, const Size(1280, 720));
      await tester.pumpWidget(buildWidget());

      expect(find.text('Living Room PC'), findsOneWidget);
      expect(
        find.text('Skyrim SE · Gonçalo · Level 43 (320 XP)'),
        findsOneWidget,
      );
      expect(find.text('living-room.local:58231'), findsNothing);
      expect(find.text('Connected'), findsOneWidget);
      expect(find.byType(DovahSigil), findsOneWidget);
      expect(find.byTooltip('Notifications'), findsOneWidget);
      expect(find.byTooltip('Settings'), findsOneWidget);
      expect(
        find.byKey(const Key('session-shell-Overview-tab')),
        findsOneWidget,
      );
      expect(find.byType(SessionOverviewScreen), findsOneWidget);
      expect(find.text('Current play session'), findsOneWidget);
    });

    testWidgets('SessionShellScreen styles stale summary values without copy', (
      WidgetTester tester,
    ) async {
      viewModel = SessionShellViewModel(
        host: Fixtures.buildHostCardViewData(
          state: DovahConnectionCardState.connected,
        ),
        onBack: () => backCalls++,
        characterName: 'Gonçalo',
        characterLevelLabel: 'Level 43 (320 XP)',
        identityStatus: DovahLinkStateStatus.failed,
        levelStatus: DovahLinkStateStatus.recovering,
      );

      await tester.pumpWidget(buildWidget());

      final DovahThemeTokens tokens = dovahThemeDataFor(
        DovahThemePreset.dovah,
      ).extension<DovahThemeTokens>()!;
      final Text summary = tester.widget(
        find.byKey(const Key('session-shell-character-summary')),
      );
      expect(summary.data, 'Skyrim SE · Gonçalo · Level 43 (320 XP)');
      expect(summary.style?.color, tokens.textMuted);
      expect(summary.style?.fontStyle, FontStyle.italic);
      expect(find.textContaining('Failed'), findsNothing);
      expect(find.textContaining('Recovering'), findsNothing);
    });

    testWidgets('SessionShellScreen emphasizes a recovering summary quietly', (
      WidgetTester tester,
    ) async {
      viewModel = SessionShellViewModel(
        host: Fixtures.buildHostCardViewData(
          state: DovahConnectionCardState.connected,
        ),
        onBack: () => backCalls++,
        characterName: 'Gonçalo',
        characterLevelLabel: 'Level 43 (320 XP)',
        identityStatus: DovahLinkStateStatus.synchronized,
        levelStatus: DovahLinkStateStatus.recovering,
      );

      await tester.pumpWidget(buildWidget());

      final DovahThemeTokens tokens = dovahThemeDataFor(
        DovahThemePreset.dovah,
      ).extension<DovahThemeTokens>()!;
      final Text summary = tester.widget(
        find.byKey(const Key('session-shell-character-summary')),
      );
      expect(summary.style?.color, tokens.textPrimary);
      expect(summary.style?.fontStyle, isNull);
    });

    testWidgets('SessionShellScreen navigates to prototype placeholders', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(buildWidget());
        expect(
          tester.getSemantics(
            find.byKey(const Key('session-shell-Overview-tab')),
          ),
          isSemantics(isButton: true, isSelected: true),
        );

        for (final (String tab, String title) in [
          ('Map', 'World Map'),
          ('Quests', 'Quest Journal'),
          ('Inventory', 'Inventory'),
          ('Character', 'Character'),
        ]) {
          await tester.tap(find.byKey(Key('session-shell-$tab-tab')));
          await tester.pumpAndSettle();

          expect(
            tester.getSemantics(find.byKey(Key('session-shell-$tab-tab'))),
            isSemantics(isButton: true, isSelected: true),
          );
          expect(
            find.byKey(const Key('session-shell-placeholder-page')),
            findsOneWidget,
          );
          expect(
            find.byKey(const Key('session-placeholder-title')),
            findsOneWidget,
          );
          expect(
            tester
                .widget<Text>(
                  find.byKey(const Key('session-placeholder-title')),
                )
                .data,
            title,
          );
          expect(find.byType(SessionOverviewScreen), findsNothing);
        }

        await tester.tap(find.byKey(const Key('session-shell-Overview-tab')));
        await tester.pumpAndSettle();
        expect(find.byType(SessionOverviewScreen), findsOneWidget);
      } finally {
        handle.dispose();
      }
    });

    testWidgets('SessionShellScreen reflects SDK recovery state', (
      WidgetTester tester,
    ) async {
      viewModel = SessionShellViewModel(
        host: Fixtures.buildHostCardViewData(
          state: DovahConnectionCardState.reconnecting,
        ),
        onBack: () => backCalls++,
      );

      await tester.pumpWidget(buildWidget());

      expect(find.text('Reconnecting…'), findsOneWidget);
    });

    testWidgets('SessionShellScreen gives a missing Host a safe fallback', (
      WidgetTester tester,
    ) async {
      viewModel = SessionShellViewModel(host: null, onBack: () => backCalls++);

      await tester.pumpWidget(buildWidget());

      expect(find.text('Host unavailable'), findsOneWidget);
      expect(find.text('Unknown'), findsOneWidget);
    });

    for (final DovahConnectionCardState state
        in DovahConnectionCardState.values) {
      testWidgets('SessionShellScreen colors ${state.label} status correctly', (
        WidgetTester tester,
      ) async {
        viewModel = SessionShellViewModel(
          host: Fixtures.buildHostCardViewData(state: state),
          onBack: () => backCalls++,
        );
        await tester.pumpWidget(buildWidget());

        final DovahThemeTokens tokens = dovahThemeDataFor(
          DovahThemePreset.dovah,
        ).extension<DovahThemeTokens>()!;
        final Color expected = switch (state) {
          DovahConnectionCardState.connected ||
          DovahConnectionCardState.available => tokens.success,
          DovahConnectionCardState.reconnecting ||
          DovahConnectionCardState.connecting ||
          DovahConnectionCardState.repair => tokens.warning,
          DovahConnectionCardState.offline => tokens.statusOffline,
          DovahConnectionCardState.checking ||
          DovahConnectionCardState.unknown => tokens.textMuted,
        };
        final Text status = tester.widget(
          find.byKey(const Key('session-shell-status')),
        );
        final Container dot = tester.widget(
          find.byKey(const Key('session-shell-status-dot')),
        );

        expect(status.style?.color, expected);
        expect((dot.decoration! as BoxDecoration).color, expected);
      });
    }
  });

  group('SessionShellScreen matches the prototype action breakpoint', () {
    for (final (Size size, bool showsNotifications) in [
      (const Size(900, 720), false),
      (const Size(901, 720), true),
    ]) {
      testWidgets(
        'SessionShellScreen notifications at width ${size.width} are $showsNotifications',
        (WidgetTester tester) async {
          setDovahTestWindow(tester, size);
          await tester.pumpWidget(buildWidget());

          expect(
            find.byTooltip('Notifications'),
            showsNotifications ? findsOneWidget : findsNothing,
          );
        },
      );
    }
  });

  group('SessionShellScreen calls callbacks', () {
    testWidgets('SessionShellScreen Back returns to Connections', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());
      await tester.tap(find.byKey(const Key('session-shell-back-button')));

      expect(backCalls, 1);
    });

    testWidgets('SessionShellScreen opens the shared Settings dialog', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());
      await tester.tap(find.byTooltip('Settings'));
      await tester.pumpAndSettle();

      expect(find.text('Settings'), findsOneWidget);
      expect(find.text('This device'), findsOneWidget);
    });

    testWidgets('SessionShellScreen keeps Notifications prototype-only', (
      WidgetTester tester,
    ) async {
      setDovahTestWindow(tester, const Size(1280, 720));
      await tester.pumpWidget(buildWidget());
      await tester.tap(find.byTooltip('Notifications'));
      await tester.pumpAndSettle();

      expect(find.byTooltip('Notifications'), findsOneWidget);
      expect(find.byType(SessionShellScreen), findsOneWidget);
      expect(find.text('Settings'), findsNothing);
      expect(backCalls, 0);
    });
  });

  group('SessionShellScreen meets accessibility recommended guidelines', () {
    testWidgets('SessionShellScreen gives Back an accessible target', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(buildWidget());

        expect(
          tester.getSemantics(find.byKey(const Key('dovah-button-semantics'))),
          isSemantics(label: 'Connections', isButton: true, hasTapAction: true),
        );
        expect(
          tester
              .getSize(find.byKey(const Key('session-shell-back-button')))
              .height,
          greaterThanOrEqualTo(48),
        );
      } finally {
        handle.dispose();
      }
    });
  });

  group('SessionShellScreen lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'SessionShellScreen renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            setDovahTestWindow(tester, size);
            await tester.pumpWidget(buildWidget(preset: preset));

            expect(tester.takeException(), isNull);
            expect(
              tester
                  .getSize(find.byKey(const Key('session-shell-back-button')))
                  .height,
              greaterThanOrEqualTo(48),
            );
          },
        );
      }
    }

    testWidgets(
      'SessionShellScreen keeps header controls usable with increased text scaling',
      (WidgetTester tester) async {
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(
          buildWidget(textScaler: const TextScaler.linear(1.5)),
        );

        expect(tester.takeException(), isNull);
        expect(
          tester
              .getSize(find.byKey(const Key('session-shell-back-button')))
              .height,
          greaterThanOrEqualTo(48),
        );
        expect(find.byTooltip('Settings'), findsOneWidget);
        await tester.tap(find.byTooltip('Settings'));
        await tester.pumpAndSettle();
        expect(find.text('This device'), findsOneWidget);
      },
    );

    for (final Size size in dovahResponsiveTestSizes) {
      final bool isCompact = size.height <= 620;
      testWidgets(
        'SessionShellScreen uses the ${isCompact ? 'compact' : 'regular'} header height at $size',
        (WidgetTester tester) async {
          setDovahTestWindow(tester, size);
          await tester.pumpWidget(buildWidget());

          final Size glyph = tester.getSize(
            find.byKey(const Key('session-shell-glyph')),
          );
          final Size back = tester.getSize(
            find.byKey(const Key('session-shell-back-button')),
          );
          final Size header = tester.getSize(
            find.byKey(const Key('session-shell-header')),
          );
          expect(glyph.width, DovahSessionMetrics.glyphSize);
          expect(header.height, isCompact ? 54 : 65);
          expect(back.height, greaterThanOrEqualTo(48));
          expect(back.height, lessThanOrEqualTo(header.height));
        },
      );
    }
  });
}
