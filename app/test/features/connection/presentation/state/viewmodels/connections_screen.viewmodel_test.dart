import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../../fixtures/fixtures.dart';

/// Mocktail double for [NavigatorService], matching this project's existing
/// mock-the-concrete-class convention for it (see `navigator_service_test.dart`'s `MockGoRouter`).
class MockNavigatorService extends Mock implements NavigatorService {}

/// Exercises [ConnectionsScreenViewModel.fromStore] projections.
void main() {
  late MockNavigatorService mockNavigatorService;

  setUp(() async {
    await sl.reset();
    mockNavigatorService = MockNavigatorService();
    sl.registerLazySingleton<NavigatorService>(() => mockNavigatorService);
  });

  group('ConnectionsScreenViewModel fromStore()', () {
    test('fromStore selects the card for the static default Host', () {
      final Store<AppState> store = const CreateStore()();

      final ConnectionsScreenViewModel viewModel =
          ConnectionsScreenViewModel.fromStore(store);

      expect(viewModel.hostCards, [Fixtures.buildHostCardModel()]);
    });

    test(
      'onSelectHost dispatches ConnectionHostSelectedAction, navigating to pairing',
      () {
        final Store<AppState> store = const CreateStore()(
          middleware: [ConnectionMiddleware().call],
        );
        final ConnectionsScreenViewModel viewModel =
            ConnectionsScreenViewModel.fromStore(store);

        viewModel.onSelectHost(viewModel.hostCards.single.host);

        verify(() => mockNavigatorService.go(AppRoutes.pairing)).called(1);
      },
    );

    test('onSelectHost does not navigate before a Host is selected', () {
      final Store<AppState> store = const CreateStore()(
        middleware: [ConnectionMiddleware().call],
      );
      ConnectionsScreenViewModel.fromStore(store);

      verifyNever(() => mockNavigatorService.go(any()));
    });

    test(
      'two ViewModels with the same cards are equal, regardless of callback identity',
      () {
        final ConnectionsScreenViewModel first =
            ConnectionsScreenViewModel.fromStore(const CreateStore()());
        final ConnectionsScreenViewModel second =
            ConnectionsScreenViewModel.fromStore(const CreateStore()());

        expect(first, second);
      },
    );

    test('two ViewModels with different cards are not equal', () {
      final ConnectionsScreenViewModel first = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardModel()],
        onSelectHost: (Host host) {},
      );
      final ConnectionsScreenViewModel second = ConnectionsScreenViewModel(
        hostCards: [Fixtures.buildHostCardModel(title: 'Other')],
        onSelectHost: (Host host) {},
      );

      expect(first, isNot(second));
    });
  });
}
