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

/// Mocktail double for [NavigatorService], matching this project's existing
/// mock-the-concrete-class convention for it (see `navigator_service_test.dart`'s `MockGoRouter`).
class MockNavigatorService extends Mock implements NavigatorService {}

/// Mocktail double for [Store], called directly rather than dispatched
/// through -- `dispatch` and the middleware's own `next` both append to one
/// action log, so no real reducer is involved.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [ConnectionMiddleware] in isolation: calls
/// `middleware.call(store, action, next)` directly with the action under
/// test, rather than dispatching through a real Store.
void main() {
  late ConnectionMiddleware middleware;
  late MockNavigatorService mockNavigatorService;
  late MockStore store;
  late List<Object?> actionLog;

  void next(dynamic action) => actionLog.add(action);

  setUp(() async {
    await sl.reset();
    middleware = ConnectionMiddleware();
    mockNavigatorService = MockNavigatorService();
    sl.registerLazySingleton<NavigatorService>(() => mockNavigatorService);

    actionLog = [];
    store = MockStore();
    when(() => store.dispatch(any())).thenAnswer(
      (Invocation invocation) =>
          actionLog.add(invocation.positionalArguments[0]),
    );
  });

  tearDown(() async {
    await sl.reset();
    reset(mockNavigatorService);
    reset(store);
  });

  group('ConnectionMiddleware — ConnectionBridgeSelectedAction', () {
    test('navigates to the pairing route', () {
      final BridgeEntity bridge = BridgeEntity(
        displayName: 'Local Bridge',
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );

      middleware.call(store, ConnectionBridgeSelectedAction(bridge), next);

      expect(actionLog, [ConnectionBridgeSelectedAction(bridge)]);
      verify(() => mockNavigatorService.go(AppRoutes.pairing)).called(1);
    });
  });
}
