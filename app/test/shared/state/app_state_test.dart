import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/state/app_state.dart';

void main() {
  test('initial creates an empty application state', () {
    expect(AppState.initial(), isA<AppState>());
  });
}
