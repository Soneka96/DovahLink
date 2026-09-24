import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises connection-state initialization and copying.
void main() {
  group('ConnectionState — initial', () {
    test('creates a state with the static default Host', () {
      final ConnectionState state = ConnectionState.initial();

      expect(state.hosts, [Fixtures.buildHost()]);
    });
  });

  group('ConnectionState — copyWith', () {
    test('preserves hosts when omitted', () {
      final ConnectionState state = ConnectionState.initial();

      final ConnectionState result = state.copyWith();

      expect(result.hosts, state.hosts);
    });

    test('replaces hosts when supplied', () {
      final ConnectionState state = ConnectionState.initial();
      final List<Host> replacement = [
        Fixtures.buildHost(displayName: 'Other Host'),
      ];

      final ConnectionState result = state.copyWith(hosts: replacement);

      expect(result.hosts, replacement);
    });
  });
}
