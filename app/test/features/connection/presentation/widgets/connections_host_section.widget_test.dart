import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/connections_host_section.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
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
                      displayName: 'Second Host',
                      uri: Uri.parse('ws://192.168.1.11:2000/'),
                    ),
                    title: 'Second Host',
                    detail: '192.168.1.11:2000',
                  ),
                ],
                onSelectHost: (Host host) {},
              ),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(find.text('MY SKYRIM PCS'), findsOneWidget);
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
      }
    }

    testWidgets(
      'ConnectionsHostSection displays the unknown state on each card',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [Fixtures.buildHostCardViewData()],
            onSelectHost: (Host host) {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.text('Not connected'), findsOneWidget);
        expect(find.text('DovahLink Host'), findsOneWidget);
      },
    );

    testWidgets(
      'ConnectionsHostSection renders no cards without error when empty',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(cards: const [], onSelectHost: (Host host) {}),
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
                  onSelectHost: (Host host) {},
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
              onSelectHost: (Host host) {},
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;

          expect(
            tester.getSize(find.byType(DovahConnectionCard)).height,
            greaterThanOrEqualTo(tokens.connectionCardMinHeight),
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
                  displayName: 'Second Host',
                  uri: Uri.parse('ws://192.168.1.11:2000/'),
                ),
                title: 'Second Host',
              ),
            ],
            onSelectHost: (Host host) {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final double gap =
            tester
                .getTopLeft(
                  find.byKey(const Key('host-card-ws://192.168.1.11:2000/')),
                )
                .dy -
            tester
                .getBottomLeft(
                  find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
                )
                .dy;

        expect(gap, isA<double>());
        expect(gap, DovahThemeTokens.rootListGap);
      },
    );
  });

  group('ConnectionsHostSection calls onSelectHost', () {
    testWidgets(
      'ConnectionsHostSection calls onSelectHost with the tapped card Host',
      (WidgetTester tester) async {
        final Host first = Fixtures.buildHost(
          displayName: 'First Host',
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final Host second = Fixtures.buildHost(
          displayName: 'Second Host',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        final List<Host> selected = [];
        await pumpDovahThemedWidget(
          tester,
          ConnectionsHostSection(
            cards: [
              Fixtures.buildHostCardViewData(host: first, title: 'First Host'),
              Fixtures.buildHostCardViewData(
                host: second,
                title: 'Second Host',
              ),
            ],
            onSelectHost: selected.add,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        await tester.tap(find.byKey(const Key('host-card-ws://127.0.0.1:2/')));
        await tester.pump();

        expect(selected, [second]);
      },
    );

    testWidgets(
      'ConnectionsHostSection keeps Host identity when cards are reordered',
      (WidgetTester tester) async {
        final Host first = Fixtures.buildHost(
          displayName: 'Shared Host Name',
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final Host second = Fixtures.buildHost(
          displayName: 'Shared Host Name',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        final List<Host> selected = [];

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

        await tester.tap(find.byKey(const Key('host-card-ws://127.0.0.1:2/')));
        await tester.pump();

        expect(selected, [second]);
      },
    );

    testWidgets(
      'ConnectionsHostSection does not call onSelectHost before a tap',
      (WidgetTester tester) async {
        final List<Host> selected = [];
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
              onSelectHost: (Host host) {},
            ),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          expect(
            find.bySemanticsLabel(
              'Local Host, DovahLink Host, 127.0.0.1:58231, Not connected',
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
}
