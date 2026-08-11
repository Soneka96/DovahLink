import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/app/app.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

void main() {
  testWidgets('renders the disconnected shell before a connection exists', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

    expect(find.text('DovahLink is not connected'), findsOneWidget);
  });
}
