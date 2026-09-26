import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/hello_ack_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Reads one canonical protocol fixture, relative to `protocol/fixtures/`.
JsonMap _readFixture(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  return jsonDecode(file.readAsStringSync()) as JsonMap;
}

/// Runs [HelloAckPayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the canonical hello_ack fixture', () {
      final JsonMap json =
          _readFixture('connection/hello-ack.json')['payload'] as JsonMap;

      final HelloAckPayload payload = HelloAckPayload.fromJson(json);

      expect(payload.hostVersion, json['hostVersion'] as String);
      expect(payload.hostId, json['hostId']);
      expect(payload.hostId, isNotEmpty);
      expect(payload.hostName, json['hostName']);
      expect(payload.hostName, isNotEmpty);
      expect(payload.clientIdentityKind, ClientIdentityKind.unpaired);
    });

    test('Method fromJson decodes a paired clientIdentityKind', () {
      final JsonMap json =
          _readFixture('connection/hello-ack-paired.json')['payload']
              as JsonMap;
      final HelloAckPayload payload = HelloAckPayload.fromJson(json);

      expect(payload.clientIdentityKind, ClientIdentityKind.paired);
    });

    test(
      'Method fromJson rejects an unrecognized clientIdentityKind as ProtocolFormatException',
      () {
        expect(
          () => HelloAckPayload.fromJson(<String, dynamic>{
            'hostVersion': '0.2.0',
            'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
            'hostName': 'Soneka-Desktop',
            'clientIdentityKind': 'not-a-real-kind',
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test('Method fromJson rejects a payload missing a required key', () {
      final JsonMap withMissingKey =
          (_readFixture('connection/hello-ack.json')['payload'] as JsonMap)
            ..remove('hostVersion');

      expect(
        () => HelloAckPayload.fromJson(withMissingKey),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test(
      'Method fromJson rejects a payload with the wrong type for a required key',
      () {
        final JsonMap withWrongType =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        withWrongType['hostVersion'] = 42;

        expect(
          () => HelloAckPayload.fromJson(withWrongType),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test('Method fromJson rejects a payload missing clientIdentityKind', () {
      final JsonMap withMissingKey =
          (_readFixture('connection/hello-ack.json')['payload'] as JsonMap)
            ..remove('clientIdentityKind');

      expect(
        () => HelloAckPayload.fromJson(withMissingKey),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    for (final String field in <String>['hostId', 'hostName']) {
      test('Method fromJson rejects a payload missing $field', () {
        final JsonMap withMissingKey =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        withMissingKey.remove(field);

        expect(
          () => HelloAckPayload.fromJson(withMissingKey),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test('Method fromJson rejects $field with the wrong type', () {
        final JsonMap withWrongType =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        withWrongType[field] = 42;

        expect(
          () => HelloAckPayload.fromJson(withWrongType),
          throwsA(isA<ProtocolFormatException>()),
        );
      });
    }

    for (final String hostId in <String>[
      '',
      'not-a-uuid',
      '00000000-0000-0000-0000-000000000000',
    ]) {
      test('Method fromJson rejects invalid hostId $hostId', () {
        final JsonMap payload =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        payload['hostId'] = hostId;

        expect(
          () => HelloAckPayload.fromJson(payload),
          throwsA(isA<ProtocolFormatException>()),
        );
      });
    }

    test('Method fromJson rejects an empty or control-containing hostName', () {
      for (final String hostName in <String>[
        '',
        '   ',
        'DESKTOP\nPC',
        'DESKTOP\u007fPC',
        'DESKTOP\u0085PC',
      ]) {
        final JsonMap payload =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        payload['hostName'] = hostName;

        expect(
          () => HelloAckPayload.fromJson(payload),
          throwsA(isA<ProtocolFormatException>()),
        );
      }
    });

    test('Method fromJson enforces the UTF-8 byte bound for hostName', () {
      final String atLimit = List<String>.filled(32, 'é').join();
      final JsonMap accepted =
          _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
      accepted['hostName'] = atLimit;
      expect(HelloAckPayload.fromJson(accepted).hostName, atLimit);

      final JsonMap supplementaryCharacter =
          _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
      supplementaryCharacter['hostName'] = 'PC-😀';
      expect(
        HelloAckPayload.fromJson(supplementaryCharacter).hostName,
        'PC-😀',
      );

      final JsonMap rejected =
          _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
      rejected['hostName'] = '$atLimit\u00e9';
      expect(
        () => HelloAckPayload.fromJson(rejected),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    for (final String hostName in <String>[
      'DESKTOP\uD800',
      '\uDC00DESKTOP',
      'DESKTOP\uD800PC',
    ]) {
      test('Method fromJson rejects a hostName with unpaired surrogates', () {
        final JsonMap payload =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        payload['hostName'] = hostName;

        expect(
          () => HelloAckPayload.fromJson(payload),
          throwsA(isA<ProtocolFormatException>()),
        );
      });
    }

    test(
      'Method fromJson rejects a payload with the wrong type for clientIdentityKind',
      () {
        final JsonMap withWrongType =
            _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
        withWrongType['clientIdentityKind'] = 42;

        expect(
          () => HelloAckPayload.fromJson(withWrongType),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );
    test('Method fromJson rejects an empty hostVersion', () {
      final JsonMap payload =
          _readFixture('connection/hello-ack.json')['payload'] as JsonMap;
      payload['hostVersion'] = '';

      expect(
        () => HelloAckPayload.fromJson(payload),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
