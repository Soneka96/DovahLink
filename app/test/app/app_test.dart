import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/app/app.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Exercises the root application shell before connection.
void main() {
  group('DovahLinkApp', () {
    testWidgets('renders the Bridge list before a connection exists', (
      WidgetTester tester,
    ) async {
      initDependencies();
      await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

      expect(
        find.byKey(const Key('bridge-tile-Local Bridge')),
        findsOneWidget,
      );
      expect(find.text('Local Bridge'), findsOneWidget);
    });

    testWidgets('resolves the pairing route through the real app shell', (
      WidgetTester tester,
    ) async {
      initDependencies();
      await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

      sl<GoRouter>().go(AppRoutes.pairing);
      // Not pumpAndSettle: PairingScreen auto-starts a real connection attempt
      // with no bridge listening in this test, so it retries forever by
      // design and never quiesces. The route-transition duration is enough
      // to mount the destination screen, which is all this asserts.
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));

      expect(find.byKey(const Key('pairing-status')), findsOneWidget);
      expect(find.byKey(const Key('bridge-tile-Local Bridge')), findsNothing);
    });

    testWidgets(
      'NavigatorService navigates the same router instance backing the app shell',
      (WidgetTester tester) async {
        initDependencies();
        await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

        sl<NavigatorService>().go(AppRoutes.pairing);
        // See the comment above: PairingScreen never quiesces without a real
        // bridge, so this waits out the route transition instead.
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));

        expect(find.byKey(const Key('pairing-status')), findsOneWidget);
        expect(
          find.byKey(const Key('bridge-tile-Local Bridge')),
          findsNothing,
        );
      },
    );
  });
}
