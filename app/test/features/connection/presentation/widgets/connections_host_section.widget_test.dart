import 'package:flutter/material.dart';

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
                  Fixtures.buildHostCardViewData(),
                  Fixtures.buildHostCardViewData(
                    host: Fixtures.buildHost(
                      hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
                      displayName: 'Second Host',
                      uri: Uri.parse('ws://192.168.1.11:2000/'),
                    ),
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
            expect(find.text('AVAILABLE'), findsOneWidget);
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
            cards: [Fixtures.buildHostCardViewData()],
            onSelectHost: (HostCardViewData card) {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.text('Not connected'), findsOneWidget);
        expect(find.text('Discovered candidate'), findsOneWidget);
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
              cards: [Fixtures.buildHostCardViewData()],
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
              Fixtures.buildHostCardViewData(),
              Fixtures.buildHostCardViewData(
                host: Fixtures.buildHost(
                  hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
                  displayName: 'Second Host',
                  uri: Uri.parse('ws://192.168.1.11:2000/'),
                ),
                title: 'Second Host',
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
          title: 'Second Host',
        );
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(host: first, title: 'First Host'),
              secondCard,
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

        expect(selected, [secondCard]);
        expect(selected.single.source, ConnectionHostSelectionSource.candidate);
      },
    );

    testWidgets(
      'ConnectionsHostSection keeps different Hosts at one endpoint distinct when tapped',
      (WidgetTester tester) async {
        final Host host = Fixtures.buildHost();
        final HostCardViewData knownHostCard = Fixtures.buildHostCardViewData(
          host: host,
          source: ConnectionHostSelectionSource.knownHost,
          subtitle: 'Known Host',
          state: DovahConnectionCardState.offline,
        );
        final HostCardViewData candidateCard = Fixtures.buildHostCardViewData(
          host: Fixtures.buildHost(
            hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
            uri: host.uri,
          ),
        );
        final List<HostCardViewData> selected = [];

        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [knownHostCard, candidateCard],
            onSelectHost: selected.add,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.byType(DovahConnectionCard), findsNWidgets(2));
        expect(find.text('Known Host'), findsOneWidget);
        expect(find.text('Discovered candidate'), findsOneWidget);
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

        await tester.tap(
          find.byKey(
            const Key('host-card-81869993-955c-4ba3-a7d0-d35ca86078ea'),
          ),
        );
        await tester.pump();
        await tester.tap(
          find.byKey(
            const Key('host-card-81f6cc90-3a88-40c7-8351-104d4a36c971'),
          ),
        );
        await tester.pump();

        expect(selected, [knownHostCard, candidateCard]);
        expect(selected.map((HostCardViewData card) => card.source), [
          ConnectionHostSelectionSource.knownHost,
          ConnectionHostSelectionSource.candidate,
        ]);
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
              Fixtures.buildHostCardViewData(host: first),
              Fixtures.buildHostCardViewData(host: second),
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
              Fixtures.buildHostCardViewData(host: second),
              Fixtures.buildHostCardViewData(host: first),
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
        expect(selected.single.source, ConnectionHostSelectionSource.candidate);
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
            cards: [Fixtures.buildHostCardViewData(host: before)],
            onSelectHost: (HostCardViewData card) => selected.add(card.host),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );
        expect(find.byKey(key), findsOneWidget);

        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [Fixtures.buildHostCardViewData(host: after)],
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
            cards: [Fixtures.buildHostCardViewData()],
            onSelectHost: selected.add,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(selected, isEmpty);
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
              cards: [Fixtures.buildHostCardViewData()],
              onSelectHost: (HostCardViewData card) {},
            ),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          expect(
            find.bySemanticsLabel(
              'Local Host, Discovered candidate, 127.0.0.1:58231, Not connected',
            ),
            findsOneWidget,
          );
          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
        } finally {
          semantics.dispose();
        }
      },
    );
  });

  group('ConnectionsHostSection renders temporary discovery states', () {
    testWidgets(
      'ConnectionsHostSection shows the candidate without status feedback when available',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [Fixtures.buildHostCardViewData()],
            discoveryStatus: ConnectionDiscoveryStatus.available,
            onSelectHost: ignoreHost,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.text('AVAILABLE'), findsOneWidget);
        expect(find.byType(DovahConnectionCard), findsOneWidget);
        expect(
          find.byKey(const Key('connection-discovery-status')),
          findsNothing,
        );
      },
    );

    testWidgets('ConnectionsHostSection announces active discovery', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        await pumpDovahThemedWidget(
          tester,
          const ConnectionsHostSection(
            cards: <HostCardViewData>[],
            discoveryStatus: ConnectionDiscoveryStatus.discovering,
            onSelectHost: ignoreHost,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          find.text('Searching for DovahLink on this PC…'),
          findsOneWidget,
        );
        expect(
          tester.getSemantics(
            find.byKey(const Key('connection-discovery-status')),
          ),
          isSemantics(
            label: 'Searching for DovahLink on this PC…',
            isLiveRegion: true,
          ),
        );
      } finally {
        semantics.dispose();
      }
    });

    testWidgets(
      'ConnectionsHostSection displays empty discovery feedback without cards',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const ConnectionsHostSection(
            cards: <HostCardViewData>[],
            discoveryStatus: ConnectionDiscoveryStatus.empty,
            onSelectHost: ignoreHost,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.text('No new local Hosts found.'), findsOneWidget);
        expect(find.byType(DovahConnectionCard), findsNothing);
      },
    );

    testWidgets(
      'ConnectionsHostSection displays centralized hostUnavailable copy',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const ConnectionsHostSection(
            cards: <HostCardViewData>[],
            discoveryStatus: ConnectionDiscoveryStatus.failed,
            discoveryFailure: ConnectionFailureReason.hostUnavailable,
            onSelectHost: ignoreHost,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          find.text(
            'Could not reach the local Host. Check that it is running and try again.',
          ),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'ConnectionsHostSection displays centralized invalidResponse copy',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const ConnectionsHostSection(
            cards: <HostCardViewData>[],
            discoveryStatus: ConnectionDiscoveryStatus.failed,
            discoveryFailure: ConnectionFailureReason.invalidResponse,
            onSelectHost: ignoreHost,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          find.text('The local Host returned an invalid response. Try again.'),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'ConnectionsHostSection displays centralized incompatibleHost copy',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const ConnectionsHostSection(
            cards: <HostCardViewData>[],
            discoveryStatus: ConnectionDiscoveryStatus.failed,
            discoveryFailure: ConnectionFailureReason.incompatibleHost,
            onSelectHost: ignoreHost,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          find.text('This local Host version is not compatible with the app.'),
          findsOneWidget,
        );
      },
    );

    testWidgets('ConnectionsHostSection displays centralized unknown copy', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const ConnectionsHostSection(
          cards: <HostCardViewData>[],
          discoveryStatus: ConnectionDiscoveryStatus.failed,
          onSelectHost: ignoreHost,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.text('Host discovery failed. Try again.'), findsOneWidget);
    });
  });
}

/// Ignores Host selection when discovery status is the behavior under test.
void ignoreHost(HostCardViewData card) {}
