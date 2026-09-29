import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import '../fixtures/fixtures.dart';

/// Runs persisted Host relationship behavior tests.
void main() {
  test('PersistedKnownHost compares metadata and its owning credential', () {
    final relationship = PersistedKnownHost(
      host: Fixtures.buildDovahLinkHost(),
      credential: 'credential-a',
    );
    final equivalent = PersistedKnownHost(
      host: Fixtures.buildDovahLinkHost(),
      credential: 'credential-a',
    );
    final otherCredential = PersistedKnownHost(
      host: Fixtures.buildDovahLinkHost(),
      credential: 'credential-b',
    );

    expect(relationship, equivalent);
    expect(relationship.hashCode, equivalent.hashCode);
    expect(relationship, isNot(otherCredential));
  });
}
