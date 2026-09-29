import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// Runs unknown Known Host failure behavior tests.
void main() {
  test('DovahLinkKnownHostNotFoundException retains the requested ID', () {
    const DovahLinkKnownHostNotFoundException error =
        DovahLinkKnownHostNotFoundException('host-id');

    expect(error.hostId, 'host-id');
    expect(error.toString(), contains('host-id'));
  });
}
