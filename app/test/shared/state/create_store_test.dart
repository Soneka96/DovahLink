import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

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
}
