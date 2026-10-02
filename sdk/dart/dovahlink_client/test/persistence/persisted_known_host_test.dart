import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import '../fixtures/fixtures.dart';

/// Runs persisted Host relationship behavior tests.
void main() {
  group('Behavior equality behaves correctly', () {
    test(
      'Behavior equality compares metadata, credential, and recovery hint',
      () {
        final PersistedKnownHost relationship = PersistedKnownHost(
          host: Fixtures.buildDovahLinkHost(),
          credential: 'credential-a',
          pairingRequired: true,
        );
        final PersistedKnownHost equivalent = PersistedKnownHost(
          host: Fixtures.buildDovahLinkHost(),
          credential: 'credential-a',
          pairingRequired: true,
        );
        final PersistedKnownHost otherCredential = PersistedKnownHost(
          host: Fixtures.buildDovahLinkHost(),
          credential: 'credential-b',
          pairingRequired: true,
        );
        final PersistedKnownHost otherRecoveryHint = PersistedKnownHost(
          host: Fixtures.buildDovahLinkHost(),
          credential: 'credential-a',
        );

        expect(relationship, equivalent);
        expect(relationship.hashCode, equivalent.hashCode);
        expect(relationship, isNot(otherCredential));
        expect(relationship, isNot(otherRecoveryHint));
      },
    );
  });

  group('Property pairingRequired behaves correctly', () {
    test('Property pairingRequired defaults to false', () {
      final PersistedKnownHost relationship = PersistedKnownHost(
        host: Fixtures.buildDovahLinkHost(),
      );

      expect(relationship.pairingRequired, isFalse);
    });
  });
}
