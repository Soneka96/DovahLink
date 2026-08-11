import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

void main() {
  group('AppState — initial', () {
    test('initial creates the connection state', () {
      final AppState state = AppState.initial();

      expect(state, isA<AppState>());
      expect(state.connection, isA<ConnectionState>());
      expect(state.connection.phase, ConnectionPhase.disconnected);
      expect(state.connection.session, isNull);
      expect(state.connection.error, isNull);
    });
  });
}
