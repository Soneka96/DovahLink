import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/app/app.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

void main() {
  testWidgets('renders the disconnected shell before a connection exists', (
    WidgetTester tester,
  ) async {
    initDependencies();
    await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

    expect(find.byKey(const Key('connection-status')), findsOneWidget);
    expect(find.text('Disconnected'), findsOneWidget);
  });
}
