import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/sections/appearance.section.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/models/host_card.model.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/connections.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/root_header.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_environment_background.widget.dart';
import '../../../../fixtures/fixtures.dart';

/// Mock ViewModel supplied to [ConnectionsScreen].
class MockConnectionsScreenViewModel extends Mock
    implements ConnectionsScreenViewModel {}

/// Mock ViewModel supplied to the [AppearanceSection] the screen's dialog shows.
class MockAppearanceSectionViewModel extends Mock
    implements AppearanceSectionViewModel {}

/// Mock Store supplied to the screen's [StoreConnector].
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [ConnectionsScreen]'s rendering, selection, appearance access, theming, and
/// accessibility through its ViewModel contract.
void main() {
  late MockStore store;
  late MockConnectionsScreenViewModel viewModel;
  late MockAppearanceSectionViewModel appearanceViewModel;
  late List<Host> selectedHosts;
  late List<DovahThemePreset> selectedPresets;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    viewModel = MockConnectionsScreenViewModel();
    appearanceViewModel = MockAppearanceSectionViewModel();
    selectedHosts = [];
    selectedPresets = [];

    when(() => store.state).thenReturn(AppState.initial());
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => viewModel.hostCards).thenReturn([Fixtures.buildHostCardModel()]);
    when(() => viewModel.onSelectHost).thenReturn(selectedHosts.add);
    when(
      () => appearanceViewModel.activePreset,
    ).thenReturn(DovahThemePreset.dovah);
    when(
      () => appearanceViewModel.onSelectPreset,
    ).thenReturn(selectedPresets.add);
    sl.registerFactoryParam<ConnectionsScreenViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => viewModel,
    );
    sl.registerFactoryParam<AppearanceSectionViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => appearanceViewModel,
    );
  });

  tearDown(() async {
    await sl.reset();
    reset(viewModel);
    reset(appearanceViewModel);
    reset(store);
  });

  /// Builds the screen with the mocked Store and ViewModel under [preset]. The Store provider sits
  /// above the [MaterialApp], as in the real app, so dialogs opened from the screen can reach it.
  Widget buildWidget({
    DovahThemePreset preset = DovahThemePreset.dovah,
    TextScaler? textScaler,
  }) => StoreProvider<AppState>(
    store: store,
    child: MaterialApp(
      theme: dovahThemeDataFor(preset),
      builder: (BuildContext context, Widget? child) {
        final MediaQueryData mediaQuery = MediaQuery.of(context);
        return MediaQuery(
          data: mediaQuery.copyWith(
            textScaler: textScaler ?? mediaQuery.textScaler,
          ),
          child: child!,
        );
      },
      home: const ConnectionsScreen(),
    ),
  );

  /// Sizes the test surface to [size] for the current test.
  Future<void> useSurface(WidgetTester tester, Size size) async {
    await tester.binding.setSurfaceSize(size);
    addTearDown(() => tester.binding.setSurfaceSize(null));
  }

  group('ConnectionsScreen contains widgets', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [
        Size(500, 600),
        Size(720, 480),
        Size(900, 560),
        Size(1280, 720),
        Size(1600, 900),
      ]) {
        testWidgets(
          'ConnectionsScreen contains the prototype hierarchy under $preset at $size without overflow',
          (WidgetTester tester) async {
            await useSurface(tester, size);
            await tester.pumpWidget(buildWidget(preset: preset));
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;

            expect(tester.takeException(), isNull);
            expect(find.byType(DovahEnvironmentBackground), findsOneWidget);
            expect(find.text('DOVAHLINK'), findsOneWidget);
            expect(find.text('SKYRIM COMPANION'), findsOneWidget);
            expect(find.text('YOUR SKYRIM'), findsOneWidget);
            expect(
              find.text(tokens.uppercaseLabels ? 'CONNECTIONS' : 'Connections'),
              findsOneWidget,
            );
            expect(
              find.text('Select an available PC to enter its game.'),
              findsOneWidget,
            );
            expect(find.text('Discover Skyrim'), findsOneWidget);
            expect(find.text('MY SKYRIM PCS'), findsOneWidget);
            expect(
              find.text(
                'Trusted PCs reconnect automatically when Skyrim becomes available.',
                skipOffstage: false,
              ),
              findsOneWidget,
            );
          },
        );
      }
    }

    testWidgets(
      'ConnectionsScreen contains one card per Host from its ViewModel',
      (WidgetTester tester) async {
        final Host second = Fixtures.buildHost(
          displayName: 'Second Host',
          uri: Uri.parse('ws://192.168.1.11:2000/'),
        );
        when(() => viewModel.hostCards).thenReturn([
          Fixtures.buildHostCardModel(),
          Fixtures.buildHostCardModel(
            host: second,
            title: 'Second Host',
            detail: '192.168.1.11:2000',
          ),
        ]);
        await useSurface(tester, const Size(1280, 900));

        await tester.pumpWidget(buildWidget());

        expect(find.byType(DovahConnectionCard), findsNWidgets(2));
        expect(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
          findsOneWidget,
        );
        expect(
          find.byKey(const Key('host-card-ws://192.168.1.11:2000/')),
          findsOneWidget,
        );
        expect(find.text('192.168.1.11:2000'), findsOneWidget);
      },
    );

    testWidgets('ConnectionsScreen contains no ListTile', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(find.byType(ListTile), findsNothing);
    });

    testWidgets(
      'ConnectionsScreen renders without cards or error when there are no Hosts',
      (WidgetTester tester) async {
        when(() => viewModel.hostCards).thenReturn(const <HostCardModel>[]);

        await tester.pumpWidget(buildWidget());

        expect(tester.takeException(), isNull);
        expect(find.text('MY SKYRIM PCS'), findsOneWidget);
        expect(find.byType(DovahConnectionCard), findsNothing);
      },
    );

    testWidgets(
      'ConnectionsScreen truncates a very long Host name at large text scale without overflow',
      (WidgetTester tester) async {
        final String longName = 'A very long Host name ' * 12;
        when(() => viewModel.hostCards).thenReturn([
          Fixtures.buildHostCardModel(
            host: Fixtures.buildHost(displayName: longName),
            title: longName,
          ),
        ]);
        await useSurface(tester, const Size(900, 560));

        await tester.pumpWidget(
          buildWidget(textScaler: const TextScaler.linear(2)),
        );

        expect(tester.takeException(), isNull);
      },
    );

    testWidgets(
      'ConnectionsScreen scrolls horizontally instead of overflowing below its minimum width',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(500, 600));

        await tester.pumpWidget(buildWidget());

        expect(tester.takeException(), isNull);
        expect(
          find.byWidgetPredicate(
            (Widget widget) =>
                widget is SingleChildScrollView &&
                widget.scrollDirection == Axis.horizontal,
          ),
          findsOneWidget,
        );
      },
    );

    for (final (Size, double, double) layout in const [
      (
        Size(500, 600),
        DovahThemeTokens.rootMinimumWidth -
            DovahThemeTokens.rootContentSideMargin * 2,
        DovahThemeTokens.rootMinimumWidth - 500,
      ),
      (Size(1600, 900), DovahThemeTokens.rootContentMaxWidth, 0),
    ]) {
      testWidgets(
        'ConnectionsScreen keeps its content width and horizontal scroll correct at ${layout.$1}',
        (WidgetTester tester) async {
          await useSurface(tester, layout.$1);
          await tester.pumpWidget(buildWidget());

          final Finder horizontalViewport = find.byWidgetPredicate(
            (Widget widget) =>
                widget is SingleChildScrollView &&
                widget.scrollDirection == Axis.horizontal,
          );
          final ScrollableState horizontalScroll = tester.state(
            find
                .descendant(
                  of: horizontalViewport,
                  matching: find.byType(Scrollable),
                )
                .first,
          );

          expect(tester.takeException(), isNull);
          expect(tester.getSize(find.byType(RootHeader)).width, layout.$2);
          expect(horizontalScroll.position.maxScrollExtent, layout.$3);
        },
      );
    }
  });

  group('ConnectionsScreen selects a Host', () {
    testWidgets(
      'ConnectionsScreen calls onSelectHost with the tapped card Host',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());

        await tester.tap(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
        );
        await tester.pump();

        expect(selectedHosts, [Fixtures.buildHost()]);
      },
    );

    testWidgets(
      'ConnectionsScreen passes the second Host, not the first, when the second card is tapped',
      (WidgetTester tester) async {
        final Host first = Fixtures.buildHost(
          displayName: 'First Host',
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final Host second = Fixtures.buildHost(
          displayName: 'Second Host',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        when(() => viewModel.hostCards).thenReturn([
          Fixtures.buildHostCardModel(host: first, title: 'First Host'),
          Fixtures.buildHostCardModel(host: second, title: 'Second Host'),
        ]);
        await useSurface(tester, const Size(1280, 900));
        await tester.pumpWidget(buildWidget());

        await tester.tap(find.byKey(const Key('host-card-ws://127.0.0.1:2/')));
        await tester.pump();

        expect(selectedHosts, [second]);
      },
    );

    testWidgets(
      'ConnectionsScreen distinguishes and selects Hosts with identical display names',
      (WidgetTester tester) async {
        final Host first = Fixtures.buildHost(
          displayName: 'Shared Host Name',
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final Host second = Fixtures.buildHost(
          displayName: 'Shared Host Name',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        when(() => viewModel.hostCards).thenReturn([
          Fixtures.buildHostCardModel(host: first, title: first.displayName),
          Fixtures.buildHostCardModel(host: second, title: second.displayName),
        ]);
        await useSurface(tester, const Size(1280, 900));
        await tester.pumpWidget(buildWidget());

        expect(find.text('Shared Host Name'), findsNWidgets(2));
        expect(
          find.byKey(const Key('host-card-ws://127.0.0.1:1/')),
          findsOneWidget,
        );
        expect(
          find.byKey(const Key('host-card-ws://127.0.0.1:2/')),
          findsOneWidget,
        );

        await tester.tap(find.byKey(const Key('host-card-ws://127.0.0.1:2/')));
        await tester.pump();

        expect(selectedHosts, [second]);
      },
    );

    testWidgets('ConnectionsScreen does not select a Host before a tap', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(selectedHosts, isEmpty);
    });

    testWidgets(
      'ConnectionsScreen does not select a Host when Discover Skyrim is tapped',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());

        await tester.tap(find.text('Discover Skyrim'), warnIfMissed: false);
        await tester.pump();

        expect(selectedHosts, isEmpty);
      },
    );
  });

  group('ConnectionsScreen opens the appearance UI', () {
    testWidgets(
      'ConnectionsScreen displays the appearance picker in a dialog when the appearance action is tapped',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());

        await tester.tap(find.byIcon(Icons.settings_outlined));
        await tester.pumpAndSettle();

        expect(find.byType(DovahDialog), findsOneWidget);
        expect(find.text('Appearance'), findsOneWidget);
        expect(find.byType(AppearanceSection), findsOneWidget);
        expect(find.text('Choose your Skyrim atmosphere'), findsOneWidget);
      },
    );

    testWidgets(
      'ConnectionsScreen opens the appearance picker when its header action is activated by keyboard',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();
        expect(
          find.byKey(const Key('dovah-icon-button-focus-outline')),
          findsOneWidget,
        );
        await tester.sendKeyEvent(LogicalKeyboardKey.enter);
        await tester.pumpAndSettle();

        expect(find.byType(DovahDialog), findsOneWidget);
        expect(find.byType(AppearanceSection), findsOneWidget);
      },
    );

    testWidgets(
      'ConnectionsScreen moves keyboard focus from Appearance to the Host card, skipping disabled Discover',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();
        expect(
          find.byKey(const Key('dovah-icon-button-focus-outline')),
          findsOneWidget,
        );

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();

        expect(
          find.byKey(const Key('dovah-icon-button-focus-outline')),
          findsNothing,
        );
        expect(
          find.byKey(const Key('dovah-connection-card-focus-outline')),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'ConnectionsScreen passes the selected appearance preset to its ViewModel',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());
        await tester.tap(find.byIcon(Icons.settings_outlined));
        await tester.pumpAndSettle();

        await tester.tap(find.text(DovahThemePreset.hearth.label));
        await tester.pump();

        expect(selectedPresets, [DovahThemePreset.hearth]);
      },
    );

    testWidgets(
      'ConnectionsScreen does not display the appearance dialog before the action is tapped',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildWidget());

        expect(find.byType(DovahDialog), findsNothing);
        expect(find.byType(AppearanceSection), findsNothing);
      },
    );

    testWidgets(
      'ConnectionsScreen closes the appearance dialog with its close action',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(1280, 720));
        await tester.pumpWidget(buildWidget());
        await tester.tap(find.byIcon(Icons.settings_outlined));
        await tester.pumpAndSettle();

        await tester.tap(find.byTooltip('Close'));
        await tester.pumpAndSettle();

        expect(find.byType(DovahDialog), findsNothing);
      },
    );
  });

  group('ConnectionsScreen follows the active theme', () {
    testWidgets(
      'ConnectionsScreen keeps rendering its content when the theme changes',
      (WidgetTester tester) async {
        await useSurface(tester, const Size(1280, 720));

        for (final DovahThemePreset preset in DovahThemePreset.values) {
          await tester.pumpWidget(buildWidget(preset: preset));
          await tester.pumpAndSettle();
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;

          expect(tester.takeException(), isNull);
          expect(
            find.text(tokens.uppercaseLabels ? 'CONNECTIONS' : 'Connections'),
            findsOneWidget,
          );
          expect(find.byType(DovahConnectionCard), findsOneWidget);
        }
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'ConnectionsScreen uses the $preset background token behind its content',
        (WidgetTester tester) async {
          await useSurface(tester, const Size(1280, 720));
          await tester.pumpWidget(buildWidget(preset: preset));
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;

          final ColoredBox base = tester.widget(
            find
                .descendant(
                  of: find.byType(DovahEnvironmentBackground),
                  matching: find.byType(ColoredBox),
                )
                .first,
          );

          expect(base.color, tokens.background);
        },
      );
    }
  });

  group('ConnectionsScreen meets accessibility recommended guidelines', () {
    testWidgets(
      'ConnectionsScreen labels its controls and meets tap-target guidelines',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await useSurface(tester, const Size(1280, 720));
          await tester.pumpWidget(buildWidget());

          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
        } finally {
          handle.dispose();
        }
      },
    );

    testWidgets(
      'ConnectionsScreen exposes the title as a header, the host as a button, and Discover as disabled',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await useSurface(tester, const Size(1280, 720));
          await tester.pumpWidget(buildWidget());

          final SemanticsData title = tester
              .getSemantics(find.bySemanticsLabel('Connections'))
              .getSemanticsData();
          final SemanticsData host = tester
              .getSemantics(
                find.bySemanticsLabel(
                  'Local Host, DovahLink Host, 127.0.0.1:58231, Not connected',
                ),
              )
              .getSemanticsData();
          final SemanticsData discover = tester
              .getSemantics(find.bySemanticsLabel('Discover Skyrim'))
              .getSemanticsData();
          final SemanticsData appearance = tester
              .getSemantics(find.bySemanticsLabel('Appearance settings'))
              .getSemanticsData();

          expect(title.flagsCollection.isHeader, isTrue);
          expect(host.flagsCollection.isButton, isTrue);
          expect(host.flagsCollection.isEnabled, Tristate.isTrue);
          expect(discover.flagsCollection.isEnabled, Tristate.isFalse);
          expect(appearance.flagsCollection.isButton, isTrue);
          expect(appearance.hasAction(SemanticsAction.tap), isTrue);
        } finally {
          handle.dispose();
        }
      },
    );
  });
}
