import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Exercises Redux store creation and dispatch wiring.
void main() {
  group('CreateStore — initial', () {
    test('creates a distinct Redux store with the initial state', () {
      final Store<AppState> store = const CreateStore()();
      final AppState initialState = store.state;

      expect(store.state, isA<AppState>());
      store.dispatch(Object());
      expect(identical(store.state, initialState), isTrue);
    });
  });

  group('CreateStore — dispatch', () {
    test('updates connection state for a handled action', () {
      final Store<AppState> store = const CreateStore()();

      store.dispatch(const ConnectionStartedAction());

      expect(store.state.connection.phase, ConnectionPhase.connecting);
    });
  });

  group('CreateStore — middleware', () {
    test('defaults to no middleware', () {
      final List<String> calls = [];
      final Store<AppState> store = const CreateStore()();

      store.dispatch(const ConnectionStartedAction());

      expect(calls, isEmpty);
      expect(store.state.connection.phase, ConnectionPhase.connecting);
    });

    test('wires provided middleware into the store', () {
      final List<String> calls = [];
      final Middleware<AppState> middleware =
          TypedMiddleware<AppState, ConnectionStartedAction>((
            Store<AppState> store,
            ConnectionStartedAction action,
            NextDispatcher next,
          ) {
            calls.add('called');
            next(action);
          }).call;
      final Store<AppState> store = const CreateStore()(
        middleware: [middleware],
      );

      store.dispatch(const ConnectionStartedAction());

      expect(calls, ['called']);
      expect(store.state.connection.phase, ConnectionPhase.connecting);
    });
  });
}
