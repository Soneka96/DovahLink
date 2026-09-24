import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/host_list.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/host_list_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../fixtures/fixtures.dart';

/// Mocktail double for [NavigatorService], matching this project's existing
/// mock-the-concrete-class convention for it (see `navigator_service_test.dart`'s `MockGoRouter`).
class MockNavigatorService extends Mock implements NavigatorService {}

/// Exercises Host-list rendering and selection behavior.
void main() {
  late MockNavigatorService mockNavigatorService;

  setUp(() async {
    await sl.reset();
    mockNavigatorService = MockNavigatorService();
    sl.registerLazySingleton<NavigatorService>(() => mockNavigatorService);
    initConnectionDependencies();
  });

  group('HostListScreen contains widgets', () {
    testWidgets('HostListScreen contains the static default Host tile', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: const CreateStore()(),
            child: const HostListScreen(),
          ),
        ),
      );

      expect(find.byKey(const Key('host-tile-Local Host')), findsOneWidget);
      expect(find.text('Local Host'), findsOneWidget);
    });
  });

  group('HostListScreen selection', () {
    testWidgets('HostListScreen tapping a Host tile navigates to pairing', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: const CreateStore()(
              middleware: [ConnectionMiddleware().call],
            ),
            child: const HostListScreen(),
          ),
        ),
      );

      await tester.tap(find.byKey(const Key('host-tile-Local Host')));
      await tester.pump();

      verify(() => mockNavigatorService.go(AppRoutes.pairing)).called(1);
    });

    testWidgets(
      'HostListScreen tapping the second tile passes that Host, not the first',
      (WidgetTester tester) async {
        final HostEntity first = Fixtures.buildHostEntity(
          displayName: 'First Host',
          uri: Uri.parse('ws://127.0.0.1:1/'),
        );
        final HostEntity second = Fixtures.buildHostEntity(
          displayName: 'Second Host',
          uri: Uri.parse('ws://127.0.0.1:2/'),
        );
        HostEntity? selected;
        sl.unregister<HostListScreenViewModel>();
        sl.registerFactoryParam<HostListScreenViewModel, Store<AppState>, void>(
          (Store<AppState> store, void _) => HostListScreenViewModel(
            hosts: [first, second],
            onSelectHost: (HostEntity host) => selected = host,
          ),
        );

        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: const CreateStore()(),
              child: const HostListScreen(),
            ),
          ),
        );

        await tester.tap(find.byKey(const Key('host-tile-Second Host')));
        await tester.pump();

        expect(selected, second);
      },
    );
  });

  group('HostListScreen meets accessibility recommended guidelines', () {
    testWidgets(
      'HostListScreen labels the Host tile and meets its minimum tap-target size',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(
            MaterialApp(
              home: StoreProvider<AppState>(
                store: const CreateStore()(),
                child: const HostListScreen(),
              ),
            ),
          );

          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
        } finally {
          handle.dispose();
        }
      },
    );

    testWidgets('HostListScreen exposes the Host tile label as semantics', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: const CreateStore()(),
              child: const HostListScreen(),
            ),
          ),
        );

        expect(
          tester.getSemantics(find.byKey(const Key('host-tile-Local Host'))),
          matchesSemantics(
            label: 'Local Host',
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
