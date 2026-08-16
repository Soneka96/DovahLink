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
    testWidgets('renders the disconnected shell before a connection exists', (
      WidgetTester tester,
    ) async {
      initDependencies();
      await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

      expect(find.byKey(const Key('connection-status')), findsOneWidget);
      expect(find.text('Disconnected'), findsOneWidget);
    });

    testWidgets('resolves the pairing route through the real app shell', (
      WidgetTester tester,
    ) async {
      initDependencies();
      await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

      sl<GoRouter>().go(AppRoutes.pairing);
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('pairing-placeholder')), findsOneWidget);
      expect(find.byKey(const Key('connection-status')), findsNothing);
    });

    testWidgets(
      'NavigatorService navigates the same router instance backing the app shell',
      (WidgetTester tester) async {
        initDependencies();
        await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

        sl<NavigatorService>().go(AppRoutes.pairing);
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('pairing-placeholder')), findsOneWidget);
        expect(find.byKey(const Key('connection-status')), findsNothing);
      },
    );
  });
}
