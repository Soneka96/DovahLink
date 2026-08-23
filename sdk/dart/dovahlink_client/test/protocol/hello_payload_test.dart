import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/hello_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Reads one canonical protocol fixture, relative to `protocol/fixtures/`.
JsonMap _readFixture(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  return jsonDecode(file.readAsStringSync()) as JsonMap;
}

/// Runs [HelloPayload.toJson] and constructor behavior tests.
void main() {
  group('Method toJson behaves correctly', () {
    test(
      'Method toJson matches the canonical one_time_local_token fixture',
      () {
        final HelloPayload payload = HelloPayload(
          clientId: 'client-1',
          authMethod: AuthMethod.oneTimeLocalToken,
          authToken: 'redacted-in-documentation',
        );

        expect(payload.endpoint, ProtocolEndpoint.client);
        expect(
          payload.toJson(),
          _readFixture('connection/hello.json')['payload'],
        );
      },
    );

    test(
      'Method toJson matches the canonical trusted_device_credential fixture',
      () {
        final HelloPayload payload = HelloPayload(
          clientId: 'client-1',
          authMethod: AuthMethod.trustedDeviceCredential,
          authToken: 'redacted-in-documentation',
        );

        expect(
          payload.toJson(),
          _readFixture(
            'connection/hello-trusted-device-credential.json',
          )['payload'],
        );
      },
    );

    test(
      'Method toJson omits auth.token entirely for unpaired, matching the canonical fixture',
      () {
        final HelloPayload payload = HelloPayload(
          clientId: 'client-1',
          authMethod: AuthMethod.unpaired,
        );

        final JsonMap json = payload.toJson();
        final JsonMap auth = json['auth'] as JsonMap;

        expect(json, _readFixture('connection/hello-unpaired.json')['payload']);
        expect(auth.containsKey('token'), isFalse);
      },
    );
  });

  group('Method constructor behaves correctly', () {
    test('Method constructor rejects unpaired with a non-null authToken', () {
      expect(
        () => HelloPayload(
          clientId: 'client-1',
          authMethod: AuthMethod.unpaired,
          authToken: 'x',
        ),
        throwsA(isA<ArgumentError>()),
      );
    });

    test(
      'Method constructor rejects a credentialed auth method with a null authToken',
      () {
        expect(
          () => HelloPayload(
            clientId: 'client-1',
            authMethod: AuthMethod.trustedDeviceCredential,
          ),
          throwsA(isA<ArgumentError>()),
        );
      },
    );

    test(
      'Method constructor rejects oneTimeLocalToken with a null authToken',
      () {
        expect(
          () => HelloPayload(
            clientId: 'client-1',
            authMethod: AuthMethod.oneTimeLocalToken,
          ),
          throwsA(isA<ArgumentError>()),
        );
      },
    );
  });
}
