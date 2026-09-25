import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/widgets/connections_hero.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/root_header.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
import 'widgets/dovah_widget_test_helpers.dart';

/// The metrics a probe read during its most recent build.
class _Probe {
  late DovahRootMetrics root;
  late DovahConnectionCardMetrics card;
  late DovahPageMetrics page;
  late DovahSessionMetrics session;
  late DovahOverviewMetrics overview;
}

/// Half of the default `MaterialApp` theme animation, which is linear, so the theme is exactly
/// halfway between the old and the new values.
final Duration _halfTransition = kThemeAnimationDuration ~/ 2;

/// Builds the real header, title row, and connection card the Connections screen shows, plus a
/// probe that reads every metrics accessor, under [preset]'s theme.
Widget _buildApp(DovahThemePreset preset, _Probe probe) => MaterialApp(
  theme: dovahThemeDataFor(preset),
  home: Scaffold(
    body: SingleChildScrollView(
      child: Column(
        children: [
          RootHeader(onOpenAppearance: () {}),
          ConnectionsHero(onDiscover: () {}),
          DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Last connected yesterday',
            state: DovahConnectionCardState.available,
            onTap: () {},
          ),
          Builder(
            builder: (BuildContext context) {
              probe.root = context.dovahRootMetrics;
              probe.card = context.dovahConnectionCardMetrics;
              probe.page = context.dovahPageMetrics;
              probe.session = context.dovahSessionMetrics;
              probe.overview = context.dovahOverviewMetrics;
              return const SizedBox.shrink();
            },
          ),
        ],
      ),
    ),
  ),
);

double _headerHeight(WidgetTester tester) =>
    tester.getSize(find.byType(RootHeader)).height;

double _titleFontSize(WidgetTester tester) =>
    tester.widget<Text>(find.text('Connections')).style!.fontSize!;

DovahSurface _cardSurface(WidgetTester tester) => tester.widget<DovahSurface>(
  find.descendant(
    of: find.byType(DovahConnectionCard),
    matching: find.byWidgetPredicate(
      (Widget widget) =>
          widget is DovahSurface && widget.role == DovahMaterialRole.surface,
    ),
  ),
);

double _iconTileSize(WidgetTester tester) => tester
    .getSize(
      find.descendant(
        of: find.byType(DovahConnectionCard),
        matching: find.byWidgetPredicate(
          (Widget widget) =>
              widget is DovahSurface && widget.role == DovahMaterialRole.icon,
        ),
      ),
    )
    .width;

/// Switches the app's theme to [to], then returns with the animation not yet advanced.
Future<void> _switchTheme(
  WidgetTester tester,
  DovahThemePreset to,
  _Probe probe,
) => tester.pumpWidget(_buildApp(to, probe));

/// Exercises that a theme change animates the geometry read through the `context.dovah*Metrics`
/// accessors instead of snapping it, which is what the theme-metric extensions exist to guarantee.
void main() {
  group('Behavior header and title geometry animates across a theme change', () {
    for (final (
          DovahThemePreset from,
          DovahThemePreset to,
          Size window,
          double fromHeader,
          double toHeader,
          double fromTitle,
          double toTitle,
        )
        in [
          (
            DovahThemePreset.dovah,
            DovahThemePreset.hearth,
            const Size(1280, 720),
            88.0,
            86.0,
            34.0,
            38.0,
          ),
          (
            DovahThemePreset.hearth,
            DovahThemePreset.dovah,
            const Size(1280, 720),
            86.0,
            88.0,
            38.0,
            34.0,
          ),
          (
            DovahThemePreset.dovah,
            DovahThemePreset.hearth,
            const Size(800, 700),
            88.0,
            86.0,
            28.0,
            38.0,
          ),
          (
            DovahThemePreset.dovah,
            DovahThemePreset.hearth,
            const Size(1280, 560),
            62.0,
            62.0,
            25.0,
            38.0,
          ),
        ]) {
      testWidgets(
        'Theme change from ${from.name} to ${to.name} at $window morphs the header and title instead of snapping',
        (WidgetTester tester) async {
          final _Probe probe = _Probe();
          setDovahTestWindow(tester, window);
          await tester.pumpWidget(_buildApp(from, probe));
          await tester.pumpAndSettle();
          expect(_headerHeight(tester), fromHeader);
          expect(_titleFontSize(tester), fromTitle);

          await _switchTheme(tester, to, probe);
          expect(_headerHeight(tester), fromHeader);
          expect(_titleFontSize(tester), fromTitle);

          await tester.pump();
          await tester.pump(_halfTransition);
          expect(tester.hasRunningAnimations, isTrue);
          expect(
            _headerHeight(tester),
            closeTo((fromHeader + toHeader) / 2, 0.01),
          );
          expect(
            _titleFontSize(tester),
            closeTo((fromTitle + toTitle) / 2, 0.01),
          );
          expect(probe.root.pageTitleFontSize, _titleFontSize(tester));
          expect(tester.takeException(), isNull);

          await tester.pumpAndSettle();
          expect(_headerHeight(tester), toHeader);
          expect(_titleFontSize(tester), toTitle);
        },
      );
    }

    testWidgets(
      'Theme change from dovah to hearth passes through values strictly between the endpoints',
      (WidgetTester tester) async {
        final _Probe probe = _Probe();
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(_buildApp(DovahThemePreset.dovah, probe));
        await tester.pumpAndSettle();
        await _switchTheme(tester, DovahThemePreset.hearth, probe);
        await tester.pump();

        final List<double> titleSizes = <double>[];
        for (int step = 0; step < 4; step++) {
          await tester.pump(const Duration(milliseconds: 40));
          titleSizes.add(_titleFontSize(tester));
        }

        expect(titleSizes.first, greaterThan(34));
        expect(titleSizes.first, lessThan(38));
        expect(titleSizes, orderedEquals(<double>[...titleSizes]..sort()));
        expect(titleSizes.toSet().length, greaterThan(2));
      },
    );
  });

  group('Behavior connection card geometry animates across a theme change', () {
    testWidgets(
      'Theme change from frostbound to hearth morphs the card padding, icon tile, and corners instead of snapping',
      (WidgetTester tester) async {
        final _Probe probe = _Probe();
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(_buildApp(DovahThemePreset.frostbound, probe));
        await tester.pumpAndSettle();
        expect(
          _cardSurface(tester).padding,
          const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        );
        expect(_iconTileSize(tester), 37);
        expect(_cardSurface(tester).cornerCutSize, 11);

        await _switchTheme(tester, DovahThemePreset.hearth, probe);
        expect(_iconTileSize(tester), 37);

        await tester.pump();
        await tester.pump(_halfTransition);
        expect(
          _cardSurface(tester).padding,
          const EdgeInsets.symmetric(vertical: 13, horizontal: 16),
        );
        expect(_iconTileSize(tester), 40);
        expect(_cardSurface(tester).cornerCutSize, 5.5);
        expect(_cardSurface(tester).cornerRadius, 6);
        expect(probe.card.minHeight, 75);
        expect(tester.takeException(), isNull);

        await tester.pumpAndSettle();
        expect(
          _cardSurface(tester).padding,
          const EdgeInsets.symmetric(vertical: 16, horizontal: 18),
        );
        expect(_iconTileSize(tester), 43);
        expect(_cardSurface(tester).cornerCutSize, 0);
        expect(_cardSurface(tester).cornerRadius, 12);
      },
    );

    testWidgets(
      'Theme change from dovah to hearth morphs the card height between 80 and 82',
      (WidgetTester tester) async {
        final _Probe probe = _Probe();
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(_buildApp(DovahThemePreset.dovah, probe));
        await tester.pumpAndSettle();
        expect(probe.card.minHeight, 80);

        await _switchTheme(tester, DovahThemePreset.hearth, probe);
        await tester.pump();
        await tester.pump(_halfTransition);
        expect(probe.card.minHeight, 81);

        await tester.pumpAndSettle();
        expect(probe.card.minHeight, 82);
      },
    );
  });

  group('Behavior every metrics family animates across a theme change', () {
    testWidgets(
      'Theme change from frostbound to dovah morphs the metrics every accessor resolves',
      (WidgetTester tester) async {
        final _Probe probe = _Probe();
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(_buildApp(DovahThemePreset.frostbound, probe));
        await tester.pumpAndSettle();
        expect(probe.session.navHeight, 45);
        expect(probe.overview.heroMinHeight, 226);
        expect(probe.overview.gridGap, 10);
        expect(probe.page.contentTopPadding, 22);
        expect(probe.page.panelPadding, const EdgeInsets.all(14));
        expect(probe.root.brandTaglineLetterSpacingEm, 0.24);

        await _switchTheme(tester, DovahThemePreset.dovah, probe);
        expect(probe.session.navHeight, 45);

        await tester.pump();
        await tester.pump(_halfTransition);
        expect(probe.session.navHeight, 49);
        expect(probe.overview.heroMinHeight, 248);
        expect(probe.overview.gridGap, 12);
        expect(probe.overview.statsTopGap, 17);
        expect(probe.page.contentTopPadding, 25);
        expect(probe.page.introBottomGap, 17);
        expect(probe.page.panelPadding, const EdgeInsets.all(16));
        expect(probe.root.brandTaglineLetterSpacingEm, closeTo(0.22, 1e-9));

        await tester.pumpAndSettle();
        expect(probe.session.navHeight, 53);
        expect(probe.overview.heroMinHeight, 270);
        expect(probe.overview.gridGap, 14);
        expect(probe.page.contentTopPadding, 28);
        expect(probe.page.panelPadding, const EdgeInsets.all(18));
        expect(probe.root.brandTaglineLetterSpacingEm, 0.2);
      },
    );

    testWidgets(
      'Theme change keeps window-driven values fixed while the theme geometry morphs',
      (WidgetTester tester) async {
        final _Probe probe = _Probe();
        setDovahTestWindow(tester, const Size(800, 700));
        await tester.pumpWidget(_buildApp(DovahThemePreset.dovah, probe));
        await tester.pumpAndSettle();

        await _switchTheme(tester, DovahThemePreset.hearth, probe);
        await tester.pump();
        await tester.pump(_halfTransition);

        expect(probe.root.sideMargin, 18);
        expect(probe.root.showFooter, isTrue);
        expect(probe.card.showDetail, isFalse);
        expect(probe.page.placeholderColumns, 2);
        expect(probe.session.showFirstAction, isFalse);
        expect(probe.overview.mainColumnFlex, 115);
        expect(probe.root.pageTitleFontSize, 33);
      },
    );

    testWidgets(
      'Theme change from frostbound to dovah morphs the compact-window metrics every accessor resolves',
      (WidgetTester tester) async {
        final _Probe probe = _Probe();
        setDovahTestWindow(tester, const Size(1280, 560));
        await tester.pumpWidget(_buildApp(DovahThemePreset.frostbound, probe));
        await tester.pumpAndSettle();
        expect(probe.card.minHeight, 62);
        expect(probe.overview.heroMinHeight, 205);
        expect(probe.page.contentTopPadding, 22);
        expect(probe.page.panelPadding, const EdgeInsets.all(14));
        expect(probe.root.headerHeight, 56);

        await _switchTheme(tester, DovahThemePreset.dovah, probe);
        await tester.pump();
        await tester.pump(_halfTransition);
        expect(probe.card.minHeight, 65);
        expect(
          probe.card.padding,
          const EdgeInsets.symmetric(vertical: 8.5, horizontal: 13),
        );
        expect(probe.overview.heroMinHeight, 207.5);
        expect(probe.page.contentTopPadding, 20);
        expect(probe.page.panelPadding, const EdgeInsets.all(14.5));
        expect(probe.root.headerHeight, 59);
        expect(probe.session.navHeight, 45);

        await tester.pumpAndSettle();
        expect(probe.card.minHeight, 68);
        expect(probe.overview.heroMinHeight, 210);
        expect(probe.page.panelPadding, const EdgeInsets.all(15));
        expect(probe.root.headerHeight, 62);
      },
    );

    testWidgets(
      'Theme change from hearth to frostbound morphs the card geometry back',
      (WidgetTester tester) async {
        final _Probe probe = _Probe();
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(_buildApp(DovahThemePreset.hearth, probe));
        await tester.pumpAndSettle();
        expect(_iconTileSize(tester), 43);

        await _switchTheme(tester, DovahThemePreset.frostbound, probe);
        await tester.pump();
        await tester.pump(_halfTransition);
        expect(_iconTileSize(tester), 40);
        expect(probe.card.minHeight, 75);
        expect(_cardSurface(tester).cornerCutSize, 5.5);

        await tester.pumpAndSettle();
        expect(_iconTileSize(tester), 37);
        expect(probe.card.minHeight, 68);
      },
    );

    testWidgets(
      'Theme change to the same preset starts no animation and keeps the geometry',
      (WidgetTester tester) async {
        final _Probe probe = _Probe();
        setDovahTestWindow(tester, const Size(1280, 720));
        await tester.pumpWidget(_buildApp(DovahThemePreset.dovah, probe));
        await tester.pumpAndSettle();

        await _switchTheme(tester, DovahThemePreset.dovah, probe);
        await tester.pump();

        expect(tester.hasRunningAnimations, isFalse);
        expect(probe.root.headerHeight, 88);
        expect(probe.card.minHeight, 80);
      },
    );
  });
}
