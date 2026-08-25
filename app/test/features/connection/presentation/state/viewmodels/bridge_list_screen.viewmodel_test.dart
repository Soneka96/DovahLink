import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/bridge_list_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

import '../../../../../fixtures/fixtures.dart';

/// Mocktail double for [NavigatorService], matching this project's existing
/// mock-the-concrete-class convention for it (see `navigator_service_test.dart`'s `MockGoRouter`).
class MockNavigatorService extends Mock implements NavigatorService {}

/// Exercises [BridgeListScreenViewModel.fromStore] projections.
void main() {
  late MockNavigatorService mockNavigatorService;

  setUp(() async {
    await sl.reset();
    mockNavigatorService = MockNavigatorService();
    sl.registerLazySingleton<NavigatorService>(() => mockNavigatorService);
  });

  group('BridgeListScreenViewModel fromStore()', () {
    test('fromStore selects the static default Bridge', () {
      final Store<AppState> store = const CreateStore()();

      final BridgeListScreenViewModel viewModel =
          BridgeListScreenViewModel.fromStore(store);

      expect(viewModel.bridges, [
        Fixtures.buildBridgeEntity(),
      ]);
    });

    test('onSelectBridge dispatches ConnectionBridgeSelectedAction, navigating to pairing', () {
      final Store<AppState> store = const CreateStore()(
        middleware: [ConnectionMiddleware().call],
      );
      final BridgeListScreenViewModel viewModel =
          BridgeListScreenViewModel.fromStore(store);

      viewModel.onSelectBridge(viewModel.bridges.single);

      verify(() => mockNavigatorService.go(AppRoutes.pairing)).called(1);
    });

    test('two ViewModels with the same bridges are equal, regardless of callback identity', () {
      final BridgeListScreenViewModel first = BridgeListScreenViewModel.fromStore(
        const CreateStore()(),
      );
      final BridgeListScreenViewModel second = BridgeListScreenViewModel.fromStore(
        const CreateStore()(),
      );

      expect(first, second);
    });
  });
}
