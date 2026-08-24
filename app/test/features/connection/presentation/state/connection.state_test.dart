import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

/// Exercises connection-state initialization and copying.
void main() {
  group('ConnectionState — initial', () {
    test('creates a state with the static default Bridge', () {
      final ConnectionState state = ConnectionState.initial();

      expect(state.bridges, [
        BridgeEntity(displayName: 'Local Bridge', uri: defaultBridgeUri),
      ]);
    });
  });

  group('ConnectionState — copyWith', () {
    test('preserves bridges when omitted', () {
      final ConnectionState state = ConnectionState.initial();

      final ConnectionState result = state.copyWith();

      expect(result.bridges, state.bridges);
    });

    test('replaces bridges when supplied', () {
      final ConnectionState state = ConnectionState.initial();
      final List<BridgeEntity> replacement = [
        BridgeEntity(displayName: 'Other Bridge', uri: defaultBridgeUri),
      ];

      final ConnectionState result = state.copyWith(bridges: replacement);

      expect(result.bridges, replacement);
    });
  });
}
