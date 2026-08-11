import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

void main() {
  test('creates an independent store with initial state', () {
    const AppCompositionRoot root = AppCompositionRoot();

    final firstStore = root.createStore();
    final secondStore = root.createStore();

    expect(firstStore.state, isA<AppState>());
    expect(secondStore.state, isA<AppState>());
    expect(identical(firstStore, secondStore), isFalse);
  });
}
