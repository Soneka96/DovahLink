import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/bridge_list.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/bridge_list_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

import '../../../../fixtures/fixtures.dart';

/// Mocktail double for [NavigatorService], matching this project's existing
/// mock-the-concrete-class convention for it (see `navigator_service_test.dart`'s `MockGoRouter`).
class MockNavigatorService extends Mock implements NavigatorService {}

/// Exercises Bridge-list rendering and selection behavior.
void main() {
  late MockNavigatorService mockNavigatorService;

  setUp(() async {
    await sl.reset();
    mockNavigatorService = MockNavigatorService();
    sl.registerLazySingleton<NavigatorService>(() => mockNavigatorService);
    initConnectionDependencies();
  });

  group('BridgeListScreen contains widgets', () {
    testWidgets('BridgeListScreen contains the static default Bridge tile', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: const CreateStore()(),
            child: const BridgeListScreen(),
          ),
        ),
      );

      expect(find.byKey(const Key('bridge-tile-Local Bridge')), findsOneWidget);
      expect(find.text('Local Bridge'), findsOneWidget);
    });
  });

  group('BridgeListScreen selection', () {
    testWidgets('BridgeListScreen tapping a Bridge tile navigates to pairing', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: const CreateStore()(
              middleware: [ConnectionMiddleware().call],
            ),
            child: const BridgeListScreen(),
          ),
        ),
      );

      await tester.tap(find.byKey(const Key('bridge-tile-Local Bridge')));
      await tester.pump();

      verify(() => mockNavigatorService.go(AppRoutes.pairing)).called(1);
    });

    testWidgets(
      'BridgeListScreen tapping the second tile passes that Bridge, not the first',
      (WidgetTester tester) async {
        final BridgeEntity first = Fixtures.buildBridgeEntity(
          displayName: 'First Bridge',
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final BridgeEntity second = Fixtures.buildBridgeEntity(
          displayName: 'Second Bridge',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        BridgeEntity? selected;
        sl.unregister<BridgeListScreenViewModel>();
        sl.registerFactoryParam<
          BridgeListScreenViewModel,
          Store<AppState>,
          void
        >(
          (Store<AppState> store, void _) => BridgeListScreenViewModel(
            bridges: [first, second],
            onSelectBridge: (BridgeEntity bridge) => selected = bridge,
          ),
        );

        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: const CreateStore()(),
              child: const BridgeListScreen(),
            ),
          ),
        );

        await tester.tap(find.byKey(const Key('bridge-tile-Second Bridge')));
        await tester.pump();

        expect(selected, second);
      },
    );
  });

  group('BridgeListScreen meets accessibility recommended guidelines', () {
    testWidgets(
      'BridgeListScreen labels the Bridge tile and meets its minimum tap-target size',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(
            MaterialApp(
              home: StoreProvider<AppState>(
                store: const CreateStore()(),
                child: const BridgeListScreen(),
              ),
            ),
          );

          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
        } finally {
          handle.dispose();
        }
      },
    );

    testWidgets('BridgeListScreen exposes the Bridge tile label as semantics', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: const CreateStore()(),
              child: const BridgeListScreen(),
            ),
          ),
        );

        expect(
          tester.getSemantics(
            find.byKey(const Key('bridge-tile-Local Bridge')),
          ),
          matchesSemantics(
            label: 'Local Bridge',
            isButton: true,
            isEnabled: true,
            isFocusable: true,
            hasEnabledState: true,
            hasSelectedState: true,
            hasTapAction: true,
            hasFocusAction: true,
          ),
        );
      } finally {
        handle.dispose();
      }
    });
  });
}
