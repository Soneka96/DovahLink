import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/utils/existing_dovahlink_client.dart';

/// Exercises the holder that gives shutdown access to the pairing SDK client.
void main() {
  late ExistingDovahLinkClient existingClient;

  setUp(() {
    existingClient = ExistingDovahLinkClient();
  });

  group('Method disconnectIfCreated behaves correctly', () {
    test(
      'Method disconnectIfCreated completes without creating an unused client',
      () async {
        await expectLater(existingClient.disconnectIfCreated(), completes);

        expect(existingClient.hasClient, isFalse);
      },
    );

    test(
      'Method disconnectIfCreated disconnects a client after pairing creates it',
      () async {
        final DovahLinkClient client = DovahLinkClient(
          storage: const UnsupportedClientStorage(),
        );
        existingClient.clientCreated(client);

        await expectLater(existingClient.disconnectIfCreated(), completes);

        expect(existingClient.hasClient, isTrue);
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
      },
    );
  });
}
