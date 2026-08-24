import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/capabilities_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/capability.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [CapabilitiesPayload.fromJson] and [CapabilitiesPayload.toJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson matches the canonical capabilities-bridge fixture', () {
      final CapabilitiesPayload payload = CapabilitiesPayload.fromJson(
        _readPayload('capabilities/capabilities-bridge.json'),
      );

      expect(payload.capabilities, isEmpty);
    });

    test('Method fromJson matches the canonical capabilities-client fixture', () {
      final CapabilitiesPayload payload = CapabilitiesPayload.fromJson(
        _readPayload('capabilities/capabilities-client.json'),
      );

      expect(payload.capabilities, isEmpty);
    });

    test('Method fromJson decodes a non-empty capabilities list', () {
      final CapabilitiesPayload payload = CapabilitiesPayload.fromJson(<String, dynamic>{
        'capabilities': <Map<String, dynamic>>[
          <String, dynamic>{'id': 'state.inventory', 'version': 1},
        ],
      });

      expect(payload.capabilities, hasLength(1));
      expect(payload.capabilities.single.id, 'state.inventory');
      expect(payload.capabilities.single.version, 1);
    });

    test('Method fromJson throws ProtocolFormatException when capabilities is missing', () {
      expect(
        () => CapabilitiesPayload.fromJson(<String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when an entry is malformed', () {
      expect(
        () => CapabilitiesPayload.fromJson(<String, dynamic>{
          'capabilities': <Map<String, dynamic>>[
            <String, dynamic>{'id': 'state.inventory'},
          ],
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });

  group('Method toJson behaves correctly', () {
    test('Method toJson matches the canonical capabilities-bridge fixture', () {
      const CapabilitiesPayload payload = CapabilitiesPayload(capabilities: <Capability>[]);

      expect(payload.toJson(), _readPayload('capabilities/capabilities-bridge.json'));
    });

    test('Method toJson matches the canonical capabilities-client fixture', () {
      const CapabilitiesPayload payload = CapabilitiesPayload(capabilities: <Capability>[]);

      expect(payload.toJson(), _readPayload('capabilities/capabilities-client.json'));
    });

    test('Method toJson encodes a non-empty capabilities list', () {
      const CapabilitiesPayload payload = CapabilitiesPayload(
        capabilities: <Capability>[Capability(id: 'state.inventory', version: 1)],
      );

      expect(payload.toJson(), <String, dynamic>{
        'capabilities': <Map<String, dynamic>>[
          <String, dynamic>{'id': 'state.inventory', 'version': 1},
        ],
      });
    });
  });
}
