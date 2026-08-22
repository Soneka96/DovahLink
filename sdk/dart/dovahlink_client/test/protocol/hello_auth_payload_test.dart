import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/hello_auth_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [HelloAuthPayload.toJson] behavior tests.
void main() {
  group('Method toJson behaves correctly', () {
    test('Method toJson encodes method and token when both are present', () {
      const HelloAuthPayload payload = HelloAuthPayload(
        method: AuthMethod.oneTimeLocalToken,
        token: 'redacted-in-documentation',
      );

      expect(payload.toJson(), <String, dynamic>{
        'method': 'one_time_local_token',
        'token': 'redacted-in-documentation',
      });
    });

    test('Method toJson encodes trustedDeviceCredential\'s wire value', () {
      const HelloAuthPayload payload = HelloAuthPayload(
        method: AuthMethod.trustedDeviceCredential,
        token: 'redacted-in-documentation',
      );

      expect(payload.toJson(), <String, dynamic>{
        'method': 'trusted_device_credential',
        'token': 'redacted-in-documentation',
      });
    });

    test('Method toJson omits token entirely when absent', () {
      const HelloAuthPayload payload = HelloAuthPayload(
        method: AuthMethod.unpaired,
      );

      final JsonMap json = payload.toJson();

      expect(json['method'], 'unpaired');
      expect(json.containsKey('token'), isFalse);
    });
  });
}
