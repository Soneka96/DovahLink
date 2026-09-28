import 'package:dovahlink_client_sdk/dovahlink_client.dart' show DovahLinkHost;
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/host.mapper.dart';

/// Exercises the single SDK-to-app Host mapping boundary.
void main() {
  group('Method fromSdk behaves correctly', () {
    test('HostMapper.fromSdk maps identity, name, and endpoint', () {
      final DovahLinkHost sdkHost = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'SKYRIM-PC',
        endpoint: Uri.parse('ws://127.0.0.1:58231/'),
      );

      final host = HostMapper.fromSdk(sdkHost);

      expect(host.hostId, isA<String>());
      expect(host.hostId, sdkHost.hostId);
      expect(host.displayName, isA<String>());
      expect(host.displayName, 'SKYRIM-PC');
      expect(host.uri, isA<Uri>());
      expect(host.uri, sdkHost.endpoint);
    });
  });
}
