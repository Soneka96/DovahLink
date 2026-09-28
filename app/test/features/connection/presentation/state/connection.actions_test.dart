import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises the values carried by connection discovery actions.
void main() {
  group(
    'Behavior equality in ConnectionDiscoveryRequestedAction behaves correctly',
    () {
      test('ConnectionDiscoveryRequestedAction values compare equal', () {
        const ConnectionDiscoveryRequestedAction first =
            ConnectionDiscoveryRequestedAction();
        const ConnectionDiscoveryRequestedAction second =
            ConnectionDiscoveryRequestedAction();

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      });
    },
  );

  group(
    'Behavior equality in ConnectionDiscoveryStartedAction behaves correctly',
    () {
      test('ConnectionDiscoveryStartedAction values compare equal', () {
        const ConnectionDiscoveryStartedAction first =
            ConnectionDiscoveryStartedAction();
        const ConnectionDiscoveryStartedAction second =
            ConnectionDiscoveryStartedAction();

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      });
    },
  );

  group(
    'Property hosts in ConnectionDiscoverySucceededAction behaves correctly',
    () {
      test(
        'ConnectionDiscoverySucceededAction carries multiple candidates in order',
        () {
          final List<Host> hosts = [
            Host(displayName: 'Local Host', uri: defaultHostUri),
            Host(
              displayName: 'Second Host',
              uri: Uri.parse('ws://192.168.1.11:58231/'),
            ),
          ];
          final ConnectionDiscoverySucceededAction action =
              ConnectionDiscoverySucceededAction(hosts);

          expect(action.hosts, hosts);
        },
      );
    },
  );

  group(
    'Property failure in ConnectionDiscoveryFailedAction behaves correctly',
    () {
      test(
        'ConnectionDiscoveryFailedAction carries the semantic failure reason',
        () {
          const ConnectionDiscoveryFailedAction action =
              ConnectionDiscoveryFailedAction(
                ConnectionFailureReason.hostUnavailable,
              );

          expect(action.failure, ConnectionFailureReason.hostUnavailable);
        },
      );
    },
  );
}
