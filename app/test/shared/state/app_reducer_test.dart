import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/state/app_reducer.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

void main() {
  test('preserves the current state for an unknown action', () {
    const AppState state = AppState();

    expect(identical(appReducer(state, Object()), state), isTrue);
  });
}
