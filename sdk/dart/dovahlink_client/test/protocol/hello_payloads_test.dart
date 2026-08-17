import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/enums.dart';
import 'package:dovahlink_client_sdk/src/protocol/hello_payloads.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

/// Reads one canonical protocol fixture, relative to `protocol/fixtures/`.
JsonMap _readFixture(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  return jsonDecode(file.readAsStringSync()) as JsonMap;
}

void main() {
  group('HelloPayload', () {
    group('methods', () {
      test('toJson matches the canonical one_time_local_token fixture', () {
        const HelloPayload payload = HelloPayload(
          clientId: 'client-1',
          authMethod: AuthMethod.oneTimeLocalToken,
          authToken: 'redacted-in-documentation',
        );

        expect(
          payload.toJson(),
          _readFixture('connection/hello.json')['payload'],
        );
      });

      test(
        'toJson matches the canonical trusted_device_credential fixture',
        () {
          const HelloPayload payload = HelloPayload(
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
        'toJson omits auth.token entirely for unpaired, matching the canonical fixture',
        () {
          const HelloPayload payload = HelloPayload(
            clientId: 'client-1',
            authMethod: AuthMethod.unpaired,
          );

          final JsonMap json = payload.toJson();
          final JsonMap auth = json['auth'] as JsonMap;

          expect(
            json,
            _readFixture('connection/hello-unpaired.json')['payload'],
          );
          expect(auth.containsKey('token'), isFalse);
        },
      );

      test('rejects constructing unpaired with a non-null authToken', () {
        expect(
          () => HelloPayload(
            clientId: 'client-1',
            authMethod: AuthMethod.unpaired,
            authToken: 'x',
          ),
          throwsA(isA<AssertionError>()),
        );
      });

      test(
        'rejects constructing a credentialed auth method with a null authToken',
        () {
          expect(
            () => HelloPayload(
              clientId: 'client-1',
              authMethod: AuthMethod.trustedDeviceCredential,
            ),
            throwsA(isA<AssertionError>()),
          );
        },
      );
    });
  });

  group('HelloAckPayload', () {
    group('methods', () {
      test('fromJson decodes the canonical hello_ack fixture', () {
        final JsonMap json =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;

        final HelloAckPayload payload = HelloAckPayload.fromJson(json);

        expect(payload.bridgeVersion, '0.2.0');
        expect(payload.clientIdentityKind, 'unpaired');
      });

      test('fromJson decodes a paired clientIdentityKind', () {
        // No canonical fixture carries "paired" yet, so this hand-builds the one case none of
        // the shared connection fixtures cover.
        final HelloAckPayload payload = HelloAckPayload.fromJson(
          <String, dynamic>{
            'bridgeVersion': '0.2.0',
            'clientIdentityKind': 'paired',
          },
        );

        expect(payload.clientIdentityKind, 'paired');
      });

      test('fromJson rejects a payload missing a required key', () {
        final JsonMap withMissingKey =
            (_readFixture('connection/hello-ack.json')['payload'] as JsonMap)
              ..remove('bridgeVersion');

        expect(
          () => HelloAckPayload.fromJson(withMissingKey),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test(
        'fromJson rejects a payload with the wrong type for a required key',
        () {
          final JsonMap withWrongType =
              _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
          withWrongType['bridgeVersion'] = 42;

          expect(
            () => HelloAckPayload.fromJson(withWrongType),
            throwsA(isA<ProtocolFormatException>()),
          );
        },
      );
    });
  });
}
