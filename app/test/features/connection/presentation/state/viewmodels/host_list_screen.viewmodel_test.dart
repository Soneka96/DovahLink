import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/host_list_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../../fixtures/fixtures.dart';

/// Mocktail double for [NavigatorService], matching this project's existing
/// mock-the-concrete-class convention for it (see `navigator_service_test.dart`'s `MockGoRouter`).
class MockNavigatorService extends Mock implements NavigatorService {}

/// Exercises [HostListScreenViewModel.fromStore] projections.
void main() {
  late MockNavigatorService mockNavigatorService;

  setUp(() async {
    await sl.reset();
    mockNavigatorService = MockNavigatorService();
    sl.registerLazySingleton<NavigatorService>(() => mockNavigatorService);
  });

  group('HostListScreenViewModel fromStore()', () {
    test('fromStore selects the static default Host', () {
      final Store<AppState> store = const CreateStore()();

      final HostListScreenViewModel viewModel =
          HostListScreenViewModel.fromStore(store);

      expect(viewModel.hosts, [Fixtures.buildHostEntity()]);
    });

    test(
      'onSelectHost dispatches ConnectionHostSelectedAction, navigating to pairing',
      () {
        final Store<AppState> store = const CreateStore()(
          middleware: [ConnectionMiddleware().call],
        );
        final HostListScreenViewModel viewModel =
            HostListScreenViewModel.fromStore(store);

        viewModel.onSelectHost(viewModel.hosts.single);

        verify(() => mockNavigatorService.go(AppRoutes.pairing)).called(1);
      },
    );

    test(
      'two ViewModels with the same hosts are equal, regardless of callback identity',
      () {
        final HostListScreenViewModel first = HostListScreenViewModel.fromStore(
          const CreateStore()(),
        );
        final HostListScreenViewModel second =
            HostListScreenViewModel.fromStore(const CreateStore()());

        expect(first, second);
      },
    );
  });
}
