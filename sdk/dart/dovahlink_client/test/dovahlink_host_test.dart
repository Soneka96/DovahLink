import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';

void main() {
  group('Property hostId behaves correctly', () {
    test('Property hostId preserves the confirmed Host identity', () {
      final DovahLinkHost host = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'GONCALO-DESKTOP',
        endpoint: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(host.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
    });
  });

  group('Property hostName behaves correctly', () {
    test('Property hostName preserves mutable display metadata', () {
      final DovahLinkHost host = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'GONCALO-DESKTOP',
        endpoint: Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(host.hostName, 'GONCALO-DESKTOP');
    });
  });

  group('Property endpoint behaves correctly', () {
    test('Property endpoint preserves the current connection location', () {
      final Uri endpoint = Uri.parse('ws://127.0.0.1:58231/');
      final DovahLinkHost host = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'GONCALO-DESKTOP',
        endpoint: endpoint,
      );

      expect(host.endpoint, endpoint);
    });
  });
}
