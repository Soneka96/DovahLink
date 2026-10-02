import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/connections_host_section.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';
import '../../../../fixtures/fixtures.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises [ConnectionsHostSection] across every DovahLink theme, both test sizes, empty and
/// long-name inputs, and selection.
void main() {
  group('ConnectionsHostSection renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'ConnectionsHostSection renders a card per Host under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              ConnectionsHostSection(
                cards: [
                  Fixtures.buildHostCardViewData(
                    source: ConnectionHostSelectionSource.knownHost,
                    subtitle: 'Known Host',
                  ),
                  Fixtures.buildHostCardViewData(
                    host: Fixtures.buildHost(
                      hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
                      displayName: 'Second Host',
                      uri: Uri.parse('ws://192.168.1.11:2000/'),
                    ),
                    source: ConnectionHostSelectionSource.knownHost,
                    subtitle: 'Known Host',
                    title: 'Second Host',
                    detail: '192.168.1.11:2000',
                  ),
                ],
                onSelectHost: (HostCardViewData card) {},
              ),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(find.text('MY SKYRIM PCS'), findsOneWidget);
            expect(find.byType(DovahConnectionCard), findsNWidgets(2));
            expect(
              find.byKey(
                const Key('host-card-81869993-955c-4ba3-a7d0-d35ca86078ea'),
              ),
              findsOneWidget,
            );
            expect(
              find.byKey(
                const Key('host-card-81f6cc90-3a88-40c7-8351-104d4a36c971'),
              ),
              findsOneWidget,
            );
            expect(
              find.text('192.168.1.11:2000'),
              size.width > DovahRootMetrics.narrowMaxWindowWidth
                  ? findsOneWidget
                  : findsNothing,
            );
          },
        );
      }
    }

    testWidgets(
      'ConnectionsHostSection displays the unknown state on each card',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
              ),
            ],
            onSelectHost: (HostCardViewData card) {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.text('Unknown'), findsOneWidget);
        expect(find.text('Known Host'), findsOneWidget);
      },
    );

    testWidgets(
      'ConnectionsHostSection renders no cards without error when empty',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: const [],
            onSelectHost: (HostCardViewData card) {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(tester.takeException(), isNull);
        expect(find.text('MY SKYRIM PCS'), findsOneWidget);
        expect(find.byType(DovahConnectionCard), findsNothing);
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'ConnectionsHostSection truncates a very long Host name and large text without overflow under $preset',
        (WidgetTester tester) async {
          final String longName = 'A very long Host name ' * 12;
          await pumpDovahThemedWidget(
            tester,
            Builder(
              builder: (BuildContext context) => MediaQuery(
                data: MediaQuery.of(
                  context,
                ).copyWith(textScaler: const TextScaler.linear(2)),
                child: ConnectionsHostSection(
                  cards: [
                    Fixtures.buildHostCardViewData(
                      host: Fixtures.buildHost(displayName: longName),
                      source: ConnectionHostSelectionSource.knownHost,
                      subtitle: 'Known Host',
                      title: longName,
                      detail: 'a-very-long-host-name.local:58231' * 3,
                    ),
                  ],
                  onSelectHost: (HostCardViewData card) {},
                ),
              ),
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );

          expect(tester.takeException(), isNull);
        },
      );
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'ConnectionsHostSection keeps each card at least its themed minimum height under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            ConnectionsHostSection(
              cards: [
                Fixtures.buildHostCardViewData(
                  source: ConnectionHostSelectionSource.knownHost,
                  subtitle: 'Known Host',
                ),
              ],
              onSelectHost: (HostCardViewData card) {},
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahConnectionCardMetrics metrics =
              DovahConnectionCardMetrics.forWindow(
                themeMetrics: dovahThemeDataFor(
                  preset,
                ).extension<DovahConnectionCardThemeMetrics>()!,
                window: dovahTestSizes.first,
              );

          expect(
            tester.getSize(find.byType(DovahConnectionCard)).height,
            greaterThanOrEqualTo(metrics.minHeight),
          );
        },
      );
    }
  });

  group('ConnectionsHostSection lays out its cards', () {
    testWidgets(
      'ConnectionsHostSection separates cards by the shared list gap',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
              ),
              Fixtures.buildHostCardViewData(
                host: Fixtures.buildHost(
                  hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
                  displayName: 'Second Host',
                  uri: Uri.parse('ws://192.168.1.11:2000/'),
                ),
                source: ConnectionHostSelectionSource.knownHost,
                title: 'Second Host',
                subtitle: 'Known Host',
              ),
            ],
            onSelectHost: (HostCardViewData card) {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final double gap =
            tester
                .getTopLeft(
                  find.byKey(
                    const Key('host-card-81f6cc90-3a88-40c7-8351-104d4a36c971'),
                  ),
                )
                .dy -
            tester
                .getBottomLeft(
                  find.byKey(
                    const Key('host-card-81869993-955c-4ba3-a7d0-d35ca86078ea'),
                  ),
                )
                .dy;

        expect(gap, isA<double>());
        expect(gap, DovahRootMetrics.listGap);
      },
    );
  });

  group('ConnectionsHostSection calls onSelectHost', () {
    testWidgets(
      'ConnectionsHostSection calls onSelectHost with the tapped card Host',
      (WidgetTester tester) async {
        final Host first = Fixtures.buildHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          displayName: 'First Host',
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final Host second = Fixtures.buildHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          displayName: 'Second Host',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        final List<HostCardViewData> selected = [];
        final HostCardViewData secondCard = Fixtures.buildHostCardViewData(
          host: second,
          source: ConnectionHostSelectionSource.knownHost,
          title: 'Second Host',
          subtitle: 'Known Host',
          state: DovahConnectionCardState.available,
        );
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                host: first,
                source: ConnectionHostSelectionSource.knownHost,
                title: 'First Host',
                subtitle: 'Known Host',
                state: DovahConnectionCardState.available,
              ),
              secondCard,
            ],
            onSelectHost: selected.add,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.text('›'), findsNWidgets(2));
        await tester.tap(
          find.byKey(
            const Key('host-card-81f6cc90-3a88-40c7-8351-104d4a36c971'),
          ),
        );
        await tester.pump();

        expect(selected, [secondCard]);
        expect(selected.single.source, ConnectionHostSelectionSource.knownHost);
      },
    );

    testWidgets(
      'ConnectionsHostSection keeps Host identity when cards are reordered',
      (WidgetTester tester) async {
        final Host first = Fixtures.buildHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          displayName: 'Shared Host Name',
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final Host second = Fixtures.buildHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          displayName: 'Shared Host Name',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        final List<HostCardViewData> selected = [];

        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                host: first,
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
                state: DovahConnectionCardState.available,
              ),
              Fixtures.buildHostCardViewData(
                host: second,
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
                state: DovahConnectionCardState.available,
              ),
            ],
            onSelectHost: selected.add,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                host: second,
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
                state: DovahConnectionCardState.available,
              ),
              Fixtures.buildHostCardViewData(
                host: first,
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
                state: DovahConnectionCardState.available,
              ),
            ],
            onSelectHost: selected.add,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        await tester.tap(
          find.byKey(
            const Key('host-card-81f6cc90-3a88-40c7-8351-104d4a36c971'),
          ),
        );
        await tester.pump();

        expect(selected.single.host, second);
        expect(selected.single.source, ConnectionHostSelectionSource.knownHost);
      },
    );

    testWidgets(
      'ConnectionsHostSection keeps a Host card key when its endpoint changes',
      (WidgetTester tester) async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final Host before = Fixtures.buildHost(
          hostId: hostId,
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final Host after = Fixtures.buildHost(
          hostId: hostId,
          displayName: 'Moved Host',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        final List<Host> selected = <Host>[];
        const Key key = Key('host-card-$hostId');

        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                host: before,
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
                state: DovahConnectionCardState.available,
              ),
            ],
            onSelectHost: (HostCardViewData card) => selected.add(card.host),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );
        expect(find.byKey(key), findsOneWidget);

        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                host: after,
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
                state: DovahConnectionCardState.available,
              ),
            ],
            onSelectHost: (HostCardViewData card) => selected.add(card.host),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.byKey(key), findsOneWidget);
        await tester.tap(find.byKey(key));
        await tester.pump();
        expect(selected, <Host>[after]);
      },
    );

    testWidgets(
      'ConnectionsHostSection does not call onSelectHost before a tap',
      (WidgetTester tester) async {
        final List<HostCardViewData> selected = [];
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
              ),
            ],
            onSelectHost: selected.add,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(selected, isEmpty);
      },
    );

    testWidgets(
      'ConnectionsHostSection keeps non-online Known Hosts visible but not selectable',
      (WidgetTester tester) async {
        final List<HostCardViewData> selected = [];
        final List<HostCardViewData> offlineInfoRequests = [];
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          for (final DovahConnectionCardState state in [
            DovahConnectionCardState.offline,
            DovahConnectionCardState.connected,
            DovahConnectionCardState.reconnecting,
            DovahConnectionCardState.checking,
            DovahConnectionCardState.unknown,
          ]) {
            await pumpDovahThemedWidget(
              tester,
              ConnectionsHostSection(
                cards: [
                  Fixtures.buildHostCardViewData(
                    source: ConnectionHostSelectionSource.knownHost,
                    subtitle: 'Known Host',
                    state: state,
                  ),
                ],
                onSelectHost: selected.add,
                onShowOfflineHost: offlineInfoRequests.add,
              ),
              preset: DovahThemePreset.dovah,
              size: dovahTestSizes.first,
            );

            final DovahConnectionCard card = tester.widget(
              find.byType(DovahConnectionCard),
            );
            final SemanticsData semanticsData = tester
                .getSemantics(
                  find.bySemanticsLabel(
                    'Local Host, Known Host, 127.0.0.1:58231, ${state.label}',
                  ),
                )
                .getSemanticsData();
            final bool isOffline = state == DovahConnectionCardState.offline;
            expect(
              card.onTap,
              isOffline ? isNotNull : isNull,
              reason: state.name,
            );
            expect(
              semanticsData.flagsCollection.isEnabled,
              isOffline ? Tristate.isTrue : Tristate.isFalse,
              reason: state.name,
            );
            expect(
              semanticsData.hasAction(SemanticsAction.tap),
              isOffline,
              reason: state.name,
            );
            expect(find.text('›'), findsNothing);
            await tester.tap(
              find.byType(DovahConnectionCard),
              warnIfMissed: false,
            );
            expect(selected, isEmpty, reason: state.name);
            expect(
              offlineInfoRequests.length,
              isOffline ? 1 : 0,
              reason: state.name,
            );
            offlineInfoRequests.clear();
          }
        } finally {
          semantics.dispose();
        }
      },
    );

    testWidgets(
      'ConnectionsHostSection leaves an Offline card inert without an information callback',
      (WidgetTester tester) async {
        final List<HostCardViewData> selected = [];
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(
                source: ConnectionHostSelectionSource.knownHost,
                subtitle: 'Known Host',
                state: DovahConnectionCardState.offline,
              ),
            ],
            onSelectHost: selected.add,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final DovahConnectionCard card = tester.widget(
          find.byType(DovahConnectionCard),
        );
        expect(card.onTap, isNull);
        expect(find.text('›'), findsNothing);
        await tester.tap(find.byType(DovahConnectionCard), warnIfMissed: false);
        expect(selected, isEmpty);
      },
    );

    testWidgets(
      'ConnectionsHostSection selects a repair card to start Pair again',
      (WidgetTester tester) async {
        final HostCardViewData repairCard = Fixtures.buildHostCardViewData(
          source: ConnectionHostSelectionSource.knownHost,
          subtitle: 'Known Host',
          state: DovahConnectionCardState.repair,
        );
        final List<HostCardViewData> selected = [];
        final List<HostCardViewData> offlineInfoRequests = [];
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          await pumpDovahThemedWidget(
            tester,
            ConnectionsHostSection(
              cards: [repairCard],
              onSelectHost: selected.add,
              onShowOfflineHost: offlineInfoRequests.add,
            ),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          final SemanticsData semanticsData = tester
              .getSemantics(
                find.bySemanticsLabel(
                  'Local Host, Known Host, 127.0.0.1:58231, Pair again',
                ),
              )
              .getSemanticsData();
          expect(semanticsData.flagsCollection.isEnabled, Tristate.isTrue);
          expect(semanticsData.hasAction(SemanticsAction.tap), isTrue);
          await tester.tap(
            find.byKey(Key('host-card-${repairCard.host.hostId}')),
          );

          expect(selected, [repairCard]);
          expect(offlineInfoRequests, isEmpty);
        } finally {
          semantics.dispose();
        }
      },
    );
  });

  group('ConnectionsHostSection exposes sensible semantics', () {
    testWidgets(
      'ConnectionsHostSection labels each card with its title, subtitle, detail and state',
      (WidgetTester tester) async {
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          await pumpDovahThemedWidget(
            tester,
            ConnectionsHostSection(
              cards: [
                Fixtures.buildHostCardViewData(
                  source: ConnectionHostSelectionSource.knownHost,
                  subtitle: 'Known Host',
                ),
              ],
              onSelectHost: (HostCardViewData card) {},
            ),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          final SemanticsData host = tester
              .getSemantics(
                find.bySemanticsLabel(
                  'Local Host, Known Host, 127.0.0.1:58231, Unknown',
                ),
              )
              .getSemanticsData();
          expect(host.flagsCollection.isButton, isTrue);
          expect(host.flagsCollection.isEnabled, Tristate.isFalse);
          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
        } finally {
          semantics.dispose();
        }
      },
    );
  });
}
