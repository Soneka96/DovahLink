import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Mocktail double for [NavigatorService], matching this project's existing
/// mock-the-concrete-class convention for it (see `navigator_service_test.dart`'s `MockGoRouter`).
class MockNavigatorService extends Mock implements NavigatorService {}

/// Exercises [ConnectionMiddleware]'s dispatched-action forwarding.
void main() {
  late MockNavigatorService mockNavigatorService;
  late Store<AppState> store;

  setUp(() async {
    await sl.reset();
    mockNavigatorService = MockNavigatorService();
    sl.registerLazySingleton<NavigatorService>(() => mockNavigatorService);
    store = const CreateStore()(middleware: [ConnectionMiddleware().call]);
  });

  group('ConnectionMiddleware — ConnectionBridgeSelectedAction', () {
    test('navigates to the pairing route', () {
      final BridgeEntity bridge = BridgeEntity(
        displayName: 'Local Bridge',
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      store.dispatch(ConnectionBridgeSelectedAction(bridge));

      verify(() => mockNavigatorService.go(AppRoutes.pairing)).called(1);
    });
  });
}
