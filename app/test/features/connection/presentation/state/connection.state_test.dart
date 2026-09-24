import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

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

    test('ConnectionState initial has no selected Host', () {
      final ConnectionState state = ConnectionState.initial();

      expect(state.selectedHost, isNull);
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

    test('ConnectionState copyWith preserves selectedHost when omitted', () {
      final Host host = Fixtures.buildHost();
      final ConnectionState state = ConnectionState(selectedHost: host);

      final ConnectionState result = state.copyWith();

      expect(result.selectedHost, host);
    });

    test('ConnectionState copyWith replaces selectedHost when set', () {
      final Host host = Fixtures.buildHost();

      final ConnectionState result = ConnectionState.initial().copyWith(
        selectedHost: Some(host),
      );

      expect(result.selectedHost, host);
    });

    test('ConnectionState copyWith clears selectedHost when None', () {
      final ConnectionState state = ConnectionState(
        selectedHost: Fixtures.buildHost(),
      );

      final ConnectionState result = state.copyWith(selectedHost: const None());

      expect(result.selectedHost, isNull);
    });
  });

  group('ConnectionState — equality', () {
    test('ConnectionState differs when only selectedHost differs', () {
      final ConnectionState unselected = ConnectionState.initial();
      final ConnectionState selected = unselected.copyWith(
        selectedHost: Some(Fixtures.buildHost()),
      );

      expect(selected, isNot(unselected));
    });
  });
}
