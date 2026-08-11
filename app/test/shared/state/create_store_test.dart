import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

void main() {
  test('creates a distinct Redux store with the initial state', () {
    final store = const CreateStore()();
    final initialState = store.state;

    expect(store.state, isA<AppState>());
    store.dispatch(Object());
    expect(identical(store.state, initialState), isTrue);
  });
}
